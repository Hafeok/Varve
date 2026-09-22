# Security policy

## Reporting a vulnerability

**Report privately. Do not open a public issue.**

- **GitHub private vulnerability reporting** — the *Report a vulnerability*
  button under this repository's Security tab. Preferred, because the report,
  the discussion and the advisory stay in one place.
- **Email** — [emil@okkels-klein.dk](mailto:emil@okkels-klein.dk), if you would
  rather not use GitHub or cannot.

**You will get an acknowledgement within five working days.** If you do not,
assume the message went astray and send it again — a silence is a failure on
our side, never a judgement about your report.

A useful report says what an attacker gains, not only what the code does wrong.
The most valuable thing you can send is an input that reproduces it: for a
parser, the bytes; for the store, the sequence of operations.

After acknowledgement you get an assessment and a plan with dates. We will tell
you if we think it is not a vulnerability, and why, rather than letting the
thread go quiet. Fixes are released with an advisory naming the affected
versions, and you are credited unless you ask not to be.

**Please give us time to ship a fix before disclosing publicly.** There is no
fixed embargo and no bug bounty; there is a single maintainer, and the honest
answer is that a date will be agreed with you rather than imposed on you.

## Supported versions

| Version | Supported |
|---|---|
| `0.x` prerelease | The most recent prerelease only |
| `1.x` and later | Not yet released |

Nothing has been published to nuget.org yet. Until 1.0, only the most recent
prerelease gets fixes: there is no support window on a version whose API is
still allowed to change ([ADR 0035](docs/adr/0035-semantic-versioning.md)). This
table gains real rows at 1.0, when the public API freezes and a version becomes
something to stay on.

## What we consider a vulnerability

**Varve's parsers accept untrusted input. That is their job**, and it is where
this project's security surface mostly is. `Varve.Turtle` reads N-Triples,
N-Quads, Turtle and TriG straight off a stream, and a server built on it will
read documents from anyone who can reach it. So these are in scope:

- A crash, a hang, or unbounded memory growth from a well-formed or malformed
  document — including deeply nested collections, pathological IRIs, and inputs
  that behave differently when split across a buffer boundary.
- Any way to make a reader produce quads that the document does not contain, or
  to make the writer emit a document its own reader would read differently.
  A round trip that is not one is a correctness bug and can be a security bug.
- Path traversal or unexpected file access from a relative IRI or a base IRI.
- Once the store exists: reading data a caller is not entitled to, writing to a
  log the caller may not write to, and any way to recover a term whose key has
  been destroyed in erasure mode.
- Once the server exists: anything that bypasses the token validation or the
  claim mapping in [ADR 0037](docs/adr/0037-server-authentication.md).

Out of scope: a denial of service that needs an input larger than the caller's
own configured limits, and anything requiring write access to the dataset
directory, whose boundary is the filesystem's permissions and is documented as
such.

## How we look for these ourselves

- **The W3C conformance suites** are the acceptance gate — 883 cases, no
  exemptions — and roughly a third of them are negative cases whose whole
  purpose is to be rejected.
- **The chunk-boundary oracle** parses every manifest input whole, then again
  split at every byte offset, and requires the same answer
  ([`docs/testing.md`](docs/testing.md) §2). It has found nine defect classes,
  two of which produced *wrong quads rather than errors*. Every streaming
  reader runs it, enforced by a guard rather than remembered.
- **Property-based tests** over generated documents, to reach what the corpus
  does not ([ADR 0025](docs/adr/0025-property-based-testing.md)).
- **Zero allocation per quad**, asserted as a difference between two documents.
  A parser that allocates per quad is a parser an attacker can make allocate.

**Fuzzing is part of the strategy and is not yet built.** The oracle is a
deterministic cousin of it and the property tests generate documents, but
neither is a coverage-guided fuzzer, and neither will find what one does. A
continuous fuzzing target for the readers is scheduled with the durable
backend; until it exists, treat this section as a statement of intent rather
than of coverage, because that is what it is.

**[OpenSSF Scorecard](.github/workflows/scorecard.yml)** runs weekly over the
repository's own supply chain — pinned actions, branch protection, dependency
freshness — and uploads to code scanning.

## Dependencies

There are thirteen, all build-time or test-time, none shipped at runtime, and
every one names the decision that admits it
([ADR 0009](docs/adr/0009-dependency-policy-and-register.md)) — a gate fails the
build otherwise. A package that ships a native asset is refused outright, and a
second gate enforces that over the restore closure of everything packable.
Dependabot watches actions and NuGet weekly.

That small a surface is deliberate, and it is the most useful security property
this project has: **there is very little here that is not ours**, so there is
very little that can be compromised without us being the ones who compromised
it.
