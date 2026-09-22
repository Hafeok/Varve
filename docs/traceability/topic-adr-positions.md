# Topic — the ADR positions and the repository's working method

> **The transcript is held by the maintainer**, who attaches it to this file.
> What follows names the decisions the conversation produced. See
> [ADR 0033](../adr/0033-commit-traceability.md) for why a summary is filed
> rather than nothing, and why it is not presented as a record.

| | |
|---|---|
| **Issues** | [#4](https://github.com/Hafeok/Varve/issues/4), [#17](https://github.com/Hafeok/Varve/issues/17), [#19](https://github.com/Hafeok/Varve/issues/19), [#20](https://github.com/Hafeok/Varve/issues/20) |
| **Where** | Outside this repository |
| **Tool** | Claude Code and the Claude apps |
| **Model** | Claude Opus 5 |
| **Produced** | ADRs 0001–0009, 0024–0027, 0030, and the working method |

## What was being decided

Not the architecture of the store — that is the other topic — but the rules the
project would run under, and the positions that make those rules follow from
something rather than from taste.

## Decisions it produced

**The method itself**, [0001](../adr/0001-record-architecture-decisions.md).
ADRs, numbered, never reused, with a mandatory *Alternatives considered*
section and a no-edit rule: a decision that changed gets a superseding ADR and
the old one keeps its text. Everything else in this repository leans on that
rule, including the four stewardship ADRs of 2026-09-22, which exist in that
form *because* editing 0002 was not an option.

**Enforcement**, [0004](../adr/0004-enforcement-by-analyzers.md). The position
that a rule which exists only in a document is not a rule. It is why
`VARVE0001` and `VARVE0002` were written before there was any code for them to
constrain, why five gates now live in `eng/`, and why "prove a gate's failure
path by running it" is a standing rule rather than good practice.

**Layering**, [0003](../adr/0003-package-layering.md) and
[0005](../adr/0005-store-is-sparql-free.md). Fixed layers, references strictly
downward, same-layer references a violation, and no `Common` / `Core` / `Utils`
package — the position being that such a package is where coupling goes to
hide. Two open questions were recorded and deliberately not resolved.

**Dependencies**, [0006](../adr/0006-build-and-test-dependencies.md),
superseded within the same milestone by
[0009](../adr/0009-dependency-policy-and-register.md). The supersession is the
interesting part: 0006 enumerated the admitted packages, and the realisation
that *an ADR which enumerates will be amended forever* — becoming a stale copy
of `Directory.Packages.props` — is what produced the register-plus-gate
arrangement instead.

**Conformance**, [0007](../adr/0007-w3c-conformance-harness.md). The W3C suites
as the acceptance gate rather than as a badge, with an explicit exit criterion
for the temporary dependency on another RDF implementation. That criterion was
met two milestones early.

**Licence**, [0002](../adr/0002-licence.md) — Apache-2.0, decided on the patent
grant. Superseded on 2026-09-22 by
[0031](../adr/0031-licence-mpl-2-0.md) when the constraint it was given changed,
not because the reasoning was wrong.

Milestone 3a added [0024](../adr/0024-rdf-term-representation.md) (term
representation), [0025](../adr/0025-property-based-testing.md) (CsCheck, chosen
over FsCheck on its dependency tree), [0026](../adr/0026-hotpath-attribute.md)
and [0027](../adr/0027-benchmarking.md). Milestone 3b added
[0030](../adr/0030-turtle-recovery-and-prefixes.md).

## The positions worth stating, because the ADRs assume them

- **An ADR that lists no losing option has recorded an outcome, not a
  decision.** This is why *Alternatives considered* is mandatory and why a
  rejection always carries a reason.
- **Accepted ahead of the evidence is allowed, if the ADR says what would
  change its mind.** Six ADRs carry a revisit condition. One has fired.
- **A constraint that is narrowed must then be enforced in its narrowed form**,
  or the narrowing makes an unenforced version plausible. ADR 0009's amendments
  record this happening.
- **The specification is the authority for behaviour; the ADR is the authority
  for why.** They are different artefacts and neither replaces the other.

## What this summary cannot tell you

The arguments that did not survive into an ADR, and the sequence in which the
positions were reached. Where an option was considered and rejected, the ADRs
say so; where one was never raised, nothing here can show it.
