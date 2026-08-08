# Contributing

Bug reports and pull requests are welcome.

Everything in this repository is in English — code, comments, documentation,
commit messages, branch names and PR descriptions. No exceptions.

## Where the project stands

This is an early repository. There is no code yet, and the implementation
language is not decided, so there is nothing to build or test at the moment.
That is deliberate rather than an omission: the sections below on setting up and
running tests will be filled in with the first code, and until then any
instructions here would be a guess.

If you are thinking about contributing something substantial, open an issue
first. A design that does not fit yet is much cheaper to redirect before it is
written than after.

## Reporting a bug

`prdb-ordeno` organises video files using metadata from prdb, so a report is
useful in proportion to how precisely it separates the two sides:

- **Wrong file handling** — a file moved, renamed or skipped when it should not
  have been. Include the layout before and after, and the exact command. This is
  the tool's own logic.
- **Wrong metadata** — the tool did what it was told, but prdb's answer was
  wrong. That belongs upstream, though report it here if you are unsure and we
  will route it.
- **A crash** — the full error output, and what the tool was pointed at.

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

To be written with the first code, once the language is chosen.

## Running the tests

To be written with the first code, once the language is chosen.

## License

By contributing you agree that your contribution is licensed under the MIT
License, the same as the rest of the repository.
