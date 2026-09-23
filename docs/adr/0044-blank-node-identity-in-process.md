# 0044 — Blank node identity at the in-process boundary (Q1, split)

## Status

**Accepted.** 2026-09-23.

Answers the in-process half of the specification's **Q1** — the external form
of store-scoped blank node identity — which ADR 0012 owns and made due by
milestone 4. The protocol half is **moved to milestone 7**, with the server, by
the maintainer's decision recorded here.

## Context

Blank node identity is settled in shape (ADR 0012, spec T1 step 2): a blank id is
its own identity, allocated by the sequencer; labels in a request are scoped to
the request, so each distinct label is a fresh node; and "an existing blank node
is addressed by its store identity". Q1 asks what that identity looks like to
someone outside the store.

There are two outsides, and they need different answers.

- **In process**, a caller that read the dataset holds handles. A handle from
  this store *is* the store's `TermId` (ADR 0022), and an id is never reused, so
  it names the same node at every position.
- **Across a protocol** — SPARQL results, a Graph Store request, a serialised
  change feed — there are no handles, only terms. That needs a term that stands
  for a store blank node: a skolem IRI (RDF 1.1 Concepts §3.5) is the candidate.

The protocol answer depends on things that do not exist yet: the server's base
IRI, whether a dataset has a stable identity, and how SPARQL Update in layer 5
maps a skolem IRI back. Deciding it now would decide those in passing.

## Decision

### In process: by handle

- **A request addresses an existing blank node with
  `RequestTerm.Existing(handle)`**, where the handle came from any read of the
  same dataset. The sequencer checks that the id is in `D_head`; an unknown
  handle fails the request with an exception and leaves no trace, because it is
  a caller error rather than an outcome.
- **An `RdfTerm` blank node in a request is always fresh.** Each distinct label
  in one request is one new node; the same label in a later request is another.
  This holds even for a label that `TryExternalise` produced, which is the case
  that surprises people and is why the handle form exists.
- **`TryExternalise` of a blank id returns a blank node whose label is derived
  from the id**, so two externalisations of one node agree and two nodes never
  collide within a dataset. The label means nothing across datasets and is not
  an identity to send back.
- `Existing` accepts any class of handle, not only blank ones: addressing a
  canonical term by handle is a shortcut, not a different meaning.

A blank node nested inside a triple term is part of that term. It can be
addressed by addressing the triple term's handle; a request cannot build a new
triple term around an existing blank node. Stated as a limit, not solved.

### Across protocols: at milestone 7

**The skolem IRI scheme is decided at milestone 7, with the server**, and Q1 is
narrowed to that half in the specification's §11. The roadmap carries it there.

## Alternatives considered

- **Decide the skolem scheme now** — `/.well-known/genid/` under a configured
  authority, or a reserved URN. The obvious completion of Q1. Rejected for now:
  §3.5's `.well-known` form needs an authority the store does not have, a URN
  needs a dataset identity the store does not have either, and both choices
  would be made without the server that is their only consumer.
- **Treat an externalised label as identity** — resubmitting `_:b17` addresses
  node 17. No new API. Rejected: it makes request scoping depend on the label's
  spelling, so a caller's own `_:b17` would silently join an existing node.
- **Skolemise always**, so the store never externalises a blank node at all.
  Rejected: it would change what a dataset serialises to, and would do so for
  every consumer to serve the minority that round-trips blank nodes.

## Consequences

- **Q1 is half closed.** The in-process form is decided and tested; the
  protocol form has an owner and a due milestone.
- **Round-tripping a blank node through terms creates a new node.** That is the
  specification's request scoping, and the handle form is the way around it.
- **The model test (ADR 0043) exercises it**: generators reuse labels across
  requests and address existing nodes by handle.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0043).
  Touches **0012** (owner of Q1; blank ids are counter-allocated and never
  reused), **0022** (a handle from a store is its id), and **0005** (SPARQL
  Update in layer 5 will be the first consumer of the protocol form). No
  conflict with any.
- **Layer ownership.** `RequestTerm` is `Varve.Store`, **layer 4**. The skolem
  scheme, when decided, is a layer 5 concern that the store may need to
  recognise.
- **Analyzer rule.** None.
- **Open questions owned.** Q1's protocol half, due milestone 7.
