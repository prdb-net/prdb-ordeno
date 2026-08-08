# Repository Guidelines

A tool that organises video files using metadata from prdb. Open source, MIT.
The implementation language is not decided yet, so most of this file describes
constraints rather than commands.

It is a self-hosted web application, not a command-line tool: a long-running
service in a container, with a browser UI, that keeps filing new downloads
without being asked. Source and target storage arrive as Docker mounts. Read
`VISION.md` before designing anything — it is what these constraints are in
service of.

## Language

**Everything in this repository is in English** — code, comments, documentation,
commit messages, branch names, PR titles and descriptions, CI job names, test
names, and anything visible to users of the tool. No exceptions.

## The one hard rule

**This tool moves, renames and deletes files that a user cannot get back.**
Treat every write path as the dangerous one, because it is:

- A destructive operation is opt-in, never the default. Deleting or overwriting
  is something the user turned on deliberately, not something that happens
  because nobody turned it off.
- Every write path can be asked what it would do without doing it, and that
  answer is exactly what the real run performs. In the UI this is a preview the
  user confirms; in the automatic runs that follow, it is what gets recorded and
  shown afterwards. A button is easier to press than a command is to type, so
  running unattended behind a web UI raises the stakes here rather than
  lowering them.
- Nothing is written on the strength of a partial or failed metadata lookup. An
  API error means stopping, not falling back to a guess at the filename. A
  scheduled run is no excuse to degrade: unattended is not the same as
  unsupervised, and the user reads the result later.
- Moves across filesystems are copy-then-verify-then-delete. A rename that turns
  into a copy under the hood must not lose the file when it fails halfway.

A plausible refactor that quietly weakens one of these does more damage than any
crash. When you touch file handling, this is what the tests are for.

## Where the metadata comes from

The prdb Public API. It is documented at <https://apidocs.prdb.net> and the
OpenAPI document is public, but the path is not guessable — the docs UI fetches
`/configuration.json` to discover it.

Authentication is an API key in the `X-Api-Key` header, over https. `GET /health`
is the only endpoint that works without a key.

**Do not hand-roll an HTTP client for it.** `prdb-sdk` publishes generated,
maintained clients for Python, TypeScript, Go and C#, and they already handle
the parts that are easy to get wrong — in particular refusing a redirect to a
different origin, which every HTTP stack would otherwise follow while carrying
`X-Api-Key` off the API host, because they strip only `Authorization`. Choosing
the implementation language and choosing the SDK are one decision, not two.

## Layout

```
LICENSE           MIT
README.md         what the tool is
VISION.md         what it is for, and what it is deliberately not
CONTRIBUTING.md   how to report a bug, how to shape a commit
CHANGELOG.md      Keep a Changelog, Unreleased at the top
```

Source layout follows once the language is chosen.

## Commits

Subjects follow [Conventional Commits](https://www.conventionalcommits.org/):
`feat:`, `fix:`, `chore:`, `docs:`. Under about 72 characters, imperative.

Explain *why* in the body. A commit that says what the diff already shows is a
wasted opportunity; the interesting part is the reasoning that is no longer
visible once the code is in place.

Add a `CHANGELOG.md` entry under `## [Unreleased]` for anything a user would
notice. A refactor that changes no behaviour does not need one.

## Verifying a change

To be written with the first code. Whatever the language, the destructive paths
above get tests before they get a release, and those tests run against a real
filesystem — the interesting failures are the ones a mocked file layer cannot
have.

## Versioning

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0`, a minor
bump may break things.
