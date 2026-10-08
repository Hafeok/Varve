# 0102 — A release is a descriptor, and landing it cuts it

## Status

**Proposed — filed unaccepted by the release-descriptor session of #73,
2026-10-08** (ADR 0066). Decided by the maintainer on the plan for #73;
acceptance is the maintainer's act on the pull request.

**Supersedes [0085](0085-a-release-per-milestone.md)** whole. 0085's rule
that every milestone ends in a release stands, restated here; how a release
is cut, where its notes come from and what holds the next milestone change.

**Supersedes one ruling of [0088](0088-only-checked-commits-reach-main.md)**,
`HumanReviewGatesReleases`: a release is still gated by human review, but the
review is the approval of the descriptor's pull request, not the `release`
environment's reviewer (0088's amendment of 2026-10-08).

**Amends [0087](0087-the-identity-map.md) rule 3** so that an approved pull
request's head can land through `land/` (0087's amendment of 2026-10-08).

**Complements [0029](0029-publishing-and-versioning.md)** (the tag publishes),
**[0035](0035-semantic-versioning.md)** (the number) and
**[0072](0072-format-version-1.md)** (the storage format a release names).

**Credit.** The descriptor's shape is taken from
[mindovermachine-dev/actor-indexed-determination](https://github.com/mindovermachine-dev/actor-indexed-determination):
its `spec/release-format.md`, its release workflow and its validator were read
for behaviour; nothing is copied, and ours is C# under `eng/`. Where this
differs, the differences are listed below with their reasons.

**Revisit condition:** a descriptor that was valid, landed at its approved
head, passed every gate, and still could not be cut for a reason outside the
repository (the App's permissions, the tag ruleset, nuget.org). That is the
path this ADR claims has no human step; the first time it needs one, it is
revisited.

## Context

A release is a decision, and this repository files decisions: an ADR, a
ledger entry, an approval on a head. Under ADR 0085 the one decision still
taken by a command was the release itself. `eng/changelog.cs --release` cut a
section, a commit carried it, and **the maintainer tagged that commit by
hand**. `release pending` could only remind the next milestone's pull request
when the tag had slipped.

Three things were wrong with that shape:

- **Nothing reviewed the release.** The pull request that carried the cut was
  reviewed for its code; the decision to publish was the tag, made afterwards
  by a person with a credential, and then a second approval in the `release`
  environment. Neither approval was on a file anyone could read in the
  history.
- **The notes were generated.** The release's text was the commit subjects
  of its range. They are a record of the work, not an account of the release
  for someone consuming the packages.
- **The tag was not checked against anything.** Whether the storage format
  the release writes was the one ADR 0072 promises to read for ever, whether
  its ADRs were accepted, whether its milestone was done: all of that was a
  sentence in a report.

`actor-indexed-determination` had solved the same problem by making the
release a file: adding `releases/<version>.yaml` to a pull request proposes
the release, and merging it cuts it. Its history also records the one way
that shape fails, at v5.14.0 and v5.15.1: the ratified head was not the
merged head, so what was cut was not exactly what was approved.

## Decision

### The descriptor

A release is proposed by **`releases/<version>.yaml`**, a strict subset of
YAML read by `eng/lib/Releases.cs` (no package, hard constraint 4):

```yaml
format: 1
version: v0.1.0-preview.2
title: The protocols and the server
date: 2026-10-20
basis:
  - issue: 11
  - storage-format: 1
  - adr: "0091"
accepted-by: mailto:emil@okkels-klein.dk
summary: |
  The release notes, written.
```

- **`version`**: SemVer 2.0.0 with its prerelease, prefixed `v`, without
  build metadata; equal to the file's name; after every `v*` tag by
  precedence; never reused. Their format forbids a prerelease; ours needs
  `-preview.N` (ADR 0029).
- **`title`**: one line, no version; the tag message is `<version> —
  <title>`.
- **`date`**: the preparation date, which is `CHANGELOG.md`'s heading. Not
  in theirs; ours needs it because the changelog is a projection and must be
  deterministic.
- **`commit`**: optional. Omitted is normal: the release is cut at the commit
  that lands the descriptor. Set, it is a retro-cut, and it must be an
  ancestor of `main`.
- **`basis`**: mandatory, and every line is resolved at the commit under
  release.
  - `issue: N`, one per milestone issue the release closes, at least one:
    a commit in the range from the previous `v*` tag closes it with a
    trailer line (`Closes #N`), and the release workflow confirms it exists.
  - `adr: NNNN`, **complete both ways**: exactly the ADRs added since the
    previous tag, each with status Accepted. Ledger keys are not basis lines;
    `--draft` prints them as information.
  - `storage-format: n`, exactly one: equal to `FormatVersion.Current` in
    `Varve.Store`. **ADR 0072's read-forever commitment becomes a reviewed
    line**: the release that first writes a format says so, in a file a human
    approved.
- **`accepted-by`**: `mailto:` a human in `eng/identities.json` who may
  approve for every agent that authored a commit in the range (ADR 0087).
  The approval on the pull request's head is the evidence; the line names who
  gave it, and the validator checks they could.
- **`summary`**: the release notes, written and reviewed in the pull request.
- **`prerelease`** is derived from the version, not declared.
- **Immutable once cut.** A version with a tag is skipped, and a change to its
  descriptor fails `eng/release.cs --check`; a correction is a new version,
  which NuGet's immutability requires anyway.

The format, the rules and why the descriptor rather than the tag are in
`docs/releases.md`, which is versioned with the format.

### Landing it cuts it

`.github/workflows/release.yml`, on a push to `main` that changes `releases/`:

1. **plan** (`eng/release.cs --plan`): validates every descriptor, finds the
   one with no tag (one at a time), resolves its basis at the commit under
   release, checks the project's status there (below), and applies **the
   head guard**: that commit carries a successful `agent review` check run
   from the gates App.
2. **gates**: `ci.yml`, called at the commit under release, which may be
   older than `main`'s head when `commit` is set. Every gate CI runs on a pull
   request, the AOT publishes and the public API baselines included, and the
   ledger, register and licence-header gates. The jobs that judge a range
   (`dco`, `release pending`, issue references) are skipped: the range landed
   through them already, and its base is not this event's.
3. **cut**: the gates App, with a token minted from the `gates` environment,
   creates the annotated tag and pushes it, then closes the issues its basis
   names.

**A valid descriptor whose gates fail is not cut, and leaves no tag.** It is
retried by dispatching the workflow once the fix has landed.

The tag starts `publish.yml`, which runs its gates again, reads the GitHub
Release's title and notes from the descriptor, pushes to nuget.org, and then
creates the GitHub Release, with the gates App's token.

### The App tags, never GITHUB_TOKEN, never a person

The tag and the GitHub Release are created by the gates App (`varve-gates`).

- **The identity rule.** ADR 0087's map says who decides and who executes:
  the maintainer accepted (the approval on the head, named by `accepted-by`)
  and the App executed. A composite identity, as theirs uses ("Emil Klein -
  Claude Code AI"), says neither.
- **The mechanism.** A tag pushed with `GITHUB_TOKEN` starts no workflow, so
  `publish.yml` (`on: push: tags: [v*]`) would never run and nothing would be
  published. A push by an App's installation token is an event like any
  other, and starts it.

The App needs Contents: read & write. It bypasses the `v*` tag ruleset, and
the `gates` environment admits `v*` tags for `publish.yml`'s release job.
The maintainer's bypass stays, to repair a tag; a tag made by hand publishes
nothing, because `publish.yml` fails without the version's descriptor.

The `release` environment keeps its restriction to `main` and `v*` tags and
loses its reviewer. Its name stays in the OIDC claim, so nuget.org's trusted
publishing policy is unchanged.

### The head guard, and why it is needed here

Their miss, the ratified head not being the merged head, **can happen under
ADR 0088 as it stands.** 0088 lets a pull request merge by the merge button,
and the session pull requests that will carry descriptors have so far landed
that way (#70, #71): the merge commit GitHub creates is not the head that was
approved. A descriptor cut at such a commit would be a release nobody
approved exactly.

So the release is cut only at a commit that is itself an approved head: **the
commit under release carries a successful `agent review` check run from the
gates App**. That App posts its verdict only on a pull request's head
(`pull_request_target`, `issue_comment`) or a `land/` head, and never on a
merge commit. A descriptor landed by the merge button is therefore not cut
and leaves no tag. **It is recovered by a retro-cut:** a new pull request
with `commit:` set to the approved head, which is an ancestor of `main` and
carries the run. That is the one legitimate use of `commit`.

To make the right landing possible, **ADR 0087 rule 3 is amended**: a `land/`
range with agent commits is admitted when its head is a pull request's head
carrying the gates App's successful `agent review`. The maintainer pushes the
approved head to `land/<name>`, and the existing fast-forward
(`git push origin <sha>:main`, ADR 0088) does the rest: the merged head is
the reviewed head. `eng/release.cs --check` prints this instruction on every
pull request that proposes a release. Other pull requests land as before.

**If ADR 0088 is ever made fast-forward only**, the guard holds trivially and
can stay; it is the guard that was named as the first thing to add if 0088
were relaxed, and 0088 is already that relaxed.

### CHANGELOG.md is a projection

The descriptor is the one place a version's notes are written.
`eng/changelog.cs` renders `CHANGELOG.md` from `releases/` and nothing else:
each descriptor's date, title and summary, newest first by precedence.
`--release <version>` folds a descriptor in;
`--check` compares byte for byte and fails on a hand edit or on a descriptor
not folded in. What is unreleased is a view of the commits, `--unreleased`,
printed and not committed.

### A milestone issue closes with its descriptor

`eng/release-pending.cs` inverts. It no longer reminds; it gates: **a change
that closes a milestone issue** (a commit in the range says `Closes #N`, and
`#N` is in `docs/roadmap.md`'s milestone table, read at the base) **adds a
descriptor whose basis names `issue: N`**, or the `release pending` check
fails. ADR 0085's reminder, a milestone held until the one before it was
tagged, is gone: the release is no longer a step after the merge that can
slip.

### A milestone is complete when it is released

**A milestone is complete when a release names its issue in `basis`**, not
when its issue is closed. The issue is closed to match, by the cut, with the
gates App's token. The `Closes #N` trailer keeps one job: it is the signal
`release pending` reads to require the descriptor in the same pull request.

**`v0.1.0-preview.1` is recorded, not cut.** It was tagged by hand before
descriptors existed. `releases/v0.1.0-preview.1.yaml` records it:
- pinned to the tagged commit, `a9f0c24`;
- with the milestone issues it completed, #4 to #9;
- with its `CHANGELOG.md` section as the summary.

A descriptor whose tag exists and whose `commit` is that tag's commit is a
record. The cut skips it, and only its shape and storage format are checked:
its issues were closed by hand, and its ADRs predate any basis.

**A closing keyword stands only as a trailer line.** GitHub acts on a
closing keyword and a number anywhere in a message that reaches `main`, and
milestone Operability (#12) was closed by a commit quoting one in prose.
`eng/issue-refs.cs` refuses one anywhere but a line of its own (`Closes #N`).
#12 is reopened by hand.

### Status is a checked projection

`README.md` ships inside every package (`eng/package-metadata.cs`), and
`v0.1.0-preview.1` shipped one two milestones stale: "Milestone 5c",
"Nothing is published yet", 2,803 conformance cases where the baseline held
2,927. `eng/status.cs --check` runs in `eng/ci.cs`, and in the plan at the
commit under release. It checks four statements against what they describe:
- **The Status section's opening line** names the latest release (the newest
  descriptor, cut or being cut) and the last milestone it closed.
- **The conformance total** equals the baseline's line count.
- **The package table** has every packable project with its declared
  `<ArchLayer>`, and no "not built" row names a project that exists.
- **A roadmap heading says *(complete)*** exactly when every issue the
  roadmap's table gives it is in a release's basis.

The pending descriptor counts as released. It is the one being cut, and the
files must say so at the commit that is tagged; counting only tagged
descriptors would turn `main` red at every cut until a follow-up edit.

Its failure paths are `tests/fixtures/status/`.

### The dry run

`workflow_dispatch` with `dry_run: true`, and every push to
`land/release-**`, run the plan and every gate and create nothing; the step
summary says what would have been tagged, on which commit, by whom, with
which message. With nothing pending, the dry run plans
`tests/fixtures/releases/dry-run/`, so the path is exercised before it is
first used.

## Alternatives considered

- **Keep the manual tag** (ADR 0085). The release stays an unreviewed act by
  a credential holder, and its basis stays prose. Rejected: it is the escaped
  decision this ADR exists to file.
- **Tag from the changelog heading**: cut when a `## [<version>]` section
  lands. The section was generated from commit subjects, so the notes would
  still not be written, and nothing would resolve a basis; a generated
  heading is a weaker proposal than a reviewed file, and `CHANGELOG.md` would
  be both the input and the output of the cut.
- **GitHub's release UI.** Creates the tag and the notes in one form, outside
  the repository: the decision leaves no reviewed file, runs no gate at the
  commit it tags, and is made by a person's session, which is exactly the
  credential and the human step this removes. It is also the one path that
  tags with whoever clicked, not with the App.
- **A head guard by API only**, comparing the merged pull request's head with
  the commit. Needs the pull request to be found from the commit, which a
  `land/` landing does not have; the App's check run is on the commit itself,
  whichever way it landed.
- **Generated notes with a written preface.** Two places to write a release's
  text, and the generated half is already a command away (`--unreleased`).

## Consequences

- **No human step and no person's credential after the approval.** The
  maintainer approves the descriptor's pull request and fast-forwards `main`
  through `land/`; the App tags, `publish.yml` publishes, and the App creates
  the GitHub Release.
- **The maintainer grants the gates App Contents and Issues: read & write.**
  Until then, the cut fails when its token is minted, before anything is
  created.
- **A close-out brings `README.md`'s Status section and the roadmap heading
  current**, or the plan refuses the cut. A README is never published that
  describes another release.
- **A milestone's close-out pull request carries its descriptor**, and lands
  through `land/` at its approved head. Milestone 7b is the first:
  `releases/v0.1.0-preview.2.yaml`, `Closes #11`, `eng/changelog.cs
  --release`, and a `land/` fast-forward rather than the merge button.
- **`CHANGELOG.md` loses its `[Unreleased]` entries**, which were commit
  subjects; `eng/changelog.cs --unreleased` prints them.
- **`basis` grows with each release**, one `adr:` line per ADR first shipped;
  `eng/release.cs --draft` computes them.
- **The dry-run fixture names the next version**, and goes stale once that
  version is cut; the close-out that cuts it updates or removes it.
- **The `release pending` check keeps its name**, which ruleset 1 requires;
  its meaning changes.
- **A gate failure costs a retry, not a broken release**: no tag, so nothing
  reaches nuget.org.

## Checks

- **Checked against the accepted ADRs** (0001–0101). **Supersedes 0085**;
  supersedes **0088**'s `HumanReviewGatesReleases`; **amends 0087** rule 3.
  Consistent with **0029** (the tag publishes, through trusted publishing,
  the claim unchanged), **0033** (the descriptor's pull request carries its
  issue), **0035** (precedence), **0036** (the validator is a job in
  `eng/ci.cs`), **0066** (filed unaccepted), **0068** (the amendments are
  dated), **0072** (the storage format is a basis line) and **0086** (the
  gate lives in `eng/` until it is ported).
- **Layer ownership.** None; this is the build and the release process.
- **Analyzer rule.** None. The rules read git history, the identity map and
  GitHub's check runs, which no compilation sees. Their failure paths are
  `tests/fixtures/releases/`, run by the `release-fixtures` job of
  `eng/ci.cs`.
- **Open questions owned.** None.
