# Contributing

Bug reports and pull requests are welcome.

Everything in this repository is in English — code, comments, documentation,
commit messages, branch names and PR descriptions. No exceptions.

## Where the project stands

This is an early repository. The backend stack is settled — .NET 10 — but there
is no code yet, so there is nothing to build or test until the first feature
lands.

`VISION.md` describes what the tool is meant to become — a self-hosted web
application that files downloads into a library a media server can read — and,
just as usefully, what it is not. It is the best place to check whether an idea
belongs here before spending an evening on it.

If you are thinking about contributing something substantial, open an issue
first. A design that does not fit yet is much cheaper to redirect before it is
written than after.

## Reporting a bug

`prdb-ordeno` organises video files using metadata from prdb, so a report is
useful in proportion to how precisely it separates the two sides:

- **Wrong file handling** — a file moved, renamed or skipped when it should not
  have been. Include the directory layout before and after, the chosen target
  layout, and what the tool said it was going to do beforehand if you saw it.
  This is the tool's own logic.
- **Wrong metadata** — the tool did what it was told, but prdb's answer was
  wrong. That belongs upstream, though report it here if you are unsure and we
  will route it.
- **A crash** — the container logs around the failure, and what the tool was
  pointed at.

Since the tool runs unattended, plenty of bugs are found after the fact rather
than watched happening. That is fine, and it is what the logs are for; say which
it was, because "I saw the preview and confirmed it" and "I found it like this
in the morning" point at different code.

Anything that moves or deletes files deserves particular care in a report. Say
whether the data was recoverable, because that changes how urgent the fix is.

## Commits and pull requests

Commit subjects follow [Conventional Commits](https://www.conventionalcommits.org/):
`feat:`, `fix:`, `chore:`, `docs:`. Keep the subject under about 72 characters
and write it in the imperative.

Explain *why* in the body. A commit that says what the diff already shows is a
wasted opportunity; the interesting part is the reasoning that is no longer
visible once the code is in place.

Add an entry to `CHANGELOG.md` under `## [Unreleased]` for anything a user would
notice. Internal refactoring that changes no behaviour does not need one.

## Setting up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) — `global.json`
requires a 10.0 SDK and takes the newest one you have installed — and
[Node](https://nodejs.org) for the frontend. To run the tool rather than just
build it, you also need Docker. `ffmpeg` and `ffprobe` are needed for perceptual
hashing; the container image brings its own, so install them locally only if you
work on that part outside a container.

```
dotnet build
```

That builds the backend alone. The frontend is a separate Vite project that
builds into the host's `wwwroot`:

```
cd src/Prdb.Ordeno.Frontend
npm ci
npm run build
```

A release build does this for you — `dotnet publish -c Release` runs the npm
build first, so the published output is the whole application rather than an API
with a blank page in front of it. Pass `-p:SkipFrontendBuild=true` if you have
already built it.

While working on the UI, run both and let Vite forward the API:

```
dotnet run --project src/Prdb.Ordeno.Host    # http://localhost:8080
cd src/Prdb.Ordeno.Frontend && npm run dev   # http://localhost:5173
```

The tool keeps its database in the directory `ORDENO_DATA_DIRECTORY` names —
`/data` in the container, and `.local/data` under the host project when you run
it from the repository. Delete that directory to start over from an empty
installation, or set `ORDENO_RESET_PASSWORD=true` for one start to clear the
password and every session while keeping the rest.

## Changing the database schema

`dotnet ef` is pinned as a local tool:

```
dotnet tool restore
dotnet ef migrations add <Name> \
  --project src/Prdb.Ordeno.Infrastructure \
  --startup-project src/Prdb.Ordeno.Host \
  --output-dir Persistence/Migrations
```

Migrations are applied at startup, and one that fails stops the tool rather than
letting it run against a schema it does not understand. Write them so that they
can meet a database somebody has been using for a year: this is not a database
anyone can be asked to restore.

## Running the tests

```
dotnet test
```

Tests that touch file handling work against a real filesystem in a temporary
directory rather than a mocked one. If you are changing anything that moves,
renames or deletes files, that is where the test belongs — see the hard rule in
`AGENTS.md`.

## License

By contributing you agree that your contribution is licensed under the MIT
License, the same as the rest of the repository.
