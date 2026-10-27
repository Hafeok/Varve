# 0058 — A staging view over a pinned read, and validators bound to a dataset

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5c plan:
dataset-level validators, with request validators kept and run alongside;
and the staging view, as specification T1 step 2 exposed, with the invariant
stated below. Refines ADR 0017 (who supplies validators) and exposes what ADR
0012 and T1 step 2 already do inside the sequencer. Neither is changed.

**Amended 2026-10-27** (ADR 0068), filed unaccepted: `CommitRequest` is
renamed `CommitRequestBuilder`; see the end.

## Context

Two gaps appeared when SPARQL Update (ADR 0057) was written against the store.

**Validators.** T1's input is "an ordered list of assert and retract
operations over terms, metadata, an optional expected position, zero or more
pre-commit validators", and the store implements exactly that:
`CommitRequest.Validators`, per request. The brief's use of the hook is
different — "shapes graphs bound to a dataset or named graph can gate a
transaction" — and so is what a caller of SPARQL Update expects: a validator
that the writer chooses per request is one the writer can leave out. A gate
has to be the dataset's.

**Terms a request creates.** SPARQL Update evaluates each operation over the
state the earlier ones produced (ADR 0057), which is
`Overlay(pinned, δ₁ ; … ; δₖ₋₁)`. The overlay (ADR 0017) is over the base
source's handles. But an earlier operation may have asserted a quad about a
term the dataset has never held — a new IRI, a literal, a fresh blank node —
and a pinned view has no handle for it: `TryInternalise` answers false, as the
contract says it must for a term the source does not hold. The integration
cannot invent handles of its own, because handles are opaque (ADR 0022) and
any value it chose might be one the store issues. The sequencer already solves
this problem internally, in T1 step 2: "Unknown terms get provisional fresh
ids", visible to validators through the pending view. What is missing is that
step, exposed.

## Decision

### Validators bound to the dataset

**`DatasetOptions.Validators`** — a list of `ICommitValidator`, empty by
default — run on every `Data` commit, in list order, **before** the request's
own validators, which are kept and run after them. Both see the same pair,
`Overlay(G_head, δ)` and `δ` (ADR 0017), and either may reject; an accepting
validator's attachment is recorded whichever list it came from. `Settings`
and `Erasure` commits carry no delta and are not validated, as now.

The binding is an option of the open dataset, not a fact in the log: a
validator is code, and a log that named code could not be replayed where the
code is absent. Recording *which shapes graph* gates a dataset, as data that
travels with the log, is a SHACL decision for milestone 8.

### The staging view

**`DatasetView.Stage()`** returns a `StagingView`: an `IQuadSource` that reads
exactly what the view reads, and that can also give a handle to a term the
view does not hold.

- **`Stage(term)`** returns the view's handle for a term it holds, and
  otherwise a **provisional handle**: interned by value, so the same IRI or
  literal staged twice is one handle. A blank node term is refused — blank
  nodes are fresh, not looked up (ADR 0044) — and so is a triple term with a
  blank node inside.
- **`StageBlank()`** returns a new provisional blank node, distinct from every
  other.
- **`StageTriple(s, p, o)`** returns the handle of the triple term over three
  handles, the view's own when it holds one.
- **Reads** — `Match`, `Contains`, `Estimate` — are the view's: no quad of
  `G_P` mentions a provisional handle, so a pattern that binds one matches
  nothing until an overlay adds quads that do. `TryExternalise` answers for
  provisional handles too; a provisional blank node's label is distinct from
  every label the view gives its own.
- **`ToRequestTerm(handle)`** maps a handle to what a `CommitRequest` takes:
  the view's own handle as `RequestTerm.Existing`, a provisional IRI, literal
  or triple term as that term, and a provisional blank node as a blank node
  term whose label is unique within the staging view — so all its
  occurrences in one request are one fresh node (T1 step 2). A triple term
  built around one of the view's blank nodes cannot be mapped — ADR 0044's
  stated limit — and throws.

### The invariant

**A staging handle reaches the dictionary or the log only through the
commit that maps it.** Provisional handles are drawn from a reserved range of
the id space that the dictionary never issues, so:

- given to the sequencer directly, as `RequestTerm.Existing`, a provisional
  handle is an unknown id and fails the request, as ADR 0044 already
  requires for any unknown handle;
- mapped with `ToRequestTerm`, it becomes a term, which T1 step 2 resolves
  like any other: a canonical term already allocated by a later commit is
  found, and anything else is allocated in the commit that closes, or not at
  all.

**Rejected or empty requests leave no trace** — I3 as it stands: a staging
view allocates nothing in the dictionary, and dropping one discards its
provisional terms with it.

## Alternatives considered

