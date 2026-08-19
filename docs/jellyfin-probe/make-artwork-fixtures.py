#!/usr/bin/env python3
"""Fixtures for one question: what a media server does with a landscape image
in the Primary slot, which is the only kind of image prdb has.

Every image is the same recognisable pattern rather than a solid colour, so a
returned image can be read for what happened to it: three vertical stripes
(red, green, blue) with a white bar along the top edge and a yellow bar along
the bottom. Cropping a 16:9 image into a portrait box keeps the green centre
and loses red and blue; fitting it keeps all three and adds bars of its own;
stretching keeps all three and the top and bottom bars.
"""

import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

TARGET = Path(sys.argv[1])
MOVIES = TARGET / "movies"

# probe.sh declares a Home Videos library over the same fixture root and
# refuses to start without the directory, so it is made here rather than left
# as a step somebody has to know about.
HOMEVIDEOS = TARGET / "homevideos"

STRIPES = [(220, 30, 30), (30, 170, 60), (40, 80, 220)]
TOP = (255, 255, 255)
BOTTOM = (240, 220, 40)


def pattern(path: Path, width: int, height: int) -> None:
    image = Image.new("RGB", (width, height))
    draw = ImageDraw.Draw(image)

    for index, colour in enumerate(STRIPES):
        left = width * index // 3
        right = width * (index + 1) // 3
        draw.rectangle([left, 0, right - 1, height - 1], fill=colour)

    bar = max(4, height // 18)
    draw.rectangle([0, 0, width - 1, bar - 1], fill=TOP)
    draw.rectangle([0, height - bar, width - 1, height - 1], fill=BOTTOM)

    image.save(path, quality=95)


def video(path: Path) -> None:
    subprocess.run(
        ["ffmpeg", "-nostdin", "-loglevel", "error", "-y",
         "-f", "lavfi", "-i", "color=c=black:s=1920x1080:d=1:r=5",
         "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", str(path)],
        check=True)


def nfo(path: Path, title: str) -> None:
    path.write_text(
        '<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n'
        "<movie>\n"
        f"  <title>{title}</title>\n"
        "  <studio>Example Studio</studio>\n"
        "  <premiered>2025-11-03</premiered>\n"
        "</movie>\n",
        encoding="utf-8")


def case(name: str, images: dict[str, tuple[int, int]]) -> None:
    directory = MOVIES / name
    directory.mkdir(parents=True, exist_ok=True)
    video(directory / f"{name}.mkv")
    nfo(directory / "movie.nfo", name)

    for file_name, (width, height) in images.items():
        pattern(directory / file_name, width, height)

    print(f"  {name}: {', '.join(images) or 'no artwork'}")


LANDSCAPE = (1920, 1080)
PORTRAIT = (600, 900)

HOMEVIDEOS.mkdir(parents=True, exist_ok=True)

print("writing artwork fixtures")

# The case this probe exists for: prdb's images are the shape of the video, so
# the Primary slot gets a landscape image.
case("Aspect 01 landscape poster and fanart",
     {"poster.jpg": LANDSCAPE, "fanart.jpg": LANDSCAPE})

# The control. What the slot was designed for, measured the same way so the
# two are comparable rather than judged against an expectation.
case("Aspect 02 portrait poster and fanart",
     {"poster.jpg": PORTRAIT, "fanart.jpg": LANDSCAPE})

# The alternative that leaves Primary empty: a backdrop and nothing else.
case("Aspect 03 fanart only", {"fanart.jpg": LANDSCAPE})

# Section 5 measured that thumb.jpg fills the Thumb slot, which is the one
# shaped for a landscape image. Whether that reaches a card is the question.
case("Aspect 04 thumb and fanart",
     {"thumb.jpg": LANDSCAPE, "fanart.jpg": LANDSCAPE})

# Everything landscape, every slot that takes one filled.
case("Aspect 05 poster thumb and fanart",
     {"poster.jpg": LANDSCAPE, "thumb.jpg": LANDSCAPE, "fanart.jpg": LANDSCAPE})

# What absence looks like, so "no images" is a measured row rather than an
# assumption about the ones above.
case("Aspect 06 no artwork", {})

files = sum(1 for _ in TARGET.rglob("*") if _.is_file())
print(f"fixtures written to {TARGET}: {files} files")
