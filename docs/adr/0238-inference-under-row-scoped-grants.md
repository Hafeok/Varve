# 0238 — Inference under row-scoped grants

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the plan in #14 (2026-10-30), decision 5, **for 1.0,
pending the Inference session's agreement on #171**
(https://github.com/Hafeok/Varve/issues/171#issuecomment-6520445643). Row-local rules served from the
closure are #205. **Amends ADR [0190](0190-entailments-under-graph-level-grants.md)**
by a dated amendment in that file.

Records decision set
[`inference-under-row-scoped-grants`](../decisions/inference-under-row-scoped-grants.md).

## Context

ADR 0190 point 1 stands: an entailment is visible only if the caller can
derive it from what it reads. With rows, a conclusion's premises are rows.
Take `alice ex:knows bob` and `ex:knows rdfs:range ex:Person`:
- they give `bob a ex:Person`, in bob's row;
- a caller who reads alice's row and not bob's can derive it, but would not
  see it by row;
- a caller who reads bob's row and not alice's would see it, but cannot
  derive it.

So ADR 0190's placement by graph cannot carry row premises. Placement by
premise set would need a graph per set of subjects, and their number is
bounded only by the data.

## Decision

Under a readable subject set that is not `all` for a classifier binding B,
over the data graphs of an inference binding that meet Γ_B:

1. **Tier 1 (rewriting) is served, and is exact.** It evaluates over the
   caller's `RowScopedQuadSource`, so it derives exactly what the caller
   can. A binding's TBox and rules graphs stay dataset-public (ADR 0190
   point 3), and a classifier binding naming one of them in Γ_B is refused
   at apply.
2. **Tier 2 (the materialised closure) is refused, never filtered.** That
   covers:
   - the views `varve:implicit` and `varve:explicit`;
   - the reserved graphs `varve:inferred/…`;
   - `Varve-Entailment: owl2-rl` when the closure serves it;
   - explain.

   The answer is `403 entailment-not-available`, with the binding as a
   member. A regime tier 1 serves is still served to the same caller.
   Filtering by about-set would leak or vanish, as above. Filtering by
   premise would need provenance per quad, which ADR 0190 refused.
3. **Cross-graph inference bindings** (ADR 0190 point 6) whose data graphs
   meet a Γ_B are refused at apply while a read grant with subject terms
   covers them, as `declaration-invalid` naming the binding and the grant.
4. **Tier 3** is refused by ADR 0234's P5.
5. **Callers whose subject set is `all`** are unaffected.

**Where this is built.** The check of point 2 is a guard, beside where the
closure's surfaces are served. #14 adds it when #171's surfaces exist on
`main`. Until then, this ADR has nothing to guard, and its properties run
over tier 1 alone. The property extending ADR 0190 point 8 gains a
row-restricted caller whose expected tier-2 answer is the refusal. Its
tier-1 answers equal a from-scratch closure over the quads it sees.

## Alternatives considered

- **A closure per caller, on request**, over the quads it sees, under a work
  limit. It is correct, at the cost of a closure per distinct subject set.
  Stated as a later option, not built.
- **Serve row-local conclusions** (`cax-sco`, `prp-dom`), whose premises and
  conclusion share the subject node. That is #205.

## Consequences

- An operator who binds inference over classified graphs serves its closure
  only to unrestricted callers. `docs/operator/row-level-access.md` and
  `docs/operator/inference.md` say so.
- **`docs/problems/entailment-not-available.md`** is added, and
  `ProblemCatalogue` gains the type.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0190**: amended;
  - **0119**: a new type.

  No conflict.
- **Layer ownership.** `Varve.Management` (4): the refusals at apply.
  `Varve.Protocol` (5): the guard.
- **Analyzer rule.** None.
- **Open questions owned.** #205.
