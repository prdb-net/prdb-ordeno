#!/usr/bin/env bash
# The tool knows a path. Jellyfin's targeted refresh wants an item id. What is
# in between?
#
# probe-refresh.sh established that a self-written sidecar change only appears
# immediately if the item is asked for directly, and that endpoint is
# POST /Items/{id}/Refresh. Nothing in this tool ever sees that id: what it has
# is the directory it just wrote into. This script measures the ways from one to
# the other, and what happens when the path the tool knows is not the path
# Jellyfin knows — which is the normal case, because both run in containers with
# their own mounts.
#
# It also measures the two things that decide whether an optional connection is
# worth having at all: whether a plain API key can reach these endpoints, and
# whether the release date format from section 4 can be read back.
#
# Run after probe.sh setup && probe.sh scan. Like probe-refresh.sh it edits a
# fixture sidecar, so regenerate the fixtures before repeating.
#
# Usage: ORDENO_FIXTURES=... probe-itemid.sh

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
BASE="${BASE:-http://127.0.0.1:8096}"
TOKEN=$(cat "$HERE/state/token")
USER_ID=$(cat "$HERE/state/userid")
FIXTURES="${ORDENO_FIXTURES:?set ORDENO_FIXTURES to the directory make-fixtures.sh wrote to}"

CASE_NAME="Example Studio - 2025-11-15 - Refresh Probe"
HOST_DIR="$FIXTURES/movies/$CASE_NAME"
HOST_NFO="$HOST_DIR/movie.nfo"
# The same directory as the container sees it. docker-compose.yml mounts
# $ORDENO_FIXTURES at /fixtures, so this is the path substitution every real
# deployment has and no configuration anywhere states.
CONTAINER_DIR="/fixtures/movies/$CASE_NAME"

hdr_user() {
    printf 'MediaBrowser Client="ordeno-probe", Device="probe", DeviceId="ordeno-probe-1", Version="1.0.0", Token="%s"' "$TOKEN"
}

api() {
    # api PATH [METHOD] [body]
    curl -sS -X "${2:-GET}" "$BASE$1" \
        -H "Authorization: $(hdr_user)" \
        -H "Content-Type: application/json" \
        ${3:+--data "$3"}
}

status_with_key() {
    # status_with_key KEY PATH [METHOD] [body] -> HTTP status only
    curl -sS -o /dev/null -w '%{http_code}' -X "${3:-GET}" "$BASE$2" \
        -H "Authorization: MediaBrowser Token=\"$1\"" \
        -H "Content-Type: application/json" \
        ${4:+--data "$4"}
}

body_with_key() {
    curl -sS -X "${3:-GET}" "$BASE$2" \
        -H "Authorization: MediaBrowser Token=\"$1\"" \
        -H "Content-Type: application/json" \
        ${4:+--data "$4"}
}

set_title() {
    python3 - "$HOST_NFO" "$1" <<'PY'
import re, sys, pathlib
p, title = pathlib.Path(sys.argv[1]), sys.argv[2]
p.write_text(re.sub(r"<title>.*?</title>", f"<title>{title}</title>",
                    p.read_text(encoding="utf-8")), encoding="utf-8")
PY
}

current_title() {
    # By id, not by name: the cases below change the name, and a lookup by
    # search term would stop finding the item it is watching.
    api "/Items?userId=$USER_ID&ids=$ITEM" | jq -r '.Items[0].Name // "not found"'
}

report_media_updated() {
    # report_media_updated PATH -> HTTP status
    local path="$1"
    curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/Library/Media/Updated" \
        -H "Authorization: $(hdr_user)" \
        -H "Content-Type: application/json" \
        --data "$(jq -nc --arg p "$path" '{Updates:[{Path:$p,UpdateType:"Modified"}]}')"
}

rule() { printf '\n== %s\n\n' "$1"; }

# ---------------------------------------------------------------------------
rule "1. What path does an item carry?"

api "/Items?userId=$USER_ID&recursive=true&includeItemTypes=Movie&fields=Path,MediaSources&searchTerm=Refresh" \
    | jq '.Items[0] | {Name, Id, Path, MediaSourcePaths: [.MediaSources[]?.Path]}'

# ---------------------------------------------------------------------------
rule "2. Finding that item from a path"

ALL=$(api "/Items?userId=$USER_ID&recursive=true&includeItemTypes=Movie&fields=Path")
echo "enumerate everything and match client-side:"
echo "  movies returned: $(echo "$ALL" | jq '.Items | length')"
echo "  response bytes:  $(echo "$ALL" | wc -c)"
MATCH=$(echo "$ALL" | jq -r --arg d "$CONTAINER_DIR" '[.Items[] | select(.Path | startswith($d))] | .[0].Id // "no match"')
echo "  match on the container path prefix: $MATCH"

