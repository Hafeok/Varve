# 0098 — Blank nodes at the protocol boundary: stable labels out, fresh in, no skolem IRIs (Q1)

## Status

**Accepted — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan: "Q1 closed with stable labels out,
fresh in, no skolem, spec 1.6". Closes **Q1**'s protocol half, which ADR 0044
moved to milestone 7. Specification version 1.6 records the closure.
Acceptance is the maintainer's act on the pull request.

## Context

ADR 0044 settled the in-process half: an existing blank node is addressed by
its handle, an `RdfTerm` blank node in a request is always fresh, and
`TryExternalise` gives a blank id a label derived from the id. Across a
protocol there are no handles, only terms. Two answers are possible:

- **Skolem IRIs** (RDF 1.1 Concepts §3.5). Each store blank node goes out as an
  IRI under `/.well-known/genid/`, and an IRI in that space coming back is
  mapped to the node.
- **Labels.** A blank node goes out as a blank node with a stable label, and a
  label coming in is fresh, as SPARQL 1.1 Update §3.1.1 and the Graph Store
  Protocol already require.

## Decision

1. **Out: a blank node is a blank node, labelled from its store identity.**
   Query results, Graph Store responses, `CONSTRUCT` and `DESCRIBE`, the
   change feed and the diff all write a store blank node with the label
   `TryExternalise` gives it: `b` and its counter (ADR 0044).
   - The label is stable for the dataset's lifetime.
   - It is the same in every response, at every position and in every format
     that can carry a label.
   - Two nodes never share a label.

   A blank node the evaluator makes (a `CONSTRUCT` template's, `BNODE()`) gets
   a label of its own that never collides with a store label.
2. **In: a label is scoped to its request.** Every distinct label in an update
   request or a Graph Store body is one fresh node, whatever it looks like,
   including a label the server wrote. Labels never address existing nodes.
3. **No skolemisation.** Varve does not mint skolem IRIs and gives
   `/.well-known/genid/` no meaning.
4. **What a client can do with a label**: compare it within one dataset, and
   use it to replay the feed. A feed consumer maps labels to its own nodes, and
   the same label in a later record is the same node. This is what makes the
   feed's replay property hold (`change-feed.md` §6).

## Alternatives considered

- **Skolem IRIs, always.** A client could then address any node in a later
  update. But the node would be an IRI to every client: `isBlank` changes
  answer, RDFC-1.0 canonicalises a different dataset, and an export is no
  longer the dataset that was stored. In erasure mode (milestone 9) a
  shredded node's structure would become globally addressable, which the spec
  §9 names as a re-identification risk.
- **Skolem IRIs on request**, an opt-in parameter or media-type profile. It
  has the costs above only for clients that ask, and a round trip that only
  works through that parameter. Recorded as the extension to add if a client
  ever needs to address an existing blank node. Today none does, and SPARQL
  Update addresses one by pattern.
- **Request-scoped labels out**, renamed per response. Correct per the RDF
  semantics, and it would break feed replay: the same node in two records would
  look like two nodes.

## Consequences

- **Spec version 1.6** closes Q1. §11 records the decision. §1's blank-node
  bullet says what the external form is.
- A label written by the server and sent back creates a new node. The protocol
  documentation says so where a client will look: the service description's
  comment and the GSP page.
- Exports are the stored dataset: a canonicalisation of a Graph Store `GET`
  equals one of the as-of read in process.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0012** and **0044**: store-scoped identity, and the label form, now made
    stable at the boundary;
  - **0057**: fresh labels in updates;
  - **0019**: erasure's residue.

  No conflict.
- **Layer ownership.** The label is `Varve.Store`'s (4). Its use at the
  boundary is `Varve.Protocol`'s (5).
- **Analyzer rule.** None.
- **Open questions owned.** None. Q1 is closed.
