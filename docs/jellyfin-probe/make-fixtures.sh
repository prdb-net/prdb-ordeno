#!/usr/bin/env bash
# Lay out a fixture library that puts every question in issue #1 on disk in a
# form Jellyfin can be asked about. Each case is named for the question it
# answers, so the case name can be quoted in the specification next to what
# Jellyfin made of it.
#
# Usage: make-fixtures.sh <target-directory>

set -euo pipefail

target="${1:?usage: make-fixtures.sh <target-directory>}"
movies="$target/movies"
homevideos="$target/homevideos"

# Clear the contents rather than the directory itself. The fixture root is bind
# mounted into the running container, and removing it would leave that mount
# pointing at a deleted inode: the container would go on reading a frozen copy
# of the old tree while every edit here landed somewhere it could not see. That
# failure is silent and looks exactly like Jellyfin refusing to notice changes.
mkdir -p "$target"
find "$target" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
mkdir -p "$movies" "$homevideos"

# --- the video files ----------------------------------------------------
# One second of black at two resolutions, so that "two versions of the same
# scene" is a real difference Jellyfin can read out of the files rather than
# a claim made in a filename.

tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT

make_video() {
    local out="$1" size="$2"
    ffmpeg -nostdin -loglevel error -y \
        -f lavfi -i "color=c=black:s=$size:d=1:r=5" \
        -c:v libx264 -preset ultrafast -pix_fmt yuv420p \
        "$out"
}

make_video "$tmp/1080.mkv" 1920x1080
make_video "$tmp/2160.mkv" 3840x2160

video()   { cp "$tmp/1080.mkv" "$1"; }
video4k() { cp "$tmp/2160.mkv" "$1"; }

# --- artwork ------------------------------------------------------------
# Distinguishable solid colours, so that which file became which image is
# visible rather than inferred.

image() {
    local out="$1" colour="$2" size="$3"
    ffmpeg -nostdin -loglevel error -y \
        -f lavfi -i "color=c=$colour:s=$size" -frames:v 1 "$out"
}

# --- nfo helpers --------------------------------------------------------

# A title is arbitrary text and an .nfo is XML, so the text has to be escaped
# on the way in. The first run of this generator did not do that, and the three
# cases whose titles contained '&', '<' and '>' produced an unparseable sidecar
# that Jellyfin discarded in silence — which is worth remembering as a
# requirement on the writer rather than a quirk of the fixtures.
xml_escape() {
    printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g'
}

nfo_full() {
    # $1 target path, $2 title, $3 premiered
    local t; t=$(xml_escape "$2")
    cat > "$1" <<XML
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>$t</title>
  <originaltitle>$t (original)</originaltitle>
  <sorttitle>$t (sort)</sorttitle>
  <plot>Plot text written by the fixture generator.</plot>
  <premiered>$3</premiered>
  <studio>Example Studio</studio>
  <genre>Fixture Genre</genre>
  <tag>fixture-tag</tag>
  <runtime>1</runtime>
  <uniqueid type="prdb" default="true">prdb-0001</uniqueid>
  <actor>
    <name>Full Shape Performer</name>
    <role>Performer</role>
    <type>Actor</type>
    <order>0</order>
  </actor>
  <actor>
    <name>Name Only Performer</name>
  </actor>
</movie>
XML
}

nfo_title_only() {
    # $1 target path, $2 title
    local t; t=$(xml_escape "$2")
    cat > "$1" <<XML
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>$t</title>
  <studio>Example Studio</studio>
</movie>
XML
}

say() { printf '  %s\n' "$1"; }

# =======================================================================
# 1. Baseline: everything present, in the shape VISION.md sketches.
# =======================================================================
echo "case 01 baseline"
d="$movies/Example Studio - 2025-11-03 - Baseline Complete"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-03 - Baseline Complete.mkv"
nfo_full "$d/movie.nfo" "Baseline Complete" "2025-11-03"
image "$d/poster.jpg"    red    600x900
image "$d/fanart.jpg"    blue   1920x1080
image "$d/logo.png"      green  400x200
image "$d/landscape.jpg" yellow 1000x562
image "$d/clearart.png"  purple 500x500
image "$d/banner.jpg"    orange 1000x185
image "$d/thumb.jpg"     cyan   600x340
say "one folder, movie.nfo, and one image of every documented name"