# The tool does not know the container path. It knows the tail — the site
# directory, the scene directory and the file name are identical on both sides,
# because only the mount prefix differs. Match on that instead.
TAIL="/movies/$CASE_NAME/$CASE_NAME.mkv"
MATCH_TAIL=$(echo "$ALL" | jq -r --arg t "$TAIL" '[.Items[] | select(.Path | endswith($t))] | .[0].Id // "no match"')
echo "  match on the path tail ($TAIL): $MATCH_TAIL"
echo "  ... and the prefix that implies: $(echo "$ALL" | jq -r --arg t "$TAIL" '[.Items[] | select(.Path | endswith($t)) | .Path[:-($t|length)]] | .[0] // "-"')"

echo
echo "ask the server to filter by path instead:"
BY_PATH=$(api "/Items?userId=$USER_ID&recursive=true&includeItemTypes=Movie&fields=Path&path=$(jq -rn --arg v "$CONTAINER_DIR" '$v|@uri')")
echo "  items returned for one exact path: $(echo "$BY_PATH" | jq '.Items | length')"

echo
echo "search by the scene directory name:"
BY_NAME=$(api "/Items?userId=$USER_ID&recursive=true&includeItemTypes=Movie&fields=Path&searchTerm=$(jq -rn --arg v "$CASE_NAME" '$v|@uri')")
echo "  items returned: $(echo "$BY_NAME" | jq '.Items | length')"
echo "$BY_NAME" | jq -r '.Items[] | "    " + .Name'

ITEM="$MATCH_TAIL"
[ "$ITEM" = "no match" ] && { echo "the tail match found nothing; the rest of this script needs it" >&2; exit 1; }

# ---------------------------------------------------------------------------
rule "3. Reporting a changed path instead of refreshing an item"

# POST /Library/Media/Updated takes a path rather than an item id, which would
# make the whole lookup above unnecessary. Whether it does anything is the
# question, and it has the same confound probe-refresh.sh was built to avoid:
# a report that is ignored because the edit sat inside the one-minute window
# looks exactly like a report that is ignored because the path meant nothing.
#
# So every case here waits past the window first, and nothing triggers a scan
# afterwards. If the title changes, the report is what changed it.
#
# The endpoint hands the path to the library monitor, and the probe libraries
# were created with real-time monitoring off so that nothing could refresh
# behind the observation's back. That is exactly the setting this endpoint
# might depend on, so every case runs twice, once in each state. The monitor
# also batches what it is told rather than acting on it at once, so the wait
# after a report is long enough for that batch to have been processed.

SETTLE="${SETTLE:-60}"

wait_for_scan() {
    for _ in $(seq 1 60); do
        [ "$(api /ScheduledTasks | jq -r '.[]|select(.Key=="RefreshLibrary")|.State')" = "Idle" ] && return 0
        sleep 2
    done
}

set_realtime() {
    # set_realtime true|false — on the Movies library the case above lives in.
    local want="$1" folder
    folder=$(api /Library/VirtualFolders | jq -c --arg n "Probe Movies" '.[] | select(.Name == $n)')
    api /Library/VirtualFolders/LibraryOptions POST \
        "$(echo "$folder" | jq -c --argjson w "$want" \
            '{Id: .ItemId, LibraryOptions: (.LibraryOptions | .EnableRealtimeMonitor = $w)}')" >/dev/null
    local now
    now=$(api /Library/VirtualFolders | jq -r --arg n "Probe Movies" '.[] | select(.Name == $n) | .LibraryOptions.EnableRealtimeMonitor')
    echo "  EnableRealtimeMonitor is now: $now"
}

report_case() {
    # report_case LABEL PATH TITLE [inside]
    #
    # Each case carries its own control. The monitor does not act on what it is
    # told at once, it collects and processes it later, so a report from the
    # previous case can land in the middle of this one and be read as this
    # case's result — which is how the first run of this script produced a
    # single unreproducible hit. The control is the same wait with no report in
    # it: if the title has already moved before this case reported anything,
    # something else refreshed the item and the case says nothing.
    local label="$1" path="$2" title="$3" inside="${4:-}"
    api /Library/Refresh POST >/dev/null; wait_for_scan
    [ -z "$inside" ] && sleep 70
    set_title "$title"

    sleep "$SETTLE"
    if [ "$(current_title)" = "$title" ]; then
        printf '  %-46s %s\n' "$label" "VOID — refreshed before anything was reported"
        return
    fi

    local status; status=$(report_media_updated "$path")
    sleep "$SETTLE"
    local now; now=$(current_title)
    if [ "$now" = "$title" ]; then
        printf '  %-46s HTTP %s   PICKED UP\n' "$label" "$status"
    else
        printf '  %-46s HTTP %s   ignored -> %s\n' "$label" "$status" "$now"
    fi
}

