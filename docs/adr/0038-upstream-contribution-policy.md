# 0038 — Upstream contribution to Oxigraph and the shared test suites

## Status

Accepted. 2026-09-23.

**Refines `docs/brief.md`, *Definition of correct*.** It does not depart from it.
The brief's tie-breaker ("where the specs are silent, Oxigraph's behaviour is the
tie-breaker") covers the case where the specification is silent. D1 decides the
case the brief leaves open. No ADR holds the tie-breaker, so no ADR is superseded.
ADR 0007 governs the W3C harness and its ratchet, and D5 extends that ratchet to
the differential suite without changing 0007.

Related: [0031](0031-licence-mpl-2-0.md) (MPL-2.0, the Mind Over Machine
stewardship standard), [0033](0033-commit-traceability.md) (issues and
traceability), and constraint 6.

## Context

Varve implements Oxigraph's scope independently, and the brief welcomes
differential testing against Oxigraph. Two independent implementations of the
same specifications will disagree. Each disagreement has one of a few causes:
some point at defects in Oxigraph, some at gaps in the specifications or the W3C
test suites, and some at defects in Varve. The first two are useful to people
outside Varve. Today nothing ensures they leave the repository.

Three facts shape how they can leave:

1. The brief's tie-breaker does not say what happens when the spec is not silent
   and Oxigraph deviates from it. Read loosely, differential testing then trains
   Varve to reproduce Oxigraph's defects and hides them from both projects.
2. Varve is MPL-2.0 (ADR 0031). Oxigraph is MIT OR Apache-2.0. Only its copyright
   holders can move MPL-2.0 code into Oxigraph under Oxigraph's licence. The DCO
   certifies origin. It grants no relicensing.
3. Constraint 6 (no code copied from Oxigraph) covers only the direction
   Oxigraph → Varve. It says nothing about Varve → Oxigraph.

## Decision

### D1 `spec-over-oxigraph`: the specification wins over Oxigraph

Oxigraph is the tie-breaker only where the governing specification is silent or
ambiguous. Where the specification decides a case and Oxigraph deviates, Varve
follows the specification and D3 applies.

### D2 `differential-triage`: every disagreement is triaged into exactly one category

| Category | Meaning | Varve action | Required reference |
|---|---|---|---|
| `varve-defect` | Varve deviates from the spec, or from Oxigraph where the spec is silent | Fix. Never exemptable. | Varve issue |
| `upstream-defect` | Oxigraph deviates from a spec that decides the case | Follow the spec; report upstream (D3) | Spec section and Oxigraph issue URL |
| `spec-gap` | The spec is silent or ambiguous | Follow Oxigraph; report the gap (D3) | Spec section, and the test-suite or working-group issue URL once filed |
| `intentional-divergence` | Varve differs by design (a feature Oxigraph does not have, or a decision in a Varve ADR) | Keep | ADR and decision key |

A disagreement in the differential run is recorded as an exemption entry that
carries its category and required references. `varve-defect` has no exemption
form: it is a failing test.

### D3 `upstream-destination`: findings go where they get fixed for everyone

- `upstream-defect` goes to the Oxigraph issue tracker, one defect per issue.
  The report carries a minimal reproduction, the spec section, the expected and
  actual results, and the Oxigraph version tested. Search existing issues first.
- `spec-gap` goes to the W3C test suites as a proposed test when the intended
  behaviour is clear. When it is not, it goes to the relevant working group's
  issue tracker. A test in the shared suite settles the case for Oxigraph,
  Varve and every other implementation at once.
- Design findings (as-of semantics, change feeds, the mapping from SPARQL Update
  to a single commit) go to Oxigraph as Discussions that link the relevant Varve
  ADRs, never as PRs proposing Varve's architecture.

Each upstream report has a Varve issue that links it, per ADR 0033. The person
filing the report reviews and owns it. A reproduction produced with AI assistance
is fine; an unreviewed one is not sent.

### D4 `upstream-licensing`: what may cross into Oxigraph, and by whom

- Bug reports, spec references and minimal reproductions are written for the
  report. They are not extracted from Varve source files.
- Tests proposed to the W3C suites are contributed by their author under that
  suite's contribution terms.
- Code or algorithms from Varve's MPL-2.0 tree may go to Oxigraph only from
  their copyright holders, contributing directly under Oxigraph's terms. Code
  with other contributors needs each contributor's explicit consent. No one
  translates another author's MPL-2.0 file into Rust for Oxigraph.
- Tooling built for use on both sides, such as the differential runner and the
  Oxigraph side of the benchmark harness (ADR 0027), stays in the Varve
  repository under MPL-2.0, like everything else. The licence has no exception.
  MPL-2.0 lets anyone, including Oxigraph's maintainers, run the tooling and
  combine it with code under other licences, without conditions. The only
  obligation is that changes to the MPL-2.0 files themselves are shared under
  MPL-2.0. The tooling is offered as a published tool that runs against an
  Oxigraph build, not as code to vendor into Oxigraph.

### D5 `stale-exemption`: exemptions expire when the disagreement does

The differential harness fails when an exempted disagreement no longer
reproduces, for example after an Oxigraph release fixes an `upstream-defect`.
The entry is then removed and the linked Varve issue closed. This extends ADR
0007's conformance ratchet to the differential suite.

### Enforcement

Exemption entries are data, not source. The differential harness enforces this
decision, not an analyzer (ADR 0004 does not apply). The harness fails the build
when:

- an entry has no category, or a category outside the four in D2;
- an entry lacks the references its category requires;
- an entry has category `varve-defect`;
- an exempted disagreement no longer reproduces (D5).

The harness has its own tests: one violating and one conforming entry per check.

**The differential harness does not exist yet.** Until it does, this decision
binds no code. Its enforcement is recorded as an obligation in `README.md`, due
with the harness. The differential exemption list is a file separate from the
W3C list in `tests/Varve.Conformance.Tests/baseline/exemptions.txt`, whose
format this ADR does not change.

## Alternatives considered

- **No policy, ad hoc reports.** Whether a finding leaves the repository depends
  on who notices it, and most would stay in local notes. Rejected.
- **Bug-compatibility with Oxigraph.** Treat Oxigraph as the reference
  everywhere, not only where the spec is silent. The differential suite would
  stay green and find nothing. Varve would inherit Oxigraph's defects, and the
  W3C suites would decide less than they should. Rejected.
- **Triage with required references (this ADR).** Every disagreement ends as
  either a Varve fix or a link to an upstream issue, a spec-level issue or an ADR.
  It costs one structured record per disagreement.

## Consequences

- The differential suite becomes a source of upstream reports, not only a Varve
  regression guard.
- On cases the spec decides, the spec defines Varve's behaviour, even when that
  makes Varve disagree with Oxigraph. Users comparing the two will sometimes see
  differences. The exemption list documents each one with its reason.
- Contributing code to Oxigraph is possible but personal. It needs the author to
  act, and shared code needs every contributor's consent. Findings, tests and
  reports carry no such cost, which is why D3 prefers them.
- The obligation to build the four checks and their tests lands with the
  differential harness.

## Open questions

1. Whether a `spec-gap` entry should cite a W3C test-suite PR (stronger) or
   whether a filed issue is enough (cheaper). Owner: this ADR; due with the
   differential harness.