# =======================================================================
# 2. Which .nfo wins when both candidates exist.
# =======================================================================
echo "case 02 nfo precedence"
d="$movies/Example Studio - 2025-11-04 - Nfo Precedence"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-04 - Nfo Precedence.mkv"
nfo_title_only "$d/movie.nfo" "TITLE FROM movie dot nfo"
nfo_title_only "$d/Example Studio - 2025-11-04 - Nfo Precedence.nfo" "TITLE FROM filename dot nfo"
say "movie.nfo and <filename>.nfo disagree on the title"

# =======================================================================
# 3. Filename versus sidecar: which one supplies the displayed title and year.
# =======================================================================
echo "case 03 filename versus sidecar"
d="$movies/Example Studio - 2025-11-05 - Filename Says This"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-05 - Filename Says This.mkv"
nfo_full "$d/movie.nfo" "Sidecar Says Something Else" "2019-01-31"
say "the folder and file name carry one date, the sidecar another"

# =======================================================================
# 4. Nothing but the video file.
# =======================================================================
echo "case 04 bare file"
d="$movies/Example Studio - 2025-11-06 - No Sidecar At All"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-06 - No Sidecar At All.mkv"
say "video file only"

# =======================================================================
# 5. The release date format. The parser reads <premiered> with an exact
#    format, so a date in any other shape should simply not arrive.
# =======================================================================
echo "case 05 date formats"
for spec in "Iso:2025-11-07" "Slashes:07/11/2025" "Dots:07.11.2025" "IsoWithTime:2025-11-07T00:00:00" "LongForm:07 November 2025"; do
    label="${spec%%:*}"; value="${spec#*:}"
    d="$movies/Example Studio - 2025-11-07 - Date Format $label"
    mkdir -p "$d"
    video "$d/Example Studio - 2025-11-07 - Date Format $label.mkv"
    cat > "$d/movie.nfo" <<XML
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Date Format $label</title>
  <premiered>$value</premiered>
  <studio>Example Studio</studio>
</movie>
XML
done
say "one folder per candidate date format, all claiming 7 November 2025"

# =======================================================================
# 6. How a performer entry has to be shaped to become a person.
# =======================================================================
echo "case 06 actor shapes"
d="$movies/Example Studio - 2025-11-08 - Actor Shapes"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-08 - Actor Shapes.mkv"
cat > "$d/movie.nfo" <<'XML'
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>Actor Shapes</title>
  <studio>Example Studio</studio>
  <actor>
    <name>Shape A Full</name>
    <role>Performer</role>
    <type>Actor</type>
    <order>0</order>
    <thumb>https://example.invalid/shape-a.jpg</thumb>
  </actor>
  <actor>
    <name>Shape B Name Only</name>
  </actor>
  <actor>Shape C Bare Text</actor>
  <actor>
    <name>Shape D Typed Director</name>
    <type>Director</type>
  </actor>
  <actor>
    <name>Shape E Unknown Type</name>
    <type>Performer</type>
  </actor>
  <actor>
    <name>Shape F Ordered Last</name>
    <order>9</order>
  </actor>
  <actor>
    <name></name>
    <role>Nameless</role>
  </actor>
</movie>
XML
say "seven actor nodes, each shaped differently"

# =======================================================================
# 7. A flat directory per site instead of one directory per scene.
# =======================================================================
echo "case 07 flat site directory"
d="$movies/Flat Site Directory"
mkdir -p "$d"
video "$d/Flat Site Directory - 2025-11-09 - Flat Scene One.mkv"
nfo_title_only "$d/Flat Site Directory - 2025-11-09 - Flat Scene One.nfo" "Flat Scene One"
video "$d/Flat Site Directory - 2025-11-10 - Flat Scene Two.mkv"
nfo_title_only "$d/Flat Site Directory - 2025-11-10 - Flat Scene Two.nfo" "Flat Scene Two"
nfo_title_only "$d/movie.nfo" "MOVIE DOT NFO IN A SHARED FOLDER"
image "$d/poster.jpg" red 600x900
say "two scenes loose in one folder, plus a movie.nfo and a poster.jpg"

