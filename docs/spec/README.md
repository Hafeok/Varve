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

  Version 1.3. Its §11 lists eight open questions and its §12 lists the ADRs it presupposes.
  [`docs/adr/README.md`](../adr/README.md) tracks both, with an owner and a due
  milestone for every question.

- **[`iri.md`](iri.md)** — RFC 3987 validation and RFC 3986 §5 resolution over
  UTF-8, with the §5.4 vectors as the test oracle, and an explicit list of the
  normalisations we refuse because RDF IRI equality is byte equality.
- **[`rdf-model.md`](rdf-model.md)** — terms, triples, quads and datasets per
  RDF 1.1 Concepts, with RDF 1.2's triple terms and directional
  language-tagged strings in the model from the start, and the W3C status of
  both 1.2 documents as checked on 2026-09-21.
- **[`turtle.md`](turtle.md)** — the Turtle and TriG grammars, prefix and base
  handling, blank node naming, the statement-as-recovery-unit rule, the
  chunk-boundary rule, and why a Turtle round trip is isomorphic rather than
  byte-identical while writing it twice is a fixed point. §9 lists the RDF 1.2
  constructs the reader rejects, and why both drafts being days old is the
  reason.
- **[`n-triples.md`](n-triples.md)** — the N-Triples and N-Quads grammars,
  canonical form, the line-as-recovery-unit rule, and why a column is counted
  in bytes. It also records where the RDF 1.1 N-Triples grammar contradicts its
  own test suite (`PN_CHARS_U` and the colon), which of the two we follow and
  why, and the two RDF 1.2 constructs the reader and writer accept beyond the
  1.1 grammar.

- **[`xsd.md`](xsd.md)** — the XSD 1.1 value spaces `Varve.Xsd` implements,
  the precision policy (`Int128` decimal with eighteen fractional digits,
  `Int64` integer with out-of-range literals kept as terms), the seven-property
  date and time model, durations, and the SPARQL operator mapping including
  the implicit-timezone total order for `dateTime`. Value spaces only: term
  equality stays lexical (`rdf-model.md` §2).
- **[`sparql-grammar.md`](sparql-grammar.md)** — the SPARQL 1.2 grammar by
  production with every 1.1 difference marked, the lexical rules (escapes
  processed during parsing, surrogates refused), the checks beyond the EBNF,
  the three version labels and what each refuses, byte positions, no
  recovery, and the suites that gate it — each parsed at its own version.
- **[`sparql-algebra.md`](sparql-algebra.md)** — the algebra tree the parser
  produces: node families, the §18.3 translation step by step and where the
  tree departs from it (blank nodes kept, no invented variables, aggregates
  left where they were written), the 1.2 reifier and annotation expansions,
  the update operations as a record of the request, the serialiser's
  round-trip contract, and the rewrite surface.

- **[`sparql-evaluation.md`](sparql-evaluation.md)** — what the evaluator does
  with the algebra: the entry point and the pinned-read contract, solutions as
  slots of handles with a local term table, every operator by §18.5, the
  datasets of §13, the function library by §17.4, the order of `ORDER BY`,
  the optimiser's rewrites and the property that keeps them honest, and
  ADR 0050's three arms.
- **[`sparql-results.md`](sparql-results.md)** — the four result formats'
  readers and writers: one pull reader over UTF-8 with term views, a
  non-validating XML subset reader, positions on error; one push writer to
  a buffer writer or a stream, nothing per row, and what each format cannot
  carry.
- **[`sparql-update-store.md`](sparql-update-store.md)** — SPARQL Update over
  the store: one request pinned, each operation evaluated over the overlay of
  the ones before it through a staging view, one commit with the pinned
  position expected; graph management in a store that records no empty
  graphs; `LOAD` through a contract.
- **[`rdf-canon.md`](rdf-canon.md)** — RDFC-1.0 over any quad source: the
  hash algorithms, the work limit that bounds it, triple terms, and the
  canonical N-Quads form of its Appendix A, which differs from
  `n-triples.md`'s.

Cite specification sections when you write one. Where a W3C specification is
silent, Oxigraph's behaviour is the tie-breaker; say so explicitly in the text
rather than matching it silently.
