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

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and, to run
the tool rather than just build it, Docker. `ffmpeg` and `ffprobe` are needed
for perceptual hashing; the container image brings its own, so install them
locally only if you work on that part outside a container.

```
dotnet build
```

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
