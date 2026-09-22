# Contributing

## Order of work

Specification, then ADR, then code. A decision that is not written down is not a
decision, and a rule that exists only in a document is not a rule — it is an
analyzer or it does not bind.

When a proposal contradicts an accepted ADR, say so and propose a superseding
ADR. Do not diverge quietly. When one of specification, ADR or code changes,
state in the same change what else must change.

## Adding an ADR

Take the next free number — `docs/adr/` is dense and numbers are never reused —
and write five sections: **Status**, **Context**, **Decision**, **Alternatives
considered**, **Consequences**. Add a row to `docs/adr/README.md`.

*Alternatives considered* is not a formality. An ADR that lists no losing option
has not recorded a decision, only an outcome, and the next person cannot tell
whether the alternative was rejected or never seen.

An accepted ADR is never edited. A decision that still stands but needs more
detail gets a **dated amendment** inside it; a decision that changed gets a
**superseding ADR**, and the old one keeps its text so the reasoning that was
wrong stays readable.

Where an ADR is accepted ahead of the evidence, it states a **revisit
condition**: a fact which, if it turns out to be true, supersedes it. ADR 0020's
condition fired on a real browser build, which is what the mechanism is for.

## Adding a rule

A new architectural or code rule is delivered as three things, in this order of
importance:

1. the analyzer, in `src/Varve.Analyzers/`,
2. its tests, with at least one violating and one conforming sample,
3. the page in `docs/rules/VARVE000n.md`, linking to the ADR that motivates it.

A rule without an analyzer is a suggestion. Ids are allocated in
`docs/adr/0004-enforcement-by-analyzers.md`; take the next free one and do not
reuse a retired id.

## Adding a dependency

Every third-party package needs an ADR, including build-time and test-time ones
(constraint 4). The ADR states what the package does, why the BCL does not
suffice, and whether it ships as a runtime dependency. Anything shipping a
native asset is out under constraint 1, with no exception process.

Versions are central, in `Directory.Packages.props`. Project files carry no
`Version` attribute. Do not take a version from memory — resolve the current
stable version when you add it.

Two gates run in CI. `eng/dependency-register.cs` fails a `PackageVersion` with
no `Adr` attribute, or one citing a number no ADR file matches.
`eng/native-assets.cs` fails a package that contributes a native runtime asset
to a packable project's restore closure — ADR 0009's amendment scopes the ban
to shipped artifacts, so a benchmark-only native dependency is allowed and a
shipped one is not.

## Suppressions

A suppression carries a justification that cites an ADR number:

```csharp
[SuppressMessage("Varve", "VARVE0001:Layer direction",
    Justification = "ADR 0003: recorded exception, see the open question on Varve.Shacl.")]
```

`VARVE0008` will enforce the citation once it is implemented. Until then it is a
review obligation.

## Conformance

The W3C test suites are the acceptance gate. A feature is not done until its
manifest entries pass or each failure carries a written, justified exemption.

`tests/Varve.Conformance.Tests/baseline/passing.txt` lists the tests that pass
today, sorted by test IRI. `eng/ratchet.cs` fails the build when one of them
stops passing, and prints tests that newly pass. When your change makes tests
pass, update the baseline in the same pull request.

## Branches and pull requests

**Nothing reaches `main` except through a pull request.** Never commit to
`main`, and never push it. The branch you are on is one of:

| Branch | For |
|---|---|
| `milestone/<id>` | a milestone, or a part of one — `milestone/3a`, `milestone/3b` |
| `fix/<topic>` | anything else — `fix/3a-closeout`, `fix/ratchet-exemptions` |

**One pull request per milestone part**, and **the pull request body is the
report**: what was decided, what was measured, what was found, what is left.
Not a changelog — the commits are the changelog. A reviewer should be able to
decide from the body alone whether the work is sound, and should not have to
reconstruct a finding from a diff.

When a pull request depends on one that has not merged yet, branch it from that
one rather than from `main`, open it as a draft, and rebase onto `main` when
the parent merges. A stack is honest about the dependency; a branch that
silently contains someone else's unmerged work is not.

Only the repository owner merges.

## Commits

Conventional commits. One logical change per commit. A commit that changes a
rule, its tests and its documentation together is one change, not three.

## Style

`.editorconfig` is enforced at build time and warnings are errors. Prose in
documentation is plain and precise; diagrams as Mermaid or SVG when structure is
easier to see than to read.
