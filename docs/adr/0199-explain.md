# 0199 — Explain: a well-founded proof of an entailment

## Status

**Filed unaccepted by Inference, part 2 (#171, under #95), 2026-11-06**
(ADR 0066). Decided by the maintainer on the plan in #95 (2026-10-27),
question 4d. Acceptance is the maintainer's act on the pull request.

## Context

A reader shown an implicit quad needs to know why it holds. Nothing per
entailment is stored beyond its graph (ADR 0190), so the answer is computed
on demand. A one-step answer, "this rule instance holds", can be circular
through a recursive rule (`cax-sco` over a cycle of `rdfs:subClassOf`), so it
does not explain.

## Decision

### 1. The route

`GET /datasets/{name}/explain?quad=<one N-Quads line, percent-encoded>`,
honouring `Varve-As-Of`, answering `application/json`.
`SparqlHttpClient.ExplainAsync` calls it.

### 2. The answer is a well-founded proof

```json
{
  "quad": "<…> <…> <…> <…> .",
  "position": 42,
  "status": "implicit",
  "proof": {
    "rule": "https://w3id.org/varve/ns#rule/owl2rl/cax-sco",
    "premises": [
      { "quad": "…", "status": "explicit", "graph": "…" },
      { "quad": "…", "status": "implicit", "proof": { … } }
    ]
  }
}
```

- `status` is `explicit` (asserted; no proof), `implicit` (with a proof), or
  `absent` (not in the closure at the position; `404`-free, a `200` that says
  so).
- **Well-founded:** no quad's proof uses that quad, so the tree is finite and
  each node is proved from below.
- **A TBox premise** is `explicit` with its TBox graph.

### 3. How it is found

The backward search B/F uses (ADR 0196): the rules are pre-bound with the
head, then each premise is proved without using the target quad, nor any quad
on the path to it. It runs under the read's work limit, answering
`read-limit-exceeded` beyond it.

### 4. Grants

Explain answers only for a quad the caller sees by ADR 0190's rule. Every
premise is then in a graph the caller reads, or in a TBox or rules graph, so
the proof shows nothing the caller could not read. A quad the caller does not
see is `absent`, as if it were not entailed.

### 5. Through the tier-2 seam

Explain resolves the closure through ADR 0195 point 7's seam, so it waits or
answers `503 inference-behind` like any tier-2 read, and is refused where tier
2 is refused (ADR 0195 point 8; #14).

## Alternatives considered

- **A SPARQL function.** Returns one term, not a tree.
- **A reserved graph per quad** holding its proof. Stores per entailment what
  ADR 0190 rules out, and must be maintained.
- **Store the derivations** at materialisation. The same cost, on every
  commit, for a question rarely asked.

## Consequences

- An explain costs a backward search bounded by the work limit.
- The data explorer (#96, part 2) reads it.

## Checks

- **Checked against the accepted ADRs** (0001–0194, 0206, 0213–0217, 0224,
  0225). Touches **0107**/**0190** (grants), **0114** (the work limit),
  **0195**, **0196**. No conflict.
- **Layer ownership.** `Varve.Inference` (4): the proof search.
  `Varve.Protocol` (5): the route.
- **Analyzer rule.** None new.
- **Open questions owned.** None.
