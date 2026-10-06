# 0085 — A release per milestone, and the next milestone waits for it

## Status

**Accepted — filed unaccepted by the pre-release session of #63, 2026-10-06**
(ADR 0066). Acceptance is the maintainer's act on the pull request.

**Complements [0029](0029-publishing-and-versioning.md) and
[0035](0035-semantic-versioning.md); supersedes nothing.** 0029 decided how a
version reaches nuget.org and where its number comes from; 0035 decided what
the number promises. Neither decided *when* a release is made. This does.

**Revisit condition:** a milestone held more than four weeks by its
predecessor's release for a reason outside the repository (nuget.org, the
trusted-publishing policy, the `release` environment's reviewer).

## Context

Thirteen milestones have landed on `main` and nothing has been released. The
first tag, `v0.1.0-preview.1`, has been "the next thing" since milestone 3a.

The cost showed when the release was finally rehearsed (#63). Packing under
the tag found that **MinVer ignored it**: with no `MinVerTagPrefix`, a `v`
prefixed tag is "not a version", and all ten packages would have gone up as
`0.0.0-alpha.0.49` — permanently, since a version cannot be replaced. The dry
run on every pull request could not see it, because it has no tag to read. The
same rehearsal found the publish workflow skipping three gates CI runs and
creating no release notes. Every one of those defects was introduced at some
milestone and stayed invisible because no milestone ended in a release.

And the changelog had no shape for a release at all: `eng/changelog.cs`
rendered every commit under `[Unreleased]`, divided by milestone, and said
outright that its release mode was "not written yet, because writing it now
would be guessing at a shape no release has taken".

## Decision

### Every milestone ends in a release

A milestone is a section of `eng/changelog-sections.txt`, and **it starts when
its line is added**. The milestone before it is finished by a release: a `v*`
tag on `main`, published by `publish.yml`. The number is ADR 0035's to decide
and stays `0.x` and prerelease under ADR 0029; this decides only that one is
cut. A milestone with nothing to publish still releases — the packages are
rebuilt and the changelog section says what changed.

The first release, `v0.1.0-preview.1`, closes every milestone before it, from
milestone 1 to milestone 6c: none of them was released on its own.

### The release is cut by the changelog tool

1. `dotnet run eng/changelog.cs -- --release <version>` cuts what is under
   `[Unreleased]` into `## [<version>] - <date>`, leaves `[Unreleased]` empty,
   and writes the compare links (`[Unreleased]` against the newest tag, each
   version against the one before it). The links are built from
   `PackageProjectUrl`, the one place the repository's location is written
   (ADR 0029).
2. The cut is committed as `chore(release): <version>`, with its `Refs #N`,
   and lands like any other commit (ADR 0088): through a pull request, or a
   `land/` branch whose head `main` is then fast-forwarded to.
3. The maintainer tags **that commit** `v<version>`; the `v*` ruleset
   restricts tags to the maintainer (GOVERNANCE.md).
4. `publish.yml` runs every gate, checks with `eng/changelog.cs --check` that
   the tagged `CHANGELOG.md` has the version's section, publishes, and then a
   separate job creates the GitHub release with that section as its notes.

### A released section is fixed

Once cut, a version's section is copied from `CHANGELOG.md` as it stands by
every later run; only `[Unreleased]` is regenerated, from the commits after the
newest release — after its tag, or before the tag exists after the commit that
cut it. What a version contained does not change once it is on nuget.org, so
neither does its section.

### The next milestone waits for the release

`eng/release-pending.cs` fails a change that adds a section to
`eng/changelog-sections.txt` while the base's newest section has **no `v*` tag
at or after its first commit**. It is a job in `eng/ci.cs` (ADR 0036) and a
job of its own, **`release pending`**, in `ci.yml`, so a pull request shows by
name what it waits on.

A change that adds no section passes whatever the release state, because the
fixes that make the release possible, and the release commit itself, are what
must land while it is pending. The tag, not the changelog section, ends the
pending state: a cut that was never tagged is exactly what the gate is for.

## Alternatives considered

- **Release when ready**, the state until now. Lost on the evidence above:
  "ready" never came, and the publish path rotted unexercised.
- **A release on a calendar.** Decouples the release from the unit of work
  the changelog and the ADR index are already divided by, and would cut
  releases in the middle of a milestone's ADRs.
- **A release per pull request.** Every version on nuget.org is permanent;
  dozens of prereleases a milestone would be noise for a consumer and a
  maintainer approval each.
- **Gate on the milestone's issue being closed.** Needs a network call and a
  token in a gate that must run offline in the devcontainer — ADR 0033's reason
  for not checking issues either.
- **Gate on the changelog section rather than the tag.** A section is cut
  before the tag; gating on it would let the next milestone start on a release
  that was never published.
- **Regenerate every section from tags**, with no fixed text. The release
  commit has to contain its own section before the tag exists, so the
  generator needs the cut commit as a boundary anyway; copying the released
  text also keeps a version's section from changing if a commit's subject
  parsing ever does.

## Consequences

- **Milestone 7 cannot start until `v0.1.0-preview.1` is tagged.** That is the
  point, and it is the first time the gate binds.
- A release needs the maintainer twice: the tag, and the `release`
  environment's approval. ADR 0032 already put human review on the release;
  this makes it a step of every milestone rather than an event.
- `eng/changelog.cs --check` changes meaning. It compared a regenerated file
  with the committed one, which could never pass once the regeneration was
  committed (the commit is itself listed) and was run by nothing. It now
  checks the newest tag's section, and runs in `eng/ci.cs` and `publish.yml`.
- A commit landed between the cut and the tag is in neither the release's
  section nor `[Unreleased]`. The procedure tags the cut commit, and
  `--check` fails a tag whose `CHANGELOG.md` has no section.

## Checks

- **Checked against the accepted ADRs** (0001–0084). Complements **0029** (the
  tag, the workflow; adds the release notes and the GitHub release, which 0029
  did not decide) and **0035** (the number). Consistent with **0032** (human
  review gates a release), **0033** (the release commit carries its issue),
  **0036** (the gate is a job in `eng/ci.cs`) and **0066** (filed unaccepted).
  No conflict with any.
- **Layer ownership.** None; this is the build and the release process.
- **Analyzer rule.** None. The gate reads git history and tags, which no
  analyzer can see.
- **Open questions owned.** None.
