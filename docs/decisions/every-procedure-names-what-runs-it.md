---
set: every-procedure-names-what-runs-it
namespace: varve
adr: 0248
decisions:
  - key: ProcedureIsAFencedBlockOfTheGuide
    statement: "A procedure of the operator's guide is a fenced code block in docs/operator and docs/auth-providers.md"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-19T00:00:00Z
  - key: EveryProcedureCarriesAMarker
    statement: "The line before each procedure is a marker naming the jobs that run it, each a job id of a workflow under .github/workflows or a job of eng/ci.cs, or none with the reason no job can, or example with what a block that is no procedure shows"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-19T00:00:00Z
  - key: UnrunProceduresAreListedOnTheirPage
    statement: "A page with a procedure no job runs has a section headed Not run in CI listing each with its reason"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-19T00:00:00Z
  - key: MarkerGateInEngCi
    statement: "eng/procedures.cs, the procedures job of eng/ci.cs, fails a block with no marker, a marker naming a workflow or job that does not exist, a none or example without its words, and a page with a none and no Not run in CI section, and its failure path is proven on fixtures"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-19T00:00:00Z
  - key: ExemptPagesNamedWithTheirPullRequest
    statement: "A page whose markers belong to another open pull request is exempt by name in eng/procedures-exemptions.txt with that pull request, and an exemption for a page with no unmarked block, or for a page the guide does not have, fails"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-19T00:00:00Z
---

The rulings of [ADR 0248](../adr/0248-every-procedure-names-what-runs-it.md),
filed unaccepted by #129 (ADR 0066) and accepted by the maintainer on
2026-11-19, written by his delegate (ADR 0239).
