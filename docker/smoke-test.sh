#!/usr/bin/env bash
#
# Starts a built image and checks the four claims ADR 0013 makes about it, in
# the only place they can actually be checked: a running container.
#
#   1. It comes up and answers.
#   2. What it writes into its data volume belongs to PUID:PGID.
#   3. It leaves the ownership of everything else alone.
#   4. `docker stop` stops it, rather than the daemon killing it once the
#      timeout runs out.
#
# Usage: docker/smoke-test.sh <image> [host-port]

set -euo pipefail

image="${1:?Usage: docker/smoke-test.sh <image> [host-port]}"
port="${2:-18080}"

readonly test_uid=1234
readonly test_gid=5678
readonly startup_timeout_seconds=180
readonly stop_timeout_seconds=10

container=""
workspace="$(mktemp --directory)"

cleanup() {
    if [ -n "$container" ]; then
        docker rm --force "$container" >/dev/null 2>&1 || true
    fi

    # The container wrote into these as another user, so removing them from here
    # would need root. Borrowing the image's own root is cheaper than sudo and
    # works the same way on a laptop and on a runner.
    docker run --rm --volume "$workspace:/workspace" --entrypoint /bin/sh "$image" \
        -c 'rm -rf /workspace/data /workspace/media' >/dev/null 2>&1 || true
    rmdir "$workspace" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    echo "FAIL: $*" >&2

    if [ -n "$container" ]; then
        echo "The container said:" >&2
        docker logs "$container" 2>&1 | sed 's/^/    /' >&2
    fi

    exit 1
}

mkdir -p "$workspace/data" "$workspace/media"

# Stands in for the library: a file the tool did not create, owned by whoever
# the NAS says owns it. Nothing in the container may touch that.
echo 'a video, as far as this test is concerned' >"$workspace/media/keep.mkv"
media_owner_before="$(stat --format '%u:%g' "$workspace/media/keep.mkv")"

echo "Starting $image"
container="$(docker run --detach \
    --env PUID="$test_uid" \
    --env PGID="$test_gid" \
    --env UMASK=002 \
    --volume "$workspace/data:/data" \
    --volume "$workspace/media:/media" \
    --publish "$port:8080" \
    "$image")"

echo "Waiting for /api/health (up to ${startup_timeout_seconds}s)"
health=""
for _ in $(seq "$startup_timeout_seconds"); do
    if ! docker inspect --format '{{.State.Running}}' "$container" | grep --quiet true; then
        fail "the container stopped before it answered."
    fi

    if health="$(curl --silent --fail "http://127.0.0.1:$port/api/health" 2>/dev/null)"; then
        break
    fi

    health=""
    sleep 1
done

[ -n "$health" ] || fail "no answer from /api/health within ${startup_timeout_seconds}s."
echo "  /api/health said: $health"

# The API answering proves the application is in the image. It does not prove
# the frontend is: ADR 0006 has Vite build into wwwroot in a stage this image
# then copies out of, and every way that can go wrong ends in a white page
# rather than an error. So ask for the page, and then for the script it names —
# a wwwroot that arrived without its assets serves the first and not the second.
page="$(curl --silent --fail "http://127.0.0.1:$port/" 2>/dev/null)" \
    || fail "/ did not answer; the image is serving no frontend at all."

echo "$page" | grep --quiet '<div id="root">' \
    || fail "/ answered with something that is not the application's page."

script_path="$(echo "$page" | grep --only-matching 'src="/assets/[^"]*\.js"' | head -1 | cut -d'"' -f2)"
[ -n "$script_path" ] || fail "the page names no script, so the build that produced it was not a real one."

curl --silent --fail --output /dev/null "http://127.0.0.1:$port$script_path" \
    || fail "the page asks for $script_path and the image does not have it."
echo "  the page and $script_path are both served"

database="$workspace/data/ordeno.db"
[ -f "$database" ] || fail "no database in the data volume; the tool did not get as far as its own state."

database_owner="$(stat --format '%u:%g' "$database")"
[ "$database_owner" = "$test_uid:$test_gid" ] \
    || fail "the database is owned by $database_owner, not by the requested $test_uid:$test_gid."
echo "  the database belongs to $database_owner"

media_owner_after="$(stat --format '%u:%g' "$workspace/media/keep.mkv")"
[ "$media_owner_after" = "$media_owner_before" ] \
    || fail "the entrypoint changed the owner of a mounted media file from $media_owner_before to $media_owner_after."
echo "  the mounted media file still belongs to $media_owner_before"

echo "Stopping it"
stop_started="$(date +%s)"
docker stop --timeout "$stop_timeout_seconds" "$container" >/dev/null
stop_seconds="$(($(date +%s) - stop_started))"

exit_code="$(docker inspect --format '{{.State.ExitCode}}' "$container")"
[ "$exit_code" = "0" ] \
    || fail "it exited with $exit_code after ${stop_seconds}s — 137 means SIGTERM went unheard and the daemon killed it."

[ "$stop_seconds" -lt "$stop_timeout_seconds" ] \
    || fail "stopping took ${stop_seconds}s, which is the timeout rather than a shutdown."
echo "  stopped in ${stop_seconds}s, exit code 0"

echo "OK: $image"
