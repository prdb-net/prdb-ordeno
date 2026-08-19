#!/usr/bin/env bash
# What a client draws, as opposed to what the server stores.
#
# probe-artwork.py answers everything the API can be asked. It cannot answer the
# question that decides whether an image is worth writing at all: the server
# never crops — it honours a requested dimension and leaves the aspect ratio
# alone — so how an image ends up on a card is entirely the client's doing, in
# its own JavaScript and CSS. Reading that out of a minified bundle is guessing
# with extra steps. This loads the web client Jellyfin ships, in the browser
# this machine has, and reads the page that came out.
#
# What it collects:
#   - the card class the library grid used, which is the shape decision;
#   - the image URL each card asked for, which is where the requested size
#     turns out to be derived from the image's own aspect ratio;
#   - a screenshot, and a reading of the stripe proportions across each card.
#
# Usage: probe-artwork-cards.sh [output-directory]
#
# Requires a Jellyfin already set up and scanned by probe.sh against fixtures
# from make-artwork-fixtures.py, plus chromium and python3 with Pillow.

set -euo pipefail

BASE="${BASE:-http://127.0.0.1:8096}"
CONTAINER="${ORDENO_PROBE_CONTAINER:-ordeno-jellyfin-probe}"
HERE="$(cd "$(dirname "$0")" && pwd)"
STATE_DIR="$HERE/state"

# Under $HOME by default and not in a temporary directory: chromium is commonly
# a confined snap, which silently fails to write a screenshot anywhere outside
# the user's home.
OUT="${1:-$HOME/ordeno-artwork-cards}"

CHROMIUM="${ORDENO_CHROMIUM:-$(command -v chromium || command -v chromium-browser || command -v google-chrome)}"
[ -n "$CHROMIUM" ] || { echo "no chromium on PATH; set ORDENO_CHROMIUM" >&2; exit 1; }

TOKEN="$(cat "$STATE_DIR/token")"
USER_ID="$(cat "$STATE_DIR/userid")"
AUTH="MediaBrowser Client=\"ordeno-probe\", Device=\"probe\", DeviceId=\"ordeno-probe-1\", Version=\"1.0.0\", Token=\"$TOKEN\""

mkdir -p "$OUT/profile" "$OUT/shots"

library=$(curl -sS "$BASE/Library/VirtualFolders" -H "Authorization: $AUTH" \
    | jq -r '.[] | select(.CollectionType == "movies") | .ItemId' | head -1)
[ -n "$library" ] || { echo "no movies library found" >&2; exit 1; }
echo "movies library $library"

# The client keeps its credentials in localStorage, which is per origin, so the
# page that writes them has to be served by the same server. It is copied into
# the container's web root: the container is a throwaway and is torn down with
# `docker compose down -v` afterwards.
cat > "$OUT/seed.html" <<'HTML'
<!doctype html><meta charset="utf-8"><title>seed</title><body>seeding…<script>
(async () => {
  const base = location.origin;
  const header = 'MediaBrowser Client="Jellyfin Web", Device="Chromium", DeviceId="probe-cards-1", Version="10.11.11"';
  const auth = await (await fetch(base + '/Users/AuthenticateByName', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Authorization': header },
    body: JSON.stringify({ Username: 'probe', Pw: 'probe-password' })
  })).json();
  const info = await (await fetch(base + '/System/Info/Public')).json();
  localStorage.setItem('jellyfin_credentials', JSON.stringify({ Servers: [{
    Id: info.Id, Name: info.ServerName, ManualAddress: base,
    AccessToken: auth.AccessToken, UserId: auth.User.Id,
    DateLastAccessed: Date.now(), LastConnectionMode: 2, Type: 'Server'
  }]}));
  document.body.textContent = 'seeded ' + info.Id;
})();
</script></body>
HTML
docker cp "$OUT/seed.html" "$CONTAINER:/jellyfin/jellyfin-web/seed.html" >/dev/null
echo "seed page installed in $CONTAINER"

chromium_run() {
    # A persistent profile, because the credentials written by one run are what
    # the next one logs in with. --virtual-time-budget rather than a sleep: the
    # page is a single-page application that fetches its items after load.
    timeout 180 "$CHROMIUM" --headless=new --no-sandbox --disable-gpu \
        --user-data-dir="$OUT/profile" --window-size=1400,1200 --hide-scrollbars \
        --virtual-time-budget=30000 "$@" 2>/dev/null
}

chromium_run --dump-dom "$BASE/web/seed.html" | grep -o 'seeded [0-9a-f]*' | head -1

chromium_run --dump-dom "$BASE/web/#/movies.html?topParentId=$library" > "$OUT/movies-dom.html"
chromium_run --screenshot="$OUT/shots/movies.png" "$BASE/web/#/movies.html?topParentId=$library"

echo
echo "card shapes:"
grep -o 'class="card [^"]*"' "$OUT/movies-dom.html" | sort | uniq -c

echo
echo "what each card asked the server for:"
python3 - "$OUT/movies-dom.html" <<'PY'
import html
import re
import sys

document = open(sys.argv[1], encoding="utf-8").read()
for part in document.split('class="card ')[1:]:
    card = part[:4000]
    shape = card.split('"')[0].split()[0]
    title = re.search(r'>([A-Za-z][^<>]{5,60})</', card)
    url = re.search(r'(/Items/[0-9a-f]+/Images/[A-Za-z]+\?[^"\\]*?)(?:&quot;|")',
                    html.unescape(card))
    name = title.group(1).strip() if title else "?"
    asked = url.group(1).split("&quality")[0] if url else "no image"
    print(f"  {shape:14} {name:44} {asked}")
PY

echo
echo "stripes across each card, as drawn:"
python3 - "$OUT/shots/movies.png" <<'PY'
import sys

from PIL import Image

# The card row's geometry in the page this script renders: a fixed window size
# and a fixed fixture set make it deterministic. A card whose image is one flat
# colour has no stripes to read and is skipped.
TOP, HEIGHT, WIDTH, FIRST, PITCH = 192, 233, 160, 46, 178

PURE = {"red": (220, 30, 30), "green": (30, 170, 60), "blue": (40, 80, 220)}

image = Image.open(sys.argv[1]).convert("RGB")


def classify(pixel):
    for name, colour in PURE.items():
        if sum((a - b) ** 2 for a, b in zip(pixel, colour)) < 2500:
            return name
    return None


for index in range(6):
    left = FIRST + index * PITCH
    if left + WIDTH > image.width:
        break
    row = [classify(image.getpixel((x, TOP + HEIGHT // 2)))
           for x in range(left, left + WIDTH)]
    counts = {name: row.count(name) for name in PURE}
    total = sum(counts.values())
    if not total:
        print(f"  card {index + 1}: no stripes (no artwork)")
        continue
    # Pixels belonging to no pure colour are the edges between two stripes:
    # sharp where the image arrived at the size it is drawn at, wide where it
    # was resampled up from a smaller one.
    between = len(row) - total
    print(f"  card {index + 1}: "
          f"{counts['red'] / total:.0%} / {counts['green'] / total:.0%} / "
          f"{counts['blue'] / total:.0%}   between colours: {between} of {WIDTH}")
PY

echo
echo "screenshot and page in $OUT"
