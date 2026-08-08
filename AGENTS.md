# Repository Guidelines

A tool that organises video files using metadata from prdb. Open source, MIT.
The implementation language is not decided yet, so most of this file describes
constraints rather than commands.

## Language

**Everything in this repository is in English** — code, comments, documentation,
commit messages, branch names, PR titles and descriptions, CI job names, test
names, and anything visible to users of the tool. No exceptions.

## The one hard rule

**This tool moves, renames and deletes files that a user cannot get back.**
Treat every write path as the dangerous one, because it is:

- A destructive operation is opt-in, never the default. Deleting or overwriting
  needs an explicit flag, not merely the absence of a cautious one.
- There is a dry run, and it is the default until proven otherwise. It prints
  exactly what a real run would do.
- Nothing is written on the strength of a partial or failed metadata lookup. An
  API error means stopping, not falling back to a guess at the filename.
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
above get tests before they get a release.

## Versioning

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Pre-`1.0`, a minor
bump may break things.
