# Releases

**Format version: 1.** How a Varve release is proposed, checked and cut
([ADR 0102](adr/0102-a-release-is-a-descriptor.md)). This file is the
versioned schema of the release descriptor plus the rules around it. A change
to the format bumps its version and says here how existing descriptors move;
a descriptor declares the format it conforms to, and is validated against
that version's rules.

The shape is taken from
[mindovermachine-dev/actor-indexed-determination](https://github.com/mindovermachine-dev/actor-indexed-determination)'s
`spec/release-format.md`. Where ours differs, ADR 0102 says how and why.

## 1. The descriptor

One file per release, `releases/<version>.yaml`. **The file is the release
request**: adding it in a pull request proposes the release, and landing that
pull request at its approved head cuts it.

```yaml
format: 1                       # mandatory; the format this file conforms to
version: v0.1.0-preview.2       # v<SemVer 2.0.0>, prerelease allowed; equals the file's name
title: The protocols and the server   # one line; the tag message is "<version> — <title>"
date: 2026-10-20                # the preparation date; CHANGELOG.md's heading
commit: 0123…cdef               # optional; a retro-cut only (§3). Omitted is normal
basis:                          # mandatory; resolved at the commit under release
  - issue: 11                   # each milestone issue it closes, at least one
  - storage-format: 1           # Varve.Store's FormatVersion.Current, exactly one
  - adr: "0091"                 # every ADR first shipped by this release, and no other
  - adr: "0092"
accepted-by: mailto:emil@okkels-klein.dk   # the human who approves the pull request
summary: |                      # mandatory; the release notes, written
  What a consumer of the packages needs to know about this release.
```

`dotnet run eng/release.cs -- --draft <version>` prints a descriptor with the
`adr:` lines and the storage format filled in, and the ledger keys first
shipped listed as comments, for information.

**The YAML is a strict subset**, read by `eng/lib/Releases.cs` without a
package (hard constraint 4):
- top-level `key: value` scalars, plain or quoted;
- one list, `basis:`, with one `  - kind: value` line per entry;
- `|` block scalars, indented with spaces;
- whole-line `#` comments between keys.

Anything else is refused with its line, and so are an unknown field and a
field given twice. Quote ADR numbers (`"0091"`), so that no other YAML reader
takes them for numbers.

## 2. The rules

1. **The version is the identity.**
   - It matches `v<major>.<minor>.<patch>[-<prerelease>]` (SemVer 2.0.0, no
     build metadata, which NuGet ignores) and equals the file's name.
   - It comes after every `v*` tag by SemVer precedence.
   - It is never reused: a tag is a fact about history, and a version on
     nuget.org cannot be replaced.
2. **The title carries no version.** The tag message and the GitHub
   Release's title are composed as `<version> — <title>`.
3. **`date` is the preparation date**, the day the descriptor was written.
   It is the heading of the version's section in `CHANGELOG.md`, which is a
   projection and must not depend on when the cut ran.
4. **`commit` is optional, and omitting it is normal.** A pull request
   cannot know the sha it will land as, so the default is "the commit that
   lands this file". When set, it must be a full sha and an ancestor of
   `main`; its one legitimate use is the retro-cut in §3.
5. **`basis` must resolve**, at the commit under release:
   - **`issue: N`**, one line per milestone issue the release closes, at
     least one: a commit in the range from the previous `v*` tag closes `#N`
     with a trailer line, `Closes #N`, and the release workflow confirms the
     issue exists. The release is what completes the milestone (§7); the
     trailer is what makes the descriptor necessary (§3).
   - **`adr: NNNN`**: **complete both ways**. The `adr:` lines are exactly
     the ADRs added since the previous `v*` tag, and each has status
     Accepted. A missing one fails with the line to add.
   - **`storage-format: n`**: equal to `FormatVersion.Current` in
     `src/Varve.Store/Log/FormatVersion.cs`. A release that first writes a
     format version says so in a line a human approved, and from then on that
     format is read for ever ([ADR 0072](adr/0072-format-version-1.md)).
6. **`accepted-by`** names a human in `eng/identities.json` who may approve
   for every agent that authored a commit in the range: its responsible human
   or a delegate ([ADR 0087](adr/0087-the-identity-map.md)). The approval on
   the pull request's head is the evidence; this line names who gave it.
7. **`summary` is mandatory.** Release notes are written and reviewed in the
   pull request, not generated from a commit range after the fact.
8. **`prerelease` is derived**, from the version: it has a prerelease part.
9. **Immutable once cut.** A version whose tag exists is skipped, and a pull
   request that edits its descriptor fails. Corrections ship as a new version.
10. **One release at a time.** Two pending descriptors are refused.

**A release cut before descriptors is recorded, not cut.**
`v0.1.0-preview.1` was tagged by hand under ADR 0085. Its descriptor was added
afterwards:
- `commit` names the tagged commit, `a9f0c24`;
- `basis` names the milestone issues it completed, #4 to #9;
- its `CHANGELOG.md` section, as cut, is its summary.

A descriptor whose tag exists and whose `commit` is that tag's commit is such
a record. Its shape and its storage format are checked, it is never cut, and
it is immutable like any other. Only a version tagged before ADR 0102 needs
one.

## 3. How a release lands

1. **Propose.** The milestone's close-out pull request adds the descriptor,
   with a commit whose trailer line says `Closes #<milestone issue>`. It
   brings `README.md`'s Status section and the roadmap's heading current to
   the release (§7). It also runs `dotnet
   run eng/changelog.cs -- --release <version>` and commits `CHANGELOG.md`.
   The `release pending` check fails a pull request that closes a milestone
   issue without a descriptor naming it, and `eng/release.cs --check` (the
   `pipeline (devcontainer)` job) validates the descriptor and prints the
   landing instruction below.
2. **Approve.** The responsible human approves the head (`approve <sha>`),
   as for any pull request with an agent's commits.
3. **Land at the approved head.** The maintainer pushes the approved head,
   unchanged, to `land/<name>`; CI and `agent review` run there, and `agent
   review` admits the agent's commits because the head is an approved pull
   request's (ADR 0087, amended 2026-10-08). Then the maintainer
   fast-forwards `main` to it:

   ```bash
   git push origin <approved head>:land/release-<version>
   git push origin <approved head>:main
   ```

   A `land/release-**` branch name also dry-runs the release on that head
   (§5).
4. **Cut.** The push to `main` starts `.github/workflows/release.yml`:
   - **plan** validates and resolves the descriptor and applies the head
     guard;
   - **gates** runs all of `ci.yml` at the commit under release;
   - **cut** has the gates App create the annotated tag, then close the
     issues its basis names.

   The tag starts `publish.yml`, which runs its gates, reads the notes from
   the descriptor, publishes to nuget.org, and has the gates App create the
   GitHub Release, marked prerelease when the version is one.

**The head guard.** A release is cut only at a commit that carries a
successful `agent review` check run from the gates App, which that App posts
only on an approved pull request's head or a `land/` head. **A descriptor
landed by the merge button is not cut and leaves no tag**: the merge commit
is not the head anyone approved. It is recovered by a **retro-cut**: a new
pull request that adds `commit: <approved head>` to the same descriptor, which
is still untagged and so still editable. That head is an ancestor of `main`
and carries the run, so the push that lands the edit cuts the release there.

**A failed gate leaves no tag.** Fix forward, then dispatch the workflow with
`dry_run: false`, or retro-cut if the fix's own landing is the head to cut.

## 4. CHANGELOG.md

`CHANGELOG.md` is the projection of `releases/`:
- each descriptor's `## [<version>] - <date>`, its title and its summary,
  newest first by precedence;
- each earlier `.md`, verbatim;
- the compare links.

`dotnet run eng/changelog.cs` writes it, and `-- --check` fails on any
difference, a hand edit included. There are no `[Unreleased]` entries: `dotnet
run eng/changelog.cs -- --unreleased` prints the commits since the newest tag
by milestone, as material for the next summary.

## 5. The dry run

The dry run creates nothing. It runs every gate at the commit under release,
and the step summary says which tag would have been created, on which commit,
by whom and with what message. It does not stop at a finding: the plan lists
what would stop the cut and the gates and the tagging identity still run, so
one run shows everything in the way. Then it fails. It runs on:
- `workflow_dispatch` of **Release** with `dry_run: true`;
- every push to `land/release-**`, on the head about to become `main`.

With nothing pending in `releases/`, it plans `tests/fixtures/releases/dry-run/`
instead, so that the path is exercised before a release needs it.

## 6. Closing keywords

GitHub closes an issue when a closing keyword (close, fix or resolve, in any
tense) and a number reach the default branch, wherever they stand in a commit
message. That is how milestone Operability (#12) was closed by a commit
describing the issue-reference gate. So **a closing keyword with an issue
number stands only as a trailer line of its own**, `Closes #N`, and
`eng/issue-refs.cs` refuses it anywhere else, the subject and prose
included. In prose, write a bare #N.

## 7. Status is a checked projection

`README.md` ships inside every package, so what it says about the project is
published with each version and cannot be corrected afterwards.
`v0.1.0-preview.1` shipped one two milestones stale: "Milestone 5c", "Nothing
is published yet", 2,803 conformance cases where the baseline held 2,927.
`eng/status.cs --check` (in `eng/ci.cs`, and in `eng/release.cs --plan` at the
commit under release) checks four statements against what they describe:

1. **The Status section's opening line names the latest release**, which is
   the newest descriptor in `releases/`, cut or being cut. It also names the
   last milestone that release closed: of its basis issues, the latest in the
   roadmap's table, written as "milestone(s) … N".
2. **The conformance table's total** equals the line count of
   `tests/Varve.Conformance.Tests/baseline/passing.txt`.
3. **The package table has a row for every packable project**, with the layer
   its project declares (`<ArchLayer>`), and no "not built" row names a
   project that exists.
4. **A milestone heading in `docs/roadmap.md` says *(complete)* exactly when
   every issue the roadmap's table gives it is in a release's basis.** A
   milestone is complete when it is released, not when its issue is closed;
   the cut closes the issue to match. Every descriptor in `releases/` counts,
   the pending one included: it is the one being cut, and the files must say
   so at the commit that is tagged.

**So *(complete)* means "in a descriptor", not "tagged".** A descriptor
landed by a merge commit is not cut (§3), yet the roadmap already shows its
milestone *(complete)* and the README already names its version, with no tag
behind either. That state is not a resting place: **the retro-cut is the
required next step**, a pull request adding `commit: <approved head>` to the
same descriptor, and until it lands `main` describes a release that does not
exist yet.

The failure paths are `tests/fixtures/status/`.

## 8. Why the descriptor and not the tag

The tag is the artifact; the descriptor is the decision that produced it. A
release cut by a command is a decision taken outside the record: nothing
reviewed which commit, which notes, which storage format the world would be
promised. As a file, the release has:
- a principal: the pull request's approver, named by `accepted-by` and proven
  by the head guard;
- a basis: the issue, the ADRs and the storage format, each checked against
  the repository rather than taken on the prose's word;
- a review: the pull request.

After the approval no step is a person's: the gates App tags with an
installation token, which ADR 0087's map records as an executor, never as
the one who decided. That token also makes the publish work at all, because
a tag pushed with `GITHUB_TOKEN` starts no workflow.
