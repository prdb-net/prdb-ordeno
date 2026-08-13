#!/usr/bin/env bash
# Drive a running Jellyfin through its own API and record what it made of the
# fixture library. Everything here is a question about observed behaviour, so
# the output is the raw answer Jellyfin gave rather than a verdict.
#
# Usage: probe.sh setup | scan | dump <outfile> | edit-sidecar | refresh <mode>
#                 | show-refresh | all

set -euo pipefail

BASE="${BASE:-http://127.0.0.1:8096}"
USER_NAME="probe"
USER_PASS="probe-password"
CLIENT_HDR='MediaBrowser Client="ordeno-probe", Device="probe", DeviceId="ordeno-probe-1", Version="1.0.0"'
HERE="$(cd "$(dirname "$0")" && pwd)"
STATE_DIR="$HERE/state"
TOKEN_FILE="$STATE_DIR/token"
USERID_FILE="$STATE_DIR/userid"
FIXTURES="${ORDENO_FIXTURES:?set ORDENO_FIXTURES to the directory make-fixtures.sh wrote to}"

mkdir -p "$STATE_DIR"

auth_header() {
    if [ -s "$TOKEN_FILE" ]; then
        printf '%s, Token="%s"' "$CLIENT_HDR" "$(cat "$TOKEN_FILE")"
    else
        printf '%s' "$CLIENT_HDR"
    fi
}

api() {
    # api METHOD PATH [json-body]
    local method="$1" path="$2" body="${3:-}"
    if [ -n "$body" ]; then
        curl -sS -X "$method" "$BASE$path" \
            -H "Authorization: $(auth_header)" \
            -H "Content-Type: application/json" \
            --data "$body"
    else
        curl -sS -X "$method" "$BASE$path" -H "Authorization: $(auth_header)"
    fi
}

wait_for_server() {
    # A 200 from this endpoint is not enough: while the server is still coming
    # up it answers with a body that is not yet valid JSON, and every step
    # after this one then fails in a way that looks like an API problem.
    # Wait for a parseable version string instead.
    echo "waiting for $BASE ..."
    local body version
    for _ in $(seq 1 150); do
        body=$(curl -sS --max-time 5 "$BASE/System/Info/Public" 2>/dev/null || true)
        version=$(printf '%s' "$body" | jq -r '.Version // empty' 2>/dev/null || true)
        if [ -n "$version" ] && [ "$version" != "null" ]; then
            echo "server is up: $version"
            return 0
        fi
        sleep 2
    done
    echo "server did not come up; last body was: $body" >&2
    return 1
}

startup_wizard_pending() {
    curl -sS "$BASE/System/Info/Public" | jq -e '.StartupWizardCompleted == false' >/dev/null 2>&1
}

cmd_setup() {
    wait_for_server

    if startup_wizard_pending; then
        echo "running the startup wizard"
        api POST /Startup/Configuration \
            '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
        api GET /Startup/User >/dev/null
        api POST /Startup/User \
            "$(jq -nc --arg n "$USER_NAME" --arg p "$USER_PASS" '{Name:$n,Password:$p}')" >/dev/null
        api POST /Startup/RemoteAccess \
            '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
        api POST /Startup/Complete >/dev/null
        echo "wizard complete"
    else
        echo "wizard already completed"
    fi

    echo "authenticating"
    local auth
    auth=$(curl -sS -X POST "$BASE/Users/AuthenticateByName" \
        -H "Authorization: $CLIENT_HDR" \
        -H "Content-Type: application/json" \
        --data "$(jq -nc --arg u "$USER_NAME" --arg p "$USER_PASS" '{Username:$u,Pw:$p}')")
    echo "$auth" | jq -r '.AccessToken' > "$TOKEN_FILE"
    echo "$auth" | jq -r '.User.Id'     > "$USERID_FILE"
    echo "token acquired for user $(cat "$USERID_FILE")"

    add_library "Probe Movies"      movies     /fixtures/movies     Movie
    add_library "Probe Home Videos" homevideos /fixtures/homevideos Video
}

add_library() {
    local name="$1" collection="$2" path="$3" type="$4"

    if api GET /Library/VirtualFolders | jq -e --arg n "$name" '.[] | select(.Name == $n)' >/dev/null 2>&1; then
        echo "library '$name' already exists"
        return
    fi

    # Remote providers are off so that anything the item ends up carrying came
    # from the sidecar or from the file, and nothing was fetched from the
    # internet. SaveLocalMetadata stays off so Jellyfin writes nothing back
    # into the fixtures, which the read-only mount enforces anyway.
    local options
    options=$(jq -nc --arg type "$type" '{
        LibraryOptions: {
            Enabled: true,
            EnableInternetProviders: false,
            SaveLocalMetadata: false,
            EnableRealtimeMonitor: false,
            EnableEmbeddedTitles: false,
            EnableChapterImageExtraction: false,
            ExtractChapterImagesDuringLibraryScan: false,
            EnableTrickplayImageExtraction: false,
            ExtractTrickplayImagesDuringLibraryScan: false,
            MetadataSavers: [],
            LocalMetadataReaderOrder: ["Nfo"],
            TypeOptions: [{
                Type: $type,
                MetadataFetchers: [],
                MetadataFetcherOrder: [],
                ImageFetchers: [],
                ImageFetcherOrder: []
            }]
        }
    }')

    echo "adding library '$name' ($collection) -> $path"
    curl -sS -X POST \
        "$BASE/Library/VirtualFolders?name=$(jq -rn --arg v "$name" '$v|@uri')&collectionType=$collection&paths=$(jq -rn --arg v "$path" '$v|@uri')&refreshLibrary=false" \
        -H "Authorization: $(auth_header)" \
        -H "Content-Type: application/json" \
        --data "$options"
    echo
}

