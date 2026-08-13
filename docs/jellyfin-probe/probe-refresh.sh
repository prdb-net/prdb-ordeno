#!/usr/bin/env bash
# What makes Jellyfin re-read a sidecar that changed on disk?
#
# Two things could plausibly matter, and they are easy to confuse because a
# careless run varies both at once:
#
#   * how the edit reached the disk — rewriting the file in place leaves the
#     containing directory's timestamp alone, writing a temporary file and
#     renaming it over the original does not;
#   * how long after Jellyfin last saved the item the edit landed — the local
#     metadata provider ignores a sidecar whose modification time is within one
#     minute of that, so that its own writes do not read as external changes.
#
# So test all four combinations, and wait deliberately rather than by accident.
# Each case takes a bit over a minute; the whole run is a few minutes.
#
# Usage: ORDENO_FIXTURES=... probe-refresh.sh

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
BASE="${BASE:-http://127.0.0.1:8096}"
TOKEN=$(cat "$HERE/state/token")
USER_ID=$(cat "$HERE/state/userid")
FIXTURES="${ORDENO_FIXTURES:?set ORDENO_FIXTURES to the directory make-fixtures.sh wrote to}"
CASE_DIR="$FIXTURES/movies/Example Studio - 2025-11-15 - Refresh Probe"
NFO="$CASE_DIR/movie.nfo"

api() {
    curl -sS -X "${2:-GET}" "$BASE$1" \
        -H "Authorization: MediaBrowser Client=\"ordeno-probe\", Device=\"probe\", DeviceId=\"ordeno-probe-1\", Version=\"1.0.0\", Token=\"$TOKEN\""
}

current_title() {
    api "/Items?userId=$USER_ID&recursive=true&searchTerm=Refresh&fields=PremiereDate" \
        | jq -r '.Items[0] | .Name + "  (" + ((.PremiereDate // "no date")|.[0:10]) + ")"'
}

wait_scan() {
    for _ in $(seq 1 30); do
        [ "$(api /ScheduledTasks | jq -r '.[]|select(.Key=="RefreshLibrary")|.State')" != "Idle" ] && break
        sleep 1
    done
    for _ in $(seq 1 300); do
        [ "$(api /ScheduledTasks | jq -r '.[]|select(.Key=="RefreshLibrary")|.State')" = "Idle" ] && return 0
        sleep 2
    done
    echo "scan timed out" >&2; return 1
}

write_in_place() {
    python3 - "$NFO" "$1" <<'PY'
import sys, pathlib
p, title = pathlib.Path(sys.argv[1]), sys.argv[2]
import re
text = p.read_text(encoding="utf-8")
text = re.sub(r"<title>.*?</title>", f"<title>{title}</title>", text)
# Overwrite the existing file: the inode, and the directory entry pointing at
# it, both stay exactly as they were, so the directory's timestamp does not move.
with open(p, "r+", encoding="utf-8") as f:
    f.write(text)
    f.truncate()
PY
}

write_by_rename() {
    python3 - "$NFO" "$1" <<'PY'
import sys, pathlib, os, re
p, title = pathlib.Path(sys.argv[1]), sys.argv[2]
text = re.sub(r"<title>.*?</title>", f"<title>{title}</title>", p.read_text(encoding="utf-8"))
tmp = p.with_suffix(".nfo.tmp")
tmp.write_text(text, encoding="utf-8")
os.replace(tmp, p)
PY
}

run_case() {
    local label="$1" writer="$2" delay="$3" title="$4"
    # Make sure the clock starts from a known save: scan first, then wait.
    api /Library/Refresh POST >/dev/null; wait_scan
    if [ "$delay" -gt 0 ]; then
        echo "  waiting ${delay}s so the edit falls outside the tolerance window"
        sleep "$delay"
    fi
    "$writer" "$title"
    local dir_ts; dir_ts=$(stat -c %Y "$CASE_DIR")
    api /Library/Refresh POST >/dev/null; wait_scan
    local now; now=$(current_title)
    if [ "${now%% *}" = "Refresh" ] && [[ "$now" == *"$title"* ]]; then
        printf '%-42s %s\n' "$label" "PICKED UP    -> $now"
    else
        printf '%-42s %s\n' "$label" "ignored      -> $now"
    fi
}

echo "sidecar: $NFO"
echo "starting from: $(current_title)"
echo
printf '%-42s %s\n' "CASE" "RESULT"

run_case "in place, inside the one minute window"  write_in_place  0  "Refresh InPlace Inside"
run_case "in place, after waiting"                 write_in_place  70 "Refresh InPlace Outside"
run_case "by rename, inside the one minute window" write_by_rename 0  "Refresh Rename Inside"
run_case "by rename, after waiting"                write_by_rename 70 "Refresh Rename Outside"

# And the escape hatch: ask for the item directly, replacing what is stored.
write_in_place "Refresh Targeted"
ITEM=$(api "/Items?userId=$USER_ID&recursive=true&searchTerm=Refresh" | jq -r '.Items[0].Id')
api "/Items/$ITEM/Refresh?metadataRefreshMode=FullRefresh&imageRefreshMode=Default&replaceAllMetadata=true" POST >/dev/null
sleep 10
printf '%-42s %s\n' "targeted refresh, replace all, immediately" "-> $(current_title)"

echo
echo "the fixture sidecar has been edited; regenerate the fixtures before repeating"
