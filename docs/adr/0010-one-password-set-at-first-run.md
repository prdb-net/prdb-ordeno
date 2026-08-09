# 0010. One password, set at first run

- **Status:** Accepted
- **Date:** 2026-08-09

## Context

`VISION.md` is explicit about the shape: the UI is behind a password even on a
LAN, one password, no username, no email. What it does not say is where that
password comes from, and the difference matters more than it looks. A tool that
ships with a default credential is open until someone changes it, and the window
in which nobody has is exactly the window in which it is being installed and
poked at.

The tool also has real reach: it moves and deletes files that cannot be
recovered, and it holds a prdb API key. Whoever reaches the UI has that reach.

## Decision

The first request to a fresh installation is the setup screen, and setting the
password is part of it. It is stored hashed with ASP.NET Core's
`PasswordHasher<T>` (PBKDF2), and a successful sign-in produces an HttpOnly,
SameSite session cookie. There is no default password and no way to run without
one.

## Alternatives considered

- **Password from an environment variable or a secret file.** Rejected for the
  same reason as the settings in
  [ADR 0009](0009-configuration-is-collected-by-onboarding.md), plus one of its
  own: it leaves the password in plain text in the Compose file and in the
  process environment, where anything that dumps the environment into a log or
  a crash report takes it along. It stays available as a documented reset path
  rather than as the way to configure it.
- **Trusting an upstream reverse proxy** (Authelia, Tailscale, a proxy header)
  in place of the password. Rejected for now, not on principle — plenty of this
  audience runs exactly that. But it is a second authentication path, and a
  second path is a second way to configure it wrongly; the failure mode is a
  header that anyone can set. It can be added later once the first one is solid,
  and it changes nothing about this decision if it is.

## Consequences

- The setup screen has to be reachable before authentication exists and must
  stop being reachable the moment a password is set. That transition is the
  interesting one to get right, and to test: it is the only unauthenticated
  write path in the application.
- No accounts, no roles, no password recovery by email. Losing the password
  means resetting it locally — through a documented environment variable or a
  command against the mounted data directory — and that path needs to exist
  before the first release, because it will be used.
- Sessions live in the database next to everything else, so a restart does not
  sign the user out and revoking a session is possible. The cookie is
  `Secure` when the request arrived over https, and the documentation says
  plainly that reaching this over plain http across an untrusted network sends
  the password in the clear.
- Sign-in is rate-limited. It is one password with no username, which is the
  easiest thing in the world to try repeatedly.
- Anything mechanical that needs to reach the tool later — a health check, a
  notification hook, a script — cannot use a browser session. That is a gap this
  decision leaves open on purpose rather than inventing an API token nobody has
  asked for yet.
