# Specifications

One functional specification per component, with formal grounding where it
exists: the SPARQL algebra semantics for the evaluator, and the log and
projection model as a state machine with stated invariants.

A specification here describes behaviour and the obligations a component takes
on. It is not an API listing and not a design document — decisions live in
`docs/adr/`, and the API is its own record in the `PublicAPI.*.txt` baselines.

The order is specification, then ADR, then code.

## Current

- **[`log-and-projection-model.md`](log-and-projection-model.md)** — the abstract
  state machine of `Varve.Store`: domains, the dataset directory's environment
  assumptions, log invariants, transitions, reads, projections, subscriptions,
  and erasure by crypto-shredding. **It is the authority for `Varve.Store`
  behaviour**, and where it refines or departs from `docs/brief.md` — most
  visibly in separating a pinned read from an as-of read, which the brief merges
  — the specification takes precedence and the relevant ADR says so.

  Its §11 lists eight open questions and its §12 lists the ADRs it presupposes.
  [`docs/adr/README.md`](../adr/README.md) tracks both, with an owner and a due
  milestone for every question.

## Planned

In dependency order:

- `iri.md` — RFC 3987 parsing and resolution (milestone 3).
- `rdf-model.md` — terms, triples, quads, datasets, RDF 1.2 triple terms
  (milestone 3).
- `sparql-evaluation.md` — the algebra semantics the evaluator implements
  (milestone 5).

Cite specification sections when you write one. Where a W3C specification is
silent, Oxigraph's behaviour is the tie-breaker; say so explicitly in the text
rather than matching it silently.
