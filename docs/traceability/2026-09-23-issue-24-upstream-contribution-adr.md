# ADR 0038 — upstream contribution to Oxigraph

| | |
|---|---|
| **Issue** | [#24](https://github.com/Hafeok/Varve/issues/24) |
| **Date** | 2026-09-23 |
| **Tool** | Claude Code |
| **Model** | Claude Opus 5.5 |
| **Session identifier** | `session_01Vx5KYQT32BLuTBWfJwwyjq` |
| **Commits** | 1 |

## The prompt

> Add this ADR to the repo and report back

The attachment was a finished draft, `00XX-upstream-contribution-policy.md`,
with Status Accepted and dated 2026-09-23. It left two things to fill in: its
number, and the "conformance ADR that makes Oxigraph the tie-breaker".

## What was produced

`docs/adr/0038-upstream-contribution-policy.md`, a README row and an
obligation, and this record. The decisions (D1–D5) and the enforcement list are
unchanged in substance. Three adaptations were made, each to fit a rule that
already binds:

- **Number 0038.** 0037 was the last ADR in `docs/adr/`.
- **No ADR is amended.** The draft said it amends a conformance ADR holding the
  tie-breaker. None exists. The sentence is in `docs/brief.md`, *Definition of
  correct*, and ADR 0007 is the harness, with no tie-breaker in it. D1 decides a
  case the brief leaves open and contradicts none it decides, so the Status says
  **refines the brief** rather than supersedes or departs from it. AGENTS.md's
  "one departure is recorded" stays true. Had it been a departure, it would have
  needed ADR 0031's treatment.
- **ADR 0001's section shape.** "Options considered" became *Alternatives
  considered*. Enforcement moved under *Decision*, and the Status and relations
  moved from a front-matter list into the Status section.

One statement was added. **The differential harness does not exist**, so the
Enforcement section binds no code yet. AGENTS.md says a rule only in a document
is not a rule, so the ADR says this about itself. The four checks are recorded
in `docs/adr/README.md` as an obligation due with the harness, next to the open
question on `spec-gap` references.

## Not done

No harness code and no exemption-file format. Both belong to the milestone that
builds the differential runner.