- **Validators passed through `UpdateOptions`.** No store change. Rejected by
  the maintainer: it makes the gate the writer's choice, and a second writer
  using `CommitAsync` directly would bypass it.
- **Validators recorded in dataset settings** (ADR 0021), so that every copy
  of the log agrees on them. Rejected for now, above: settings are data and a
  validator is code. A shapes-graph reference in settings may come at
  milestone 8.
- **The integration mints its own handles** for new terms, in a tagged range.
  Rejected: handles are opaque (ADR 0022); the integration cannot know which
  values the store issues, and a collision would make an overlay confuse a new
  term with an existing one — a wrong answer, not an error.
- **The integration re-maps every handle** through a table of its own, so that
  its handle space is its own. Correct, and it costs a lookup per quad
  scanned and an allocation per term: the evaluator's hot path would pay for
  a request's handful of new terms.
- **Materialise the request's state** in an `InMemoryDataset` once an
  operation creates a term. Proportional to the dataset, not the change.
- **A general "extensible source" in `Varve.Rdf`** that any source could
  support. Rejected: only the store knows its id space, and a layer 1 contract
  for reserving a range of it would describe a store.

## Consequences

- **`Varve.Store`'s public surface grows** by `DatasetOptions.Validators`,
  `DatasetView.Stage()` and `StagingView`'s members, recorded in its
  baseline.
- **A validator bound to the dataset gates every writer**, SPARQL Update and
  `CommitAsync` alike, and adds its cost to every commit (ADR 0017's
  consequence, unchanged).
- **The staging view is the pending view's public twin.** Both implement T1
  step 2's provisional ids; they share the reserved range's definition.

## Checks

- **Checked against the accepted ADRs** (0001–0057) and the specification.
  Touches **0010** and I3 (no trace from a rejected or empty request),
  **0012** (the id classes; the reserved range is inside the canonical and
  blank classes' counters and above anything a counter reaches), **0017**
  (validators and the overlay, refined for who supplies validators), **0021**
  (validators are not settings), **0022** (handles stay opaque; provisional
  handles compare by id under the view's comparer), **0044** (fresh blank
  nodes; unknown handles fail; the triple-term limit) and **0057** (the
  consumer). No conflict with any.
- **Layer ownership.** All of it is `Varve.Store`, **layer 4**, over
  `Varve.Rdf` types.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-19 — host attachments first

Filed by milestone Management plane of #105, unaccepted until the maintainer
accepts it (ADR 0066).

A commit's host attachments (ADR [0165](0165-the-management-position-on-the-commit.md))
are recorded before the dataset's validators' and the request's.

**The ledger.** The ruling is ADR 0165's `HostAttachmentsOnTheCommit`.

## Amendment, 2026-10-20 — which shapes graph gates a dataset

Filed by SHACL, part 2a (#138, under milestone 8, #13), unaccepted until the
maintainer accepts it (ADR 0066).

The question left to milestone 8 above — "recording *which shapes graph* gates
a dataset, as data that travels with the log" — is answered by ADRs
[0169](0169-commit-time-gating-under-a-shapes-binding.md) and
[0170](0170-the-shapes-binding-vocabulary.md): a binding is a management
declaration in the system dataset, applied at the management position a commit
is authorised under (ADR 0165), and the shapes graph it names is a named graph
of the gated dataset, in its log. The validator itself stays code, as decided
above: `Varve.Shacl.Store`'s, in `DatasetOptions.Validators`.

**The ledger.** The rulings are ADR 0170's `ShapesAreDataInTheDataset` and
ADR 0169's `ShaclStoreAtLayerFive`.

## Amendment, 2026-10-27 — `CommitRequest` is `CommitRequestBuilder`

Filed with the move to `DecisionDriven.Analyzers` 0.1.0-preview.10 (#151),
unaccepted until the maintainer accepts it (ADR 0066).

The request this ADR's validators and `ToRequestTerm` speak of, `CommitRequest`, is
`CommitRequestBuilder`. The accepted text above keeps the old name as written.

`DecisionDriven.Analyzers` 0.1.0-preview.10 reports `CommitRequest` under
DD0019: `Assert` and `Retract` add to the operations it holds, so a public
model type changes after it is made. The finding offers two answers. One is
`[DesignDecision]` citing an accepted decision, which accepts a violation; no
decision says the request is a model value, and none was filed. The other is
the rule's own exemption, a sealed `*Builder`: a type mutable by design that
belongs to the code assembling a value. That is what the type is. Its caller
assembles it and passes it once to `Dataset.CommitAsync`, and it is never
handed out as a value. So the type is renamed `CommitRequestBuilder` and
nothing else about it changes: the same members, the same contract. Breaking
before 1.0 (ADR 0118).

**The ledger.** No key of this ADR's set names the type; none changes.
