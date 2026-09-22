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

- **[`iri.md`](iri.md)** — RFC 3987 validation and RFC 3986 §5 resolution over
  UTF-8, with the §5.4 vectors as the test oracle, and an explicit list of the
  normalisations we refuse because RDF IRI equality is byte equality.
- **[`rdf-model.md`](rdf-model.md)** — terms, triples, quads and datasets per
  RDF 1.1 Concepts, with RDF 1.2's triple terms and directional
  language-tagged strings in the model from the start, and the W3C status of
  both 1.2 documents as checked on 2026-09-21.
- **[`n-triples.md`](n-triples.md)** — the N-Triples and N-Quads grammars,
  canonical form, the line-as-recovery-unit rule, and why a column is counted
  in bytes. It also records where the RDF 1.1 N-Triples grammar contradicts its
  own test suite (`PN_CHARS_U` and the colon), which of the two we follow and
  why, and the two RDF 1.2 constructs the reader and writer accept beyond the
  1.1 grammar.

## Planned

In dependency order:

- `turtle.md` — the Turtle and TriG grammars (milestone 3b).
- `xsd.md` — value spaces and SPARQL operator semantics. Not the term model's:
  term equality is lexical and stays that way (see `rdf-model.md` §2). Lands
  just before the evaluator, which is the first thing with a suite that gates
  it.
- `sparql-evaluation.md` — the algebra semantics the evaluator implements.

Cite specification sections when you write one. Where a W3C specification is
silent, Oxigraph's behaviour is the tie-breaker; say so explicitly in the text
rather than matching it silently.
