# Specifications

One functional specification per component, with formal grounding where it
exists: the SPARQL algebra semantics for the evaluator, and the log and
projection model as a state machine with stated invariants.

A specification here describes behaviour and the obligations a component takes
on. It is not an API listing and not a design document — decisions live in
`docs/adr/`, and the API is its own record in the `PublicAPI.*.txt` baselines.

The order is specification, then ADR, then code. This directory is empty because
milestone 1 delivers no components. The first entries will be, in dependency
order:

- `iri.md` — RFC 3987 parsing and resolution (milestone 3).
- `rdf-model.md` — terms, triples, quads, datasets, RDF 1.2 triple terms
  (milestone 3).
- `log-and-projections.md` — the commit model, projection contract and the
  invariants that make a rebuilt projection equal an incrementally maintained
  one (milestone 2 as ADRs, milestone 4 as a specification).

Cite specification sections when you write one. Where a W3C specification is
silent, Oxigraph's behaviour is the tie-breaker; say so explicitly in the text
rather than matching it silently.
