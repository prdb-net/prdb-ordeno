# 0018. The Jellyfin connection is optional, and never in the filing path

- **Status:** Accepted
- **Date:** 2026-08-13

## Context

[ADR 0008](0008-the-first-release-targets-jellyfin-only.md) makes Jellyfin the
one media server the first release targets, and the layout it files into is
settled in [`docs/jellyfin-layout.md`](../jellyfin-layout.md). That document
leaves one question open on purpose: whether the tool ever talks to the server
at all. Today it writes files and nothing else.

Three things a connection would buy, from that research:

- **Making a rewritten sidecar appear.** A plain library scan does pick up a
  changed `movie.nfo`, but only if the file's modification time is more than a
  minute later than the moment Jellyfin last saved that item. A tool that
  rewrites a sidecar and wants to see the result lands inside that window every
  time; a targeted refresh applies regardless.
- **Reading the date format.** `<premiered>` is parsed against one exact format
  and that format is a server setting. A user who changed it gets a library with
  no dates out of entirely correct sidecars, with no error on either side.
  [#21](https://github.com/prdb-net/prdb-ordeno/issues/21) exists only to
  document the way around not being able to see this.
- **Telling the user it worked.** "Filed, and Jellyfin can see it" is a better
  thing for onboarding to end on than "filed".

Against them: another URL and another credential in a setup `VISION.md` wants
short, a dependency on a server being reachable that the filing path does not
have today, and a second server-specific surface — Plex and Emby differ far more
in their APIs than in their sidecars. And none of it is needed for anything to be
*correct*. The case `VISION.md` actually names, metadata corrected years after
filing, is outside the one-minute window by years and a scheduled scan handles
it.

Two of the three gains are therefore about reassurance, and only the date format
is something the tool is otherwise blind to.

Before deciding, the route from what the tool knows to what the API wants was
measured, because the cost of the connection is mostly there. It is
`docs/jellyfin-probe/probe-itemid.sh`, and section 9 of the layout document
holds the result. In short:

- The tool knows a path. The refresh endpoint wants an item id, and the server
  cannot be asked to resolve one from the other: `GET /Items` accepts a `path=`
  parameter, ignores it, and returns **the whole library** — a caller that trusts
  it and takes the first item refreshes something at random. Searching by the
  scene name finds nothing, because the name Jellyfin indexes comes from the
  sidecar. What works is enumerating the library with `fields=Path` and matching
  here.
- **The path the tool knows is not the path Jellyfin knows.** Both run in
  containers with their own mounts, and nothing in either configuration states
  the other's. Matching on the *tail* of the path works — the site directory,
  the scene directory and the file name are the same on both sides — and the
  match yields the prefix the server uses, so the substitution is discovered
  rather than configured.
- `POST /Library/Media/Updated` takes a path instead of an item id, which would
  make the lookup unnecessary. It obeys the same one-minute tolerance a scan
  does, so it cannot do the one thing a targeted refresh is for; it only accepts
  the video file's path, not the directory's and not the sidecar's; and a path
  the server does not recognise — the tool's own, for instance — is answered with
  a 204 and nothing else. Finding the item id is the opposite of that: it is the
  receipt for having matched something the server actually has.
- A plain API key from Jellyfin's dashboard reaches all of it, including
  `GET /Items` without a user id and `GET /System/Configuration/xbmcmetadata`,
  where the release date format lives. So the connection needs a URL and a key,
  and no user account, no password, and no user id.

## Decision

**The tool may be given a Jellyfin URL and API key, and works without them.**

The two fields are part of onboarding and may be left blank. Blank is not a
degraded state: it is what the tool does today, without a warning, a banner or a
disabled-looking screen.

When they are filled in, the connection is used for exactly three things:

1. **Checking the release date format.** The connection test reads
   `ReleaseDateFormat` and says so plainly when it is not `yyyy-MM-dd`, because
   that setting silently discards every date the tool writes.
2. **Making a sidecar the tool has just rewritten appear**, by refreshing that
   one item.
3. **Confirming that the server sees what was filed**, which is what onboarding
   ends on.

And it is bound by three rules:

- **Never in the filing path.** A server that is down, moved, or answering with
  a stale key does not stop, delay or fail a move. Filing writes files; that is
  all it has ever needed and all it may need.
- **No feature may require it.** Anything built on the connection has to have an
  answer for the blank case, and the answer may be "this does not happen".
- **The item id is found, not configured.** The user gives a URL and a key, not a
  path mapping.

## Alternatives considered

- **No connection at all — the status quo.** Rejected, but narrowly, and it is
  the alternative worth the most respect: everything the tool files is correct
  without it. What decided it is the date format. A user whose server is set to
  something else gets a library with no dates, and neither side reports anything;
  without the API the whole answer is a troubleshooting entry the user has to
  think of looking for. One `GET` turns that into a sentence at setup time.
- **A required part of the setup.** Rejected. It buys the same three things and
  costs a setup that cannot be completed until a second service is reachable,
  for a tool whose value proposition is that it is set up once and then left
  alone. It would also make a media server outage into a tool outage, or invite
  exactly the fallback path the hard rule in `AGENTS.md` forbids.
- **A path mapping as a third setting** — the user tells the tool what its target
  directory is called inside Jellyfin. Rejected: the tail match derives it, and a
  setting whose only correct value can be computed is a setting that will be
  wrong on somebody's machine.
- **Reporting the changed path instead of refreshing an item**, through
  `POST /Library/Media/Updated`. Rejected. It looks like the cheaper route — no
  enumeration, no id — and it is not: it needs the path *as the server sees it*,
  which is the substitution the enumeration exists to derive, and it is ignored
  inside the tolerance window, which is the only case that needs an API at all.
  Its failure mode decides it: a path it does not recognise gets a 204, exactly
  like one it does.
- **Waiting out the one-minute window instead.** The tool could write a sidecar,
  sleep past the tolerance, and trigger a plain library scan. Rejected as a
  substitute rather than on its own merits: triggering that scan is itself an API
  call, so it does not avoid the connection, and it makes the whole library the
  unit of work for one changed file.

## Consequences

- Onboarding gains two optional fields and a connection test. The test does more
  than say "reachable": it reads the date format, and it proves the path
  substitution by matching at least one filed file. A connection that answers but
  matches nothing is a real state and has to be said out loud, because it is the
  one that looks fine and does nothing.
- A configured connection that fails is an entry in the operation log
  ([#19](https://github.com/prdb-net/prdb-ordeno/issues/19)) and nothing more —
  not a failed filing, and not a modal the user has to dismiss before the tool
  will carry on. The one exception is the connection test itself, which the user
  is standing in front of and which exists to fail out loud.
- Finding an item costs a library enumeration. Measured against the probe:
  58 movies in about 43 KB, so a library of ten thousand scenes is a few
  megabytes. That is per refresh *batch*, not per item, and the id may be kept
  per filed file — as a cache that is re-derived when it misses, never as a
  configuration. Jellyfin item ids do not survive a library being removed and
  added again.
- [#21](https://github.com/prdb-net/prdb-ordeno/issues/21) keeps its
  documentation entry, because it is the answer for everyone who left the fields
  blank, and gains a second half: when the connection exists, the tool says it
  first.
- [#18](https://github.com/prdb-net/prdb-ordeno/issues/18) can write a sidecar
  knowing how a rewrite is made visible. *When* a rewrite happens is still open,
  and this decision does not settle it.
- The seam from ADR 0008 grows a second, optional half. A media server
  implementation is a sidecar writer and, if there is anything worth talking to,
  a client. Plex and Emby arrive with the first and without the second until
  somebody has measured one, which is the same rule the sidecar writer already
  follows.
- The stored key is a credential in the tool's database, under the same rule as
  the prdb key in [ADR 0009](0009-configuration-is-collected-by-onboarding.md):
  not logged, not returned to the browser once saved.
