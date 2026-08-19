#!/usr/bin/env python3
"""What the server stores for each artwork fixture, and what it hands back when
a client asks for a particular size.

The half of the artwork question that can be answered through the API. Which
slot a file name fills is already in section 5 of docs/jellyfin-layout.md; this
asks the next thing, which is what happens to an image whose shape is not the
shape the slot expects. Run probe.sh setup and probe.sh scan first, against a
fixture root written by make-artwork-fixtures.py.

Usage: probe-artwork.py [outfile]     (BASE defaults to http://127.0.0.1:8096)
"""

import io
import json
import os
import sys
import urllib.request
from pathlib import Path

from PIL import Image

BASE = os.environ.get("BASE", "http://127.0.0.1:8096")
STATE = Path(__file__).resolve().parent / "state"
TOKEN = (STATE / "token").read_text().strip()
USER = (STATE / "userid").read_text().strip()
HEADER = ('MediaBrowser Client="ordeno-probe", Device="probe", '
          f'DeviceId="ordeno-probe-1", Version="1.0.0", Token="{TOKEN}"')

# The colours make-artwork-fixtures.py paints. A returned pixel is reported as
# the name of the nearest one, or as its raw value when it is near none of them
# — which is itself the answer when a server has added bars of its own.
NAMED = {
    (220, 30, 30): "red",
    (30, 170, 60): "green",
    (40, 80, 220): "blue",
    (255, 255, 255): "white",
    (240, 220, 40): "yellow",
    (0, 0, 0): "black",
}

# The shapes worth asking for: a portrait card box, a landscape one, and the two
# single-dimension forms, because which dimension a server honours is exactly
# what is in question.
REQUESTS = [
    ("original", ""),
    ("fill 300x450 (portrait card)", "?fillWidth=300&fillHeight=450"),
    ("fill 400x225 (landscape card)", "?fillWidth=400&fillHeight=225"),
    ("fillHeight=450 only", "?fillHeight=450"),
    ("maxWidth=300", "?maxWidth=300"),
]


def get(path: str) -> bytes:
    request = urllib.request.Request(BASE + path, headers={"Authorization": HEADER})
    with urllib.request.urlopen(request, timeout=30) as answer:
        return answer.read()


def name_of(pixel) -> str:
    red, green, blue = pixel[:3]
    best, distance = "?", 1 << 30
    for (r, g, b), label in NAMED.items():
        difference = (red - r) ** 2 + (green - g) ** 2 + (blue - b) ** 2
        if difference < distance:
            best, distance = label, difference
    return best if distance < 6000 else f"rgb{(red, green, blue)}"


def read(image: Image.Image) -> dict:
    """Five pixels that between them say what happened to the picture: the two
    outer stripes are gone if it was cropped, the top and bottom bars are gone
    if it was cropped the other way, and a colour near none of the five means
    something was added."""
    width, height = image.size
    middle = height // 2
    return {
        "size": f"{width}x{height}",
        "ratio": round(width / height, 3),
        "left": name_of(image.getpixel((width // 20, middle))),
        "centre": name_of(image.getpixel((width // 2, middle))),
        "right": name_of(image.getpixel((width - width // 20 - 1, middle))),
        "top": name_of(image.getpixel((width // 2, 1))),
        "bottom": name_of(image.getpixel((width // 2, height - 2))),
    }


def main() -> None:
    out = sys.argv[1] if len(sys.argv) > 1 else "observation-artwork.json"

    items = json.loads(get(
        f"/Items?userId={USER}&recursive=true&includeItemTypes=Movie"
        "&fields=PrimaryImageAspectRatio&enableImages=true").decode())

    report = []
    for item in sorted(items["Items"], key=lambda entry: entry["Name"]):
        slots = sorted((item.get("ImageTags") or {}).keys())
        backdrops = len(item.get("BackdropImageTags") or [])
        entry = {
            "name": item["Name"],
            "primaryImageAspectRatio": item.get("PrimaryImageAspectRatio"),
            "slots": slots,
            "backdrops": backdrops,
            "stored": json.loads(get(f"/Items/{item['Id']}/Images").decode()),
            "renders": {},
        }

        for slot in ("Primary", "Thumb", "Backdrop"):
            if slot == "Backdrop" and not backdrops:
                continue
            if slot != "Backdrop" and slot not in slots:
                continue
            for label, query in REQUESTS:
                path = f"/Items/{item['Id']}/Images/{slot}{query}"
                try:
                    entry["renders"][f"{slot} {label}"] = read(
                        Image.open(io.BytesIO(get(path))))
                except Exception as problem:  # noqa: BLE001 - the failure is the answer
                    entry["renders"][f"{slot} {label}"] = {"error": str(problem)}

        report.append(entry)

    Path(out).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out}: {len(report)} items")


if __name__ == "__main__":
    main()
