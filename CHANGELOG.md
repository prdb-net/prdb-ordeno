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
and the test projects. Nothing is identified, moved or filed yet.

The stack is .NET 10 on the backend, React with Vite in the browser, and SQLite
for local state. The first release targets Jellyfin; Plex and Emby follow after
it.

Configuration and the single password are set up in the browser on first run
rather than through the container environment, and the image will be published
to Docker Hub for `linux/amd64` and `linux/arm64`.