# =======================================================================
# 8. Two versions of one scene, named the way the documentation prescribes.
# =======================================================================
echo "case 08 two versions, bracket labels"
d="$movies/Example Studio - 2025-11-11 - Two Versions Bracketed"
mkdir -p "$d"
video   "$d/Example Studio - 2025-11-11 - Two Versions Bracketed - [1080p].mkv"
video4k "$d/Example Studio - 2025-11-11 - Two Versions Bracketed - [2160p].mkv"
nfo_full "$d/movie.nfo" "Two Versions Bracketed" "2025-11-11"
say "both filenames start with the folder name, then ' - [label]'"

# =======================================================================
# 9. The same, with the label appended without the prescribed separator.
# =======================================================================
echo "case 09 two versions, bare labels"
d="$movies/Example Studio - 2025-11-12 - Two Versions Bare"
mkdir -p "$d"
video   "$d/Example Studio - 2025-11-12 - Two Versions Bare 1080p.mkv"
video4k "$d/Example Studio - 2025-11-12 - Two Versions Bare 2160p.mkv"
nfo_full "$d/movie.nfo" "Two Versions Bare" "2025-11-12"
say "labels appended with a space and no bracket"

# =======================================================================
# 10. Two unrelated scenes sharing a folder: the case that must not merge.
# =======================================================================
echo "case 10 two unrelated scenes in one folder"
d="$movies/Example Studio - 2025-11-13 - Unrelated Pair"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-13 - Unrelated Pair.mkv"
video "$d/Example Studio - 2025-11-14 - A Different Scene Entirely.mkv"
say "one file matches the folder name, one does not"

# =======================================================================
# 11. Refresh: the sidecar is edited after the first scan.
# =======================================================================
echo "case 11 refresh probe"
d="$movies/Example Studio - 2025-11-15 - Refresh Probe"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-15 - Refresh Probe.mkv"
nfo_full "$d/movie.nfo" "Refresh Before The Edit" "2025-11-15"
say "the sidecar this case exists to edit later"

# =======================================================================
# 12. Characters in a title, one folder per class so that one failure does
#     not take the rest with it. '\' is left out: the SMB probe showed the
#     share rejects it outright.
# =======================================================================
echo "case 12 characters"
i=0
for spec in \
    "Colon:A:B" \
    "Question:A?B" \
    "Asterisk:A*B" \
    "Pipe:A|B" \
    "Angles:A<B>C" \
    "DoubleQuote:A\"B" \
    "Ampersand:A&B" \
    "Hash:A#B" \
    "Percent:A%B" \
    "Plus:A+B" \
    "Apostrophe:A'B" \
    "Brackets:A[B]C" \
    "Accents:Amélie Bär Œuvre" \
    "Cjk:日本語のタイトル" \
    "Emoji:A 🎬 B" \
    "EmDash:A — B" \
    ; do
    label="${spec%%:*}"; body="${spec#*:}"
    i=$((i + 1))
    name="Example Studio - 2025-12-$(printf '%02d' "$i") - Chars $label $body"
    d="$movies/$name"
    mkdir -p "$d" 2>/dev/null || { echo "  SKIPPED (filesystem rejected): $label"; continue; }
    video "$d/$name.mkv"
    nfo_title_only "$d/movie.nfo" "Chars $label $body"
    image "$d/poster.jpg" red 600x900
done
say "one folder per character class, each with a poster so image URLs are exercised too"

