#!/usr/bin/env bash
# What a directory accepts in a path, and what it does to it afterwards.
#
# This answers the half of the layout question that has nothing to do with
# Jellyfin: a title that files fine on a local disk and fails on a NAS is the
# bug the specification exists to prevent, so the limits have to be measured on
# the kind of storage this tool's users actually have. Run it against a local
# filesystem first for a control, then against the share.
#
# It creates one directory per case under <root>/.ordeno-path-probe and removes
# everything it created. Nothing else is touched.
#
# Usage: probe-path.sh <root>

set -u

root="${1:?usage: probe-path.sh <root>}"
probe="$root/.ordeno-path-probe"

if [ -e "$probe" ]; then
    echo "refusing to run: $probe already exists" >&2
    exit 1
fi

mkdir -p "$probe" || exit 1

# Every case gets its own subdirectory. The first version of this script reused
# one directory and identified the result with `ls | head -1`, which silently
# read a leftover from the previous case as the answer to the current one.
cell_no=0
cell() {
    cell_no=$((cell_no + 1))
    printf '%s/c%03d' "$probe" "$cell_no"
}

cleanup() {
    # Names containing characters the share maps are not always removable by
    # the name readdir reports, so remove by the name that was written where
    # that is known, and fall back to a wildcard sweep.
    rm -rf -- "$probe" 2>/dev/null || true
    if [ -e "$probe" ]; then
        find "$probe" -mindepth 1 -depth -exec rm -rf -- {} + 2>/dev/null || true
        rmdir "$probe" 2>/dev/null || true
    fi
    [ -e "$probe" ] && echo "WARNING: could not remove $probe" >&2
}
trap cleanup EXIT

printf '%-26s %-10s %s\n' "CASE" "RESULT" "DETAIL"

try_name() {
    local label="$1" name="$2"
    local c; c=$(cell)
    mkdir -p "$c" || return
    if ! mkdir "$c/$name" 2>/dev/null; then
        printf '%-26s %-10s %s\n' "$label" "REJECTED" "mkdir refused the name"
        rmdir "$c" 2>/dev/null
        return
    fi
    if ! : > "$c/$name/probe.mkv" 2>/dev/null; then
        printf '%-26s %-10s %s\n' "$label" "NO-FILE" "directory created, file could not be"
        rm -rf -- "$c" 2>/dev/null
        return
    fi
    local back; back=$(ls -A "$c")
    if [ "$back" = "$name" ]; then
        printf '%-26s %-10s %s\n' "$label" "ok" "returned unchanged"
    else
        printf '%-26s %-10s %s\n' "$label" "CHANGED" "returned as [$(printf '%s' "$back" | cat -v)]"
    fi
    rm -rf -- "$c" 2>/dev/null
}

echo "== characters =="
try_name "colon"               'a:b'
try_name "question mark"       'a?b'
try_name "asterisk"            'a*b'
try_name "pipe"                'a|b'
try_name "less than"           'a<b'
try_name "greater than"        'a>b'
try_name "double quote"        'a"b'
try_name "backslash"           'a\b'
try_name "ampersand"           'a&b'
try_name "single quote"        "a'b"
try_name "hash"                'a#b'
try_name "percent"             'a%b'
try_name "plus"                'a+b'
try_name "semicolon"           'a;b'
try_name "square brackets"     'a[b]c'
try_name "parentheses"         'a(b)c'
try_name "trailing period"     'ab.'
try_name "trailing space"      'ab '
try_name "leading space"       ' ab'
try_name "latin-1 accents"     'Amélie Bär'
try_name "cjk"                 '日本語のタイトル'
try_name "em dash"             'a — b'
try_name "emoji"               'a 🎬 b'

echo
echo "== is the component limit counted in bytes or in characters? =="
for n in 250 254 255 256; do
    try_name "ascii x${n}" "$(printf 'x%.0s' $(seq 1 "$n"))"
done
for n in 84 85 86; do
    try_name "cjk x${n} ($((n * 3)) bytes)" "$(printf '漢%.0s' $(seq 1 "$n"))"
done

echo
echo "== what does the share store, as opposed to what it shows? =="
# A mount carrying 'mapposix' translates the characters Windows forbids into
# the Unicode private use area on the wire and back again on the way in, so
# what `ls` prints is the client's translation rather than the stored name.
# Writing the private use codepoints raw and reading them back shows the
# mapping table directly, without having to guess it.
for cp in F01F F020 F021 F022 F023 F024 F025 F026 F027 F028 F029 F02A; do
    char=$(printf "\\u$cp")
    c=$(cell); mkdir -p "$c"
    if mkdir "$c/A${char}B" 2>/dev/null; then
        back=$(ls -A "$c")
        if [ "$back" = "A${char}B" ]; then
            printf '%-26s %-10s %s\n' "U+$cp" "unmapped" "stored as written"
        else
            printf '%-26s %-10s %s\n' "U+$cp" "MAPPED" "shown as [$(printf '%s' "$back" | cat -v)]"
        fi
        # Remove by the name that was written: the mapping is not symmetric for
        # an interior character, so the name readdir reported may not resolve.
        rmdir "$c/A${char}B" 2>/dev/null || rm -rf -- "$c/$back" 2>/dev/null
    else
        printf '%-26s %-10s %s\n' "U+$cp" "REJECTED" "-"
    fi
    rmdir "$c" 2>/dev/null
done

echo
echo "== total path length =="
deep="$probe/deep"
mkdir -p "$deep"
level=0
while [ "$level" -lt 40 ]; do
    next="$deep/$(printf 'd%.0s' $(seq 1 30))"
    mkdir "$next" 2>/dev/null || break
    deep="$next"
    level=$((level + 1))
done
echo "deepest path created: ${#deep} characters, over $level levels"
if : > "$deep/probe.mkv" 2>/dev/null; then
    echo "a file at that depth: ok"
else
    echo "a file at that depth: FAILED"
fi
