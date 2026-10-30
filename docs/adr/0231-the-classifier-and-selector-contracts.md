# 0231 — The classifier and selector contracts, without erasure mode

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
on the plan in #14 (2026-10-30), questions 2 and 3, and decision 6. **Amends
ADR [0023](0023-erasure-and-access-requests.md)** by a dated amendment in
that file. The classifier's assignment is ADR 0230's. The selector is
implemented by classification. Both contracts are usable with erasure mode
off.

Records decision set
[`the-classifier-and-selector-contracts`](../decisions/the-classifier-and-selector-contracts.md).

## Context

ADR 0023 names three contracts owned by `Varve.Store`, all free of SPARQL
and SHACL: the key store, the **classifier** and the **selector**. It
describes them in words:
- the classifier: "given the pending operations and the pinned quad source,
  assign term occurrences to data subjects";
- the selector: "from a quad source and a data subject to a set of quads",
  for access without erasure mode.

Row-level access is in 1.0 and needs both, with erasure mode off. So their
shapes are fixed here, as stable public API. The key store is part 2's
(#203).

## Decision

1. **`IRowClassification`**, in `Varve.Rdf` (layer 1), is a classification at
   one state, keyed by the handles of one quad source. It answers:
   - `BindingOf(graph)`: the index of the binding whose Γ_B holds the graph,
     or −1;
   - `SubjectsOf(binding, node)`: an interned **subject-set id**, 0 when the
     node is unclassified;
   - `Members(binding, set)`: the subjects of a set, as handles;
   - `IsPublicLink(binding, predicate)`;
   - `Binding(index)` and `BindingCount`.

   It lives at layer 1 because `RowScopedQuadSource` (ADR 0235) reads it. It
   sits beside `GraphScope`, which `GraphScopedQuadSource` reads.
   `BindingOf`, `SubjectsOf` and `IsPublicLink` are `[HotPath]`: they
   allocate nothing.
2. **`IClassifier`**, in `Varve.Store` (layer 4), is ADR 0023's classifier.
   - `Classify(IQuadSource pinned, QuadDelta pending)` gives the
     `IRowClassification` of `Overlay(pinned, pending)` (spec §6, R4), in the
     overlay's handles.
   - `pending` may be empty: the classification of a state.
   - **It is a pure function** of the state and of the classifier's
     configuration (ADR 0230 point 6). The configuration is fixed when the
     classifier is built for a binding at a management position.
3. **`ISelector`**, in `Varve.Store`, is ADR 0023's selector.
   - `Select(IQuadSource source, IRowClassification classification, RdfTerm
     subject)` gives a cursor over every quad of `source` that is about the
     subject: ADR 0230 point 2's about-set contains it.
   - **One implementation, `ClassificationSelector`**, is in `Varve.Store`. It
     is a function of the classification alone, so there is one definition
     of "a subject's data", as ADR 0023 wants of the key id.
   - **Its cursor compares handles through the source's `TermComparer`.** ADR
     0022 makes that the only correct equality. It allocates nothing, and
     `SelectorComparesThroughTheSourceComparer` answers `VARVE0003` for it.
4. **Access without erasure mode** is served by the selector over `G_head`:
   - `GET /datasets/{name}/access?subject=<IRI>&binding=<IRI>`, as N-Quads
     or TriG by negotiation;
   - **admin only**, because it is a controller's act under GDPR Article 15
     and not a reader's;
   - scope `Current` only. `AllHistory` needs key ids, and is part 2's (#203).
     A request asking for `AllHistory` on a dataset without erasure mode is
     `400 bad-request` naming the scope.

   The response names the binding and the position it read.
5. **Erasure mode** (part 2) uses the same `IClassifier` at commit time to
   choose the private occurrences. The contract does not change for it.

## Alternatives considered

- **The classifier as an `IProjection`.** Classification at commit time, for
  the write rule and for erasure, needs the staged state, which no
  projection has. The projection (ADR 0233) wraps a classifier, not the
  other way round.
- **`IRowClassification` in `Varve.Store`.** `RowScopedQuadSource` is at
  layer 1 and could not read it.
- **A selector by query or by shape**, ADR 0019's form. It is a second
  definition of a subject's data, and ADR 0023 superseded 0019 to avoid
  exactly that.
- **Access open to the subject themselves**, by a template grant. The right
  of access is exercised through the controller, who decides what to
  disclose (Article 15(4)). A self-service route is a later decision.

## Consequences

- **`Varve.Rdf`'s and `Varve.Store`'s baselines** gain the members above. They
  are stable 1.0 contracts.
- A dataset without erasure mode can now **answer an access request** and
  still **never an erasure**, which is ADR 0023's statement made true.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0005**: SPARQL-free contracts;
  - **0022**: handles of one source;
  - **0023**: amended, as above;
  - **0091**: a new route;
  - **0107**: admin is dataset-wide.

  No conflict.
- **Layer ownership.** `Varve.Rdf` (1): `IRowClassification`. `Varve.Store`
  (4): `IClassifier`, `ISelector`, `ClassificationSelector`.
  `Varve.Protocol` (5): the route.
- **Analyzer rule.** None new.
- **Open questions owned.** None.