scan_state() {
    api GET /ScheduledTasks | jq -r '.[] | select(.Key == "RefreshLibrary") | .State'
}

wait_for_scan() {
    # Waiting for "Idle" alone is a race: right after the trigger the task has
    # usually not started yet, so the first poll reports Idle and the caller
    # carries on against a library that was never rescanned. Wait for the task
    # to start first, and only then for it to finish.
    echo -n "scanning"
    local started=0
    for _ in $(seq 1 30); do
        if [ "$(scan_state)" != "Idle" ]; then started=1; break; fi
        echo -n ","
        sleep 1
    done
    if [ "$started" -eq 0 ]; then
        echo " the scan never started" >&2
        return 1
    fi
    for _ in $(seq 1 300); do
        if [ "$(scan_state)" = "Idle" ]; then
            echo " done"
            return 0
        fi
        echo -n "."
        sleep 2
    done
    echo " timed out" >&2
    return 1
}

cmd_scan() {
    api POST /Library/Refresh >/dev/null
    wait_for_scan
}

cmd_dump() {
    local out="${1:?usage: probe.sh dump <outfile>}"
    local uid; uid=$(cat "$USERID_FILE")
    local fields="Path,Studios,Genres,Tags,Overview,PremiereDate,ProductionYear,SortName,OriginalTitle,MediaSources,ProviderIds,People,DateCreated,ParentId"

    {
        echo "{"
        echo '"serverVersion":' "$(curl -sS "$BASE/System/Info/Public" | jq '.Version')" ","
        echo '"libraries":' "$(api GET /Library/VirtualFolders | jq '[.[] | {Name, CollectionType, Locations, ItemId}]')" ","
        echo '"items":' "$(api GET "/Items?userId=$uid&recursive=true&fields=$fields&enableImages=true&enableUserData=false" \
            | jq '[.Items[]?] | map({
                    Name, Type, Id, Path, SortName, OriginalTitle,
                    PremiereDate, ProductionYear, Overview,
                    Studios: [.Studios[]?.Name],
                    Genres, Tags, ProviderIds,
                    People: [.People[]? | {Name, Role, Type, PrimaryImageTag}],
                    ImageTags: (.ImageTags // {} | keys),
                    BackdropCount: (.BackdropImageTags // [] | length),
                    MediaSourceCount: (.MediaSources // [] | length),
                    MediaSources: [.MediaSources[]? | {Name, Path, Width: ([.MediaStreams[]? | select(.Type=="Video") | .Width] | first)}]
                  })')" ","
        echo '"persons":' "$(api GET "/Persons?userId=$uid" | jq '[.Items[]? | {Name, Id, ImageTags: (.ImageTags // {} | keys)}]')" ","
        echo '"studios":' "$(api GET "/Studios?userId=$uid" | jq '[.Items[]? | .Name]')"
        echo "}"
    } | jq '.' > "$out"

    echo "wrote $out"
    jq -r '"server " + .serverVersion + ", " + (.items | length | tostring) + " items, " + (.persons | length | tostring) + " persons"' "$out"
}

cmd_edit_sidecar() {
    # The refresh question: change a sidecar on disk after the first scan and
    # find out what makes Jellyfin notice. The fixture mount is read-only to
    # the container, so the edit happens here, on the host.
    local f="$FIXTURES/movies/Example Studio - 2025-11-15 - Refresh Probe/movie.nfo"
    sed -i 's|<title>Refresh Before The Edit</title>|<title>Refresh After The Edit</title>|' "$f"
    sed -i 's|<premiered>2025-11-15</premiered>|<premiered>2020-02-02</premiered>|' "$f"
    touch "$f"
    echo "edited $f"
    grep -E '<title>|<premiered>' "$f"
}

cmd_refresh() {
    local mode="${1:-library}"
    local uid; uid=$(cat "$USERID_FILE")
    case "$mode" in
        library)
            echo "triggering a plain library scan"
            cmd_scan
            ;;
        item)
            local id
            id=$(api GET "/Items?userId=$uid&recursive=true&searchTerm=Refresh" | jq -r '.Items[0].Id')
            echo "triggering a targeted refresh of item $id"
            api POST "/Items/$id/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=FullRefresh&replaceAllMetadata=true&replaceAllImages=false" >/dev/null
            sleep 8
            ;;
        *)
            echo "unknown refresh mode: $mode" >&2; exit 1 ;;
    esac
}

cmd_show_refresh_probe() {
    local uid; uid=$(cat "$USERID_FILE")
    api GET "/Items?userId=$uid&recursive=true&searchTerm=Refresh&fields=Path,PremiereDate,DateLastSaved" \
        | jq '[.Items[]? | {Name, PremiereDate, Path}]'
}

case "${1:?usage: probe.sh setup|scan|dump <out>|edit-sidecar|refresh <mode>|show-refresh|all}" in
    setup)         cmd_setup ;;
    scan)          cmd_scan ;;
    dump)          shift; cmd_dump "$@" ;;
    edit-sidecar)  cmd_edit_sidecar ;;
    refresh)       shift; cmd_refresh "$@" ;;
    show-refresh)  cmd_show_refresh_probe ;;
    all)
        cmd_setup
        cmd_scan
        cmd_dump "$STATE_DIR/observation-initial.json"
        ;;
    *) echo "unknown command: $1" >&2; exit 1 ;;
esac
