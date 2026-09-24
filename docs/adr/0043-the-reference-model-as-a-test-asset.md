# 0043 — The reference model is a test asset, and it is naive on purpose

## Status

**Accepted.** 2026-09-23.

Decides what the specification's §10 properties are checked *against*.

## Context

Most of §10 compares the store with itself: as-of by overlay against as-of by
replay, a rebuilt projection against an incremental one, a checkpoint plus a tail
against a full fold. Those catch the store disagreeing with itself. They do not
catch the store agreeing with itself and being wrong — a normalisation that drops
the same quad on both paths passes every one of them.

What catches that is a second, independent statement of the state machine in §3
and §5: `D_P = D_{P-1} ∪ alloc_P`, `G_P = (G_{P-1} \ R_P) ∪ A_P`, T1's seven
steps. Written simply enough that it is obviously right, and run over the same
generated requests.

## Decision

**`Varve.Store.Tests` holds a reference model**: a fold over the same request
sequence, written from the specification and nothing else.

- **It works over terms, not ids.** Quads are tuples of `RdfTerm`; the graph is a
  `HashSet`; positions are a list; the dictionary is a set of terms. Blank nodes
  are identified by `(request, label)`, which is what request scoping means, and
  the store's blank ids are related to them by a bijection the test builds as
  commits land and then holds fixed.
- **It shares no code with the store.** No reference to an internal type, no
  helper that both use. A shared helper is a place where one bug satisfies both
  sides.
- **It is deliberately slow.** Replay from zero for every as-of, linear scans,
  materialised sets. It is correct by inspection or it is useless.
- **Generators are part of the asset and are reviewed like it.** They produce
  request sequences with asserts, retracts, redundant operations, assert-then-
  retract of an absent quad, blank labels reused *across* requests, existing
  blank nodes addressed by handle, expected positions both right and stale,
  clocks that step backwards, and validators that accept, reject and attach. A
  generator that quietly never produces one of these makes a property vacuous
  (ADR 0025), so each is counted and the run fails if any case never occurred.
- **Validators are stated twice**: once over the store's `IQuadSource` and
  `QuadDelta`, once over the model's term sets. Their verdicts must agree before
  anything else is compared.

**The model-based property ties §10 together.** For each generated sequence it
checks, after every request: the outcome kind and position; `G_P` quad for quad
after externalisation; the dictionary's growth; and, over the finished run, as-of
at every position, `Diff` between sampled positions, and the settings fold.

## Alternatives considered

- **Differential testing against another store** — Oxigraph, or dotNetRDF's
  in-memory store. An oracle for free. Rejected as the primary check: neither
  implements effective deltas, request-scoped blank nodes or expected positions,
  so most of what is being tested has no counterpart. It stays welcome for query
  behaviour (the brief), which is not this milestone.
- **The model as a mode of the store** — the store in "naive mode". Rejected:
  it shares everything with the thing it checks.
- **Example-based tests only.** Readable, and they document intent. Kept for
  that, and rejected as the definition of done: §10 is stated as properties
  because the interesting failures are interleavings nobody writes by hand.

## Consequences

- **Two statements of T1 must agree**, and when they disagree the specification
  decides which is wrong. A disagreement whose answer the specification does not
  give is a finding about the specification, reported rather than resolved in
  the test.
- **Shrunk counterexamples are kept.** Each one found during development becomes
  a named regression case beside the property, and the milestone report lists
  them.
- **The model costs a second implementation of §3 and §5** — a few hundred lines
  that change only when the specification does.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0042).
  Touches **0025** (CsCheck; generators are reviewed as the test), **0010**,
  **0011**, **0012** (the behaviours the model restates), and **0044** (blank
  node identity as the model sees it). No conflict with any.
- **Layer ownership.** None — test code, `VarveLayer=none`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