# =======================================================================
# 13. Length: a name close to the 255 byte component limit observed on the
#     share.
# =======================================================================
echo "case 13 length"
prefix="Example Studio - 2025-12-31 - Long Name "
pad=$(printf 'x%.0s' $(seq 1 $((250 - ${#prefix} - 4))))
name="$prefix$pad"
d="$movies/$name"
mkdir -p "$d"
video "$d/$name.mkv"
nfo_title_only "$d/movie.nfo" "Long Name"
say "folder name at ${#name} characters, file name at $(( ${#name} + 4 ))"

# =======================================================================
# 14. The same content offered to a Home Videos library.
# =======================================================================
echo "case 14 home videos library"
d="$homevideos/Example Studio - 2025-11-03 - Baseline Complete"
mkdir -p "$d"
video "$d/Example Studio - 2025-11-03 - Baseline Complete.mkv"
nfo_full "$d/movie.nfo" "Baseline Complete" "2025-11-03"
nfo_full "$d/Example Studio - 2025-11-03 - Baseline Complete.nfo" "Baseline Complete" "2025-11-03"
image "$d/poster.jpg" red  600x900
image "$d/fanart.jpg" blue 1920x1080
say "the baseline case again, for a library declared as Home Videos"

# =======================================================================
# 15. Artwork, one filename per folder. The baseline case puts every image
#     name in one directory, which cannot say which file became which image
#     when two of them compete for the same slot. This can.
# =======================================================================
echo "case 15 artwork names"
j=0
for img in poster.jpg folder.jpg cover.jpg default.jpg movie.jpg \
           fanart.jpg backdrop.jpg background.jpg art.jpg \
           logo.png clearlogo.png clearart.png disc.jpg \
           banner.jpg thumb.jpg landscape.jpg; do
    j=$((j + 1))
    name="Art Probe $(printf '%02d' "$j") $img"
    d="$movies/$name"
    mkdir -p "$d"
    video "$d/$name.mkv"
    nfo_title_only "$d/movie.nfo" "$name"
    image "$d/$img" red 600x900
done
# The suffix form the documentation mentions: <video file name>-poster.jpg
name="Art Probe 90 suffix form"
d="$movies/$name"
mkdir -p "$d"
video "$d/$name.mkv"
nfo_title_only "$d/movie.nfo" "$name"
image "$d/$name-poster.jpg" red 600x900
image "$d/$name-fanart.jpg" blue 1920x1080
# And a folder with no artwork at all, for what absence looks like.
name="Art Probe 91 nothing"
d="$movies/$name"
mkdir -p "$d"
video "$d/$name.mkv"
nfo_title_only "$d/movie.nfo" "$name"
say "one folder per image filename, plus the suffix form and a folder with none"

# =======================================================================
# 16. The layout has a directory per site above the directory per scene.
#     Everything above sits at the library root, so this is the only case
#     that exercises the second level: whether scenes stay apart there, and
#     whether version grouping still works one level down.
# =======================================================================
echo "case 16 nested site directory"
for scene in "2025-11-25 - Nested Scene A" "2025-11-26 - Nested Scene B"; do
    d="$movies/Nested Site/Nested Site - $scene"
    mkdir -p "$d"
    video "$d/Nested Site - $scene.mkv"
    nfo_full "$d/movie.nfo" "Nested $scene" "${scene%% *}"
done
d="$movies/Nested Site/Nested Site - 2025-11-27 - Nested Two Versions"
mkdir -p "$d"
video   "$d/Nested Site - 2025-11-27 - Nested Two Versions - [1080p].mkv"
video4k "$d/Nested Site - 2025-11-27 - Nested Two Versions - [2160p].mkv"
nfo_full "$d/movie.nfo" "Nested Two Versions" "2025-11-27"
say "two scenes and one two-version scene, one level below the library root"

# =======================================================================
# 17. Whether a Home Videos library reads movie.nfo at all, against the
#     same file in a Movies library. This is what decides the library type.
# =======================================================================
echo "case 17 movie.nfo by library type"
for pair in "$homevideos:2025-11-20:Home Video Read" "$movies:2025-11-21:Movie Library Read"; do
    root="${pair%%:*}"; rest="${pair#*:}"; date="${rest%%:*}"; label="${rest#*:}"
    d="$root/Example Studio - $date - Movie Nfo Only"
    mkdir -p "$d"
    video "$d/Example Studio - $date - Movie Nfo Only.mkv"
    # Deliberately only movie.nfo, and no per-file sidecar to fall back on.
    cat > "$d/movie.nfo" <<XML
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<movie>
  <title>$label movie dot nfo</title>
  <premiered>$date</premiered>
  <studio>Example Studio</studio>
</movie>
XML
done
say "the same movie.nfo offered to each library type, with nothing else to read"

echo
echo "fixtures written to $target"
find "$target" -type f | wc -l | xargs printf '%s files\n'
du -sh "$target" | cut -f1 | xargs printf '%s on disk\n'
