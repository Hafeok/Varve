# 0151 — SHACL inputs, failures and SHACL instances

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan,
with two conditions added at approval: shape constants are looked up in a data
source and never added to it, and `sh:pattern` runs under a timeout.

## Context

SHACL validates a *data graph* against a *shapes graph* (SHACL 1.2 Core §6).
Varve reads RDF through `IQuadSource` (ADR 0022), which holds a dataset, and
a validator in part 2 reads the overlay of a pending commit (ADR 0017), whose
handles may include provisional ones (ADR 0058). SHACL also leaves to the
processor what a *failure* looks like (§6.6.1), whether ill-formed shapes are
refused (§6.6.2, a SHOULD), whether `owl:imports` is followed (§6.1, a
SHOULD) and whether entailment regimes are supported (§1.4, a MAY, with a MUST
to fail when one is asked for and not supported).

## Decision

### 1. The inputs are two sources, or two graphs of one

- **The shapes graph** is a `GraphPattern` of an `IQuadSource`: the default
  graph, a named graph, or the union of named graphs. **The data graph** is a
  `GraphPattern` of an `IQuadSource`, which may be the same source.
- They are read only through the quad source contract. Nothing is copied into
  an intermediate dataset.

### 2. Shape constants are looked up, never added

Every term the shapes graph gives a component as a constant — `sh:in`,
`sh:hasValue`, `sh:class`, `sh:datatype`, `sh:targetNode`, a path's
predicates, `sh:ignoredProperties`, the values of `sh:languageIn` — is resolved
to a handle of the data source **by `TryInternalise` only**. A term the source
does not hold resolves to nothing and therefore matches nothing: `sh:hasValue`
of an absent term is a violation for every focus node, `sh:in` never matches
it, a target node it names is still a focus node (SHACL targets by term, not by
presence) and is validated over an empty neighbourhood. **The validator never
allocates a term in a data source's dictionary**, so validating a store, a
pinned view, a staging view or an overlay leaves it as it was. A store test
shows the dictionary unchanged across a validation.

### 3. SHACL instances, without entailment

- **A SHACL instance** of a class C is a node with an `rdf:type` whose value is
  C or a SHACL subclass of C (SHACL 1.2 Core §1.1): the path
  `rdf:type/rdfs:subClassOf*` in the data graph. That is SHACL's definition,
  not inference, and it is what `sh:targetClass`, implicit class targets and
  `sh:class` use. `sh:rootClass` uses `rdfs:subClassOf*` alone.
- **`ShaclOptions.SubClassOfInShapesGraph`** (§6.4) reads `rdfs:subClassOf`
  from the shapes graph as well; `rdf:type` is always the data graph's.
- **`sh:entailment`**: any value other than simple entailment
  (`http://www.w3.org/ns/entailment/Simple`) is a failure, as §1.4 requires of
  a processor that does not support it, until Inference (#95) states
  otherwise.

### 4. `owl:imports` is not followed

Nothing is fetched and nothing is merged: the caller composes the shapes graph
from the sources it wants. A shapes graph with `owl:imports` triples is
validated as given, and the report states it: **`varve:importNotFollowed`**,
one triple per imported IRI on the report node, in the Varve namespace
(`https://w3id.org/varve/ns#`, `docs/vocabulary.md`), and
`ValidationReport.ImportsNotFollowed` in the typed report. A shapes graph that
needs the imported shapes therefore validates less than it would elsewhere,
and says so.

### 5. Ill-formed shapes graphs are refused

The syntax rules of SHACL 1.2 Core Appendix A, and those its sections state
per component, are checked when a shapes graph is loaded. A shapes graph with
any ill-formed shape is **refused before validation** with a problem per
violation, each naming the shape and the rule (the section and the rule's
words). The refusal is the failure §6.6.2 recommends; no partial validation
runs. Only *shapes* are checked — nodes that are shapes by §3.1 — so an
ill-formed node nothing uses as a shape is not an error.

### 6. Failures are problems, not results

A **failure** (§6.6.1) is never a validation result and never a guessed
report. Validation either returns a report or fails with problems
(`ShaclProblem`: a kind, the shape and focus node where it arose, and a
message). Failures are:

- a refused shapes graph (ill-formed, ADR 0150's non-stratifiable cycle, a
  constraint whose extension is absent, ADR 0148);
- an unsupported `sh:entailment`;
- a pattern that fails to compile or exceeds **`ShaclOptions.RegexTimeout`**
  (one second by default, as `EvaluationOptions.RegexTimeout`). `sh:pattern`
  runs a shapes graph and data that may both be untrusted; a pathological
  pattern fails the validation with a problem naming the shape and the
  pattern, never hangs it;
- a failure reported by an extension, such as a SPARQL query that errors or
  binds `?failure` to true;
- cancellation, through the caller's `CancellationToken`, which is thrown as
  `OperationCanceledException` and is not a problem.

## Alternatives considered

- **Copy both graphs into one `InMemoryDataset`** and validate that.
  Simplest. Rejected: proportional to the data, not the shapes, and part 2
  validates every commit.
- **Internalise shape constants into the data source.** Saves a "not held"
  branch. Rejected by the maintainer: a store's dictionary would grow with
  every validation, and a staging view's invariant (ADR 0058) forbids it.
- **Follow `owl:imports`.** §6.1's SHOULD. Rejected: it fetches over the
  network from inside a validator, which ADR 0017 forbids at commit time and
  the browser host cannot do reliably.
- **Report ill-formed shapes as validation results.** Rejected: §6.6.2 says
  the outcome is undefined, and a report would present an undefined outcome
  as a defined one.
- **Ignore `sh:entailment`.** What shacl-rs does. Contrary to §1.4's MUST.

## Consequences

- A shapes graph that relies on imports validates less, and the report says
  so.
- A caller passes `CancellationToken` and `RegexTimeout` to bound a
  validation's cost; part 2's gate runs under both.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0017** (a validator reads only the overlay and the delta; this one reads
  nothing else and writes nothing), **0022** (handles stay opaque and are
  compared by the source's comparer), **0044** (a blank node is never looked up
  by label), and **0058** (provisional handles are ordinary handles here). No
  conflict.
- **Layer ownership.** `Varve.Shacl`, layer 3.
- **Analyzer rule.** None.
- **Open questions owned.** None.