# Which cases to run, for repeating one of them without sitting through the
# rest: ORDENO_CASES="mkv-out" probe-itemid.sh, ORDENO_MODES="off".
CASES="${ORDENO_CASES:-dir-out nfo-out mkv-out host-out mkv-in}"
MODES="${ORDENO_MODES:-off on}"
wants() { [[ " $CASES " == *" $1 "* ]]; }

report_cases() {
    wants dir-out  && report_case "scene directory, outside the window" "$CONTAINER_DIR"            "Media Updated Dir$1"
    wants nfo-out  && report_case "the sidecar file, outside the window" "$CONTAINER_DIR/movie.nfo" "Media Updated Nfo$1"
    wants mkv-out  && report_case "the video file, outside the window" "$CONTAINER_DIR/$CASE_NAME.mkv" "Media Updated Mkv$1"
    wants host-out && report_case "host path, outside the window" "$HOST_DIR"                      "Media Updated Host Path$1"
    wants mkv-in   && report_case "the video file, inside the window" "$CONTAINER_DIR/$CASE_NAME.mkv" "Media Updated Inside$1" inside
    return 0
}

echo "starting from: $(current_title)"
for mode in $MODES; do
    echo
    echo "with real-time monitoring $mode:"
    [ "$mode" = "on" ] && set_realtime true || set_realtime false
    report_cases " ${mode^}"
done
set_realtime false

echo
echo "and the targeted refresh by item id, for comparison, inside the window:"
api /Library/Refresh POST >/dev/null; wait_for_scan
set_title "Refresh Via Item Id"
api "/Items/$ITEM/Refresh?metadataRefreshMode=FullRefresh&replaceAllMetadata=true" POST >/dev/null
sleep 15
echo "  title now: $(current_title)"

# ---------------------------------------------------------------------------
rule "4. What the server says about its own paths"

echo "GET /Library/VirtualFolders -> Locations:"
api /Library/VirtualFolders | jq -r '.[] | "  " + .Name + ": " + (.Locations | join(", "))'
echo "GET /Library/PhysicalPaths:"
api /Library/PhysicalPaths | jq -r '.[] | "  " + .'

# ---------------------------------------------------------------------------
rule "5. Can a plain API key do any of this?"

api "/Auth/Keys?app=ordeno-probe" POST >/dev/null || true
KEY=$(api /Auth/Keys | jq -r '.Items[] | select(.AppName == "ordeno-probe") | .AccessToken' | head -1)
echo "created API key: ${KEY:0:8}…"
echo

printf '  %-58s %s\n' "ENDPOINT" "STATUS"
printf '  %-58s %s\n' "GET  /System/Info/Public" "$(status_with_key "$KEY" /System/Info/Public)"
printf '  %-58s %s\n' "GET  /System/Info" "$(status_with_key "$KEY" /System/Info)"
printf '  %-58s %s\n' "GET  /Library/VirtualFolders" "$(status_with_key "$KEY" /Library/VirtualFolders)"
printf '  %-58s %s\n' "GET  /Items?recursive=true&fields=Path (no userId)" "$(status_with_key "$KEY" "/Items?recursive=true&includeItemTypes=Movie&fields=Path")"
printf '  %-58s %s\n' "POST /Items/{id}/Refresh" "$(status_with_key "$KEY" "/Items/$ITEM/Refresh?metadataRefreshMode=FullRefresh&replaceAllMetadata=true" POST)"
printf '  %-58s %s\n' "POST /Library/Media/Updated" "$(status_with_key "$KEY" /Library/Media/Updated POST "$(jq -nc --arg p "$CONTAINER_DIR" '{Updates:[{Path:$p,UpdateType:"Modified"}]}')")"
printf '  %-58s %s\n' "GET  /System/Configuration/xbmcmetadata" "$(status_with_key "$KEY" /System/Configuration/xbmcmetadata)"

echo
echo "items an API key sees without a user id: $(body_with_key "$KEY" "/Items?recursive=true&includeItemTypes=Movie&fields=Path" | jq '.Items | length')"

# ---------------------------------------------------------------------------
rule "6. The release date format, read back"

echo "GET /System/Configuration/xbmcmetadata:"
body_with_key "$KEY" /System/Configuration/xbmcmetadata | jq '.'

echo
echo "done. The fixture sidecar has been edited; regenerate the fixtures before repeating."
