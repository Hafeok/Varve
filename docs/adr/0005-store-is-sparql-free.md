# 0005 — `Varve.Store` is SPARQL-free

## Status

Accepted. 2026-09-20. `docs/brief.md` decides this; it is recorded here because
the layer table in ADR 0003 depends on it.

## Context

The obvious shape for a store package is the one most RDF stores ship: a store
that accepts a SPARQL query string and returns results. It is the API users
expect, and it is the API Oxigraph's `Store` presents.

It also puts the query engine inside the storage layer permanently. Every user
of the log — someone replicating it, someone feeding a full-text projection,
someone who wants change feeds and nothing else — links the parser, the algebra,
the optimiser and the evaluator, and pays for them in binary size, trimming
surface and AOT compilation time. In a project where one of three hosts is a
browser, that is not a rounding error.

There is a second cost, less obvious and more damaging. If the store can
evaluate SPARQL, SPARQL Update becomes a store concern, and then the store has
to answer "evaluated against which position" inside its own transaction
machinery. The question is real either way, but it is much easier to answer in a
package whose whole job is to answer it.

## Decision

`Varve.Store` sits at layer 4 and does not reference the SPARQL packages at
layer 3. It exposes two contracts and no query language:

- the **quad source contract** for reads, pinned to a log position;
- the **transaction contract** for writes, taking an ordered set of assertions
  and retractions plus metadata, and producing one commit.

SPARQL Update lives in a layer 5 integration package — `Varve.Sparql.Store` or
similar. It evaluates the `WHERE` clause against a pinned position through the
quad source contract, and submits the resulting delta as one commit through the
transaction contract. One update request maps to one atomic commit, and the
position it read is explicit rather than implied.

This is the same pattern as the SHACL store integration, which is the point: the
store defines contracts, and the compositions live above it. Hosts reference the
integration packages. A library user who wants the log and its projections does
not pay for a query engine.

Under ADR 0003, layer 5 is the only layer permitted to know about both a store
and an evaluator, and this is the canonical case.

## Alternatives considered

- **A store that evaluates SPARQL directly.** What users expect, and one fewer
  package. Rejected on binary size and AOT surface for the embedded and WASM
  hosts, and because it puts the hardest question in SPARQL Update semantics
  inside the transaction machinery instead of beside it.
- **A store that takes a pluggable query engine**, through an interface the
  store defines and the evaluator implements. Superficially attractive; it
  inverts the dependency without adding a package. Rejected: the interface would
  have to be expressed in SPARQL algebra terms, so layer 4 would own a type
  vocabulary from layer 2, and the dependency would be real even though the
  reference points the right way. ADR 0003's rule about callbacks typed to
  higher-layer concepts is aimed at exactly this move.
- **Putting the quad source contract in `Varve.Store`** rather than `Varve.Rdf`.
  Rejected: the evaluator at layer 3 needs it, and a layer 3 package cannot
  reference layer 4. The contract belongs in the lowest layer that can define it
  without knowing its implementers, which is layer 1.

## Consequences

`Varve.Store` alone cannot answer a SPARQL query. Documentation and the CLI must
be clear that the integration package is the normal thing to install; the split
is an architectural fact, not a suggestion that most users want half of it.

The evaluator becomes usable over anything implementing the quad source
contract — an in-memory dataset, a projection, a remote endpoint adapter — which
is what makes the brief's differential testing against Oxigraph and the
property test for projection rebuild equivalence straightforward to write.

The pre-commit validator contract is a store concern and stays at layer 4, even
though no validator exists until milestone 8. ADR set zero specifies the hook.
The validator that eventually uses it is a layer 5 composition for the same
reason SPARQL Update is.

A consequence to watch: pushing evaluation above the store means the evaluator
cannot see the store's physical index ordering, so optimisations that depend on
it have to be expressed through the quad source contract rather than by reaching
past it. If that contract turns out to be too narrow to carry the statistics an
optimiser needs, the answer is to widen the contract deliberately, in a
superseding ADR — not to move the evaluator down a layer.
