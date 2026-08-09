# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Entries describe what changed for someone *using* the tool. A refactor that
moves a thousand lines but changes no behaviour is not worth an entry; a
renamed flag or a different default is, however small the diff.

Until the first release the version is `0.x`, and a minor bump may break
things — see SemVer's clause on initial development.

## [Unreleased]

Nothing released yet. The repository holds its licence, its project
documentation and the scaffolding the application will be built in: a .NET 10
solution, a React frontend that Vite builds into the backend's static assets,
and the test projects. The tool now creates and migrates its SQLite database in
the directory `ORDENO_DATA_DIRECTORY` names, `/data` by default. Nothing is
identified, moved or filed yet.

Access exists: a fresh installation sets one password on first use, and
everything else is behind the session cookie it hands out. There is no default
password. `ORDENO_RESET_PASSWORD=true` clears the password and every session at
startup for someone who has lost it. The browser side of this arrives with
onboarding; today it is the API.

The stack is .NET 10 on the backend, React with Vite in the browser, and SQLite
for local state. The first release targets Jellyfin; Plex and Emby follow after
it.

The image will be Debian-based, bring its own `ffmpeg`, and honour `PUID` and
`PGID` so filed files carry the owner the NAS expects.

Configuration and the single password are set up in the browser on first run
rather than through the container environment, and the image will be published
to Docker Hub for `linux/amd64` and `linux/arm64`.
