# 0035 — Semantic versioning

## Status

Accepted. 2026-09-22.

**Complements [0029](0029-publishing-and-versioning.md); supersedes nothing.**
0029 decided *where a version number comes from* — MinVer, from the git tag, one
source of truth. This decides *what the number promises* and what evidence
decides which part of it moves.

## Context

The Mind Over Machine stewardship standard's first item is:

> **Semantic Versioning:** Strict adherence to Semantic Versioning (SemVer) for
> predictable releases.

ADR 0029 already produces SemVer-shaped numbers and already says versions stay
`0.x` and prerelease until SPARQL conformance. What it does not say is what a
consumer may conclude from a version number, or — the part that actually decides
releases — **how anyone tells whether a change is breaking.**

That second question is where SemVer projects usually fail. "Breaking" gets
decided by the author's memory of what they changed, late, under pressure to
ship, and the answer is reliably optimistic. A rule that depends on judgement at
the worst moment is not strict adherence.

Varve already holds the evidence that removes the judgement, and it was built
for a different reason. Every public member of every packable project is a line
in a `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` beside the project,
maintained by the Roslyn public API analyzers, with `RS0016` failing the build
on a public member that is not declared. A fixture in
`tests/fixtures/public-api/` proves the rule fires. Today those files hold 38,
152 and 166 lines for `Varve.Iri`, `Varve.Rdf` and `Varve.Turtle`.

A line leaving `PublicAPI.Shipped.txt` is a removal. A line changing is a
signature change. Neither can happen silently, because the analyzer fails the
build until someone edits the baseline, and that edit is a reviewable diff in
the same commit as the change that caused it.

## Decision

1. **SemVer 2.0.0, strictly**, for every published package.
   - **Major** — a change that can break a consumer who recompiles or who
     rebinds: a public member removed, a signature changed, a type made less
     accessible, behaviour changed in a way a conforming caller could observe.
   - **Minor** — new public surface, backwards compatible.
   - **Patch** — no public surface change; a fix to behaviour that was already
     wrong.
2. **The public API baseline files are the evidence.** A diff to
   `PublicAPI.Shipped.txt` that removes or alters a line **is** a breaking
   change, and it is decided by reading the diff rather than by anyone's
   recollection. A version that claims minor while its shipped baseline lost a
   line is wrong, and the diff says so.
   - New surface is added to `PublicAPI.Unshipped.txt` and moves to
     `PublicAPI.Shipped.txt` at release. Until it moves it is not covered by any
     promise.
   - **The baselines are not the whole promise.** They cover shape, not
     behaviour. A parser that starts rejecting a document it used to accept has
     broken a consumer without touching a single line of baseline, which is why
     the conformance ratchet is the other half of the evidence: a case that
     stops passing is a behaviour change, and the ratchet fails on it.
3. **Prerelease tags until 1.0**, per 0029 — `v0.1.0-preview.1` first, `0.x` and
   prerelease until SPARQL conformance. Under SemVer, `0.x` makes no
   compatibility promise at all. **This repository makes one anyway**: the rules
   above are followed during `0.x`, and a breaking change moves the *minor*
   while the major is zero. The difference from 1.0 is that a consumer has no
   right to rely on it, not that it is not done.
4. **1.0 is a public API freeze**, and what else has to be true for it is listed
   in `docs/roadmap.md` under the 1.0 definition. It is not reached by the
   number looking ready.
5. **Every package versions together**, from the one tag. They are released as a
   set and share a version, so a consumer mixing `Varve.Rdf` and `Varve.Turtle`
   from the same release never has to reason about which combinations were
   tested. This costs version-number churn in packages that did not change, and
   that is the cheaper failure.

## Alternatives considered

- **Independent versions per package**, each moving only when its own surface
  changes. The stewardship standard's "individually releasable components" item
  points this way, and at layer boundaries it is defensible. Rejected for now:
  `Varve.Turtle` depends on `Varve.Rdf` depends on `Varve.Iri`, and independent
  versions mean a compatibility matrix that somebody has to test and document.
  One tag, one version, one tested set. Revisit when a package can plausibly
  ship on its own schedule — the SPARQL packages at milestone 5 are the first
  candidates.
- **Decide "breaking" by review, at release time.** The common practice.
  Rejected: it puts the judgement at the moment with the most pressure and the
  least attention, and it has no artefact behind it. The baselines already
  exist and already gate the build; using them costs nothing.
- **A tool that diffs assembly metadata between the built package and the last
  published one** — `Microsoft.DotNet.ApiCompat` or similar. Genuinely stronger
  than the baselines, because it sees what the compiler emitted rather than what
  someone wrote down. Not adopted now: it needs a published package to compare
  against and nothing is published, and under constraint 4 it would need its own
  ADR and register entry. **A candidate for the 1.0 freeze**, where the promise
  becomes binding and the evidence should be mechanical.
- **Stay on `0.x` indefinitely** and never make the 1.0 promise. Rejected: it is
  the honest choice only for a project that does not intend to be depended on,
  and a store people put data in must eventually say what it will not break.

## Consequences

- **`PublicAPI.Shipped.txt` becomes a contract at 1.0** and is nearly empty
  today — one line, `#nullable enable`, in each of the three packages. Nothing
  has shipped, so nothing is promised yet. The first release moves 356 lines
  from unshipped to shipped, and that is the moment the promise starts.
- **A release checklist item follows from this**: read the shipped-baseline diff
  before choosing the version number. It belongs in the release procedure at
  the first tag, not here.
- **The ratchet is load-bearing for versioning now**, not only for conformance.
  A behavioural break that the baselines cannot see is caught by a conformance
  case that stops passing, which makes `baseline/exemptions.txt` a versioning
  artefact too: an exemption added to make a suite green is a behaviour change
  being accepted, and it needs the same scrutiny as a baseline line being
  deleted.
- **No new dependency.** The mechanism is the analyzers already in the register
  under ADR 0004, and MinVer already there under 0029.
