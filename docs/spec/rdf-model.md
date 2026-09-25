# RDF model

Functional specification for `Varve.Rdf` (layer 1).

Status: Accepted. Changes only together with the ADR that motivates the change.

Scope: terms, triples, quads, datasets, and the quad source contract. No
syntax, no store, no SPARQL.

## 1. Normative references, and their status

- **RDF 1.1 Concepts and Abstract Syntax** — W3C Recommendation, 25 February
  2014. The conformance baseline (`docs/brief.md`).
- **RDF 1.2 Concepts and Abstract Data Model** — **Candidate Recommendation
  Snapshot, 07 April 2026**, checked on 2026-09-21. Not a Recommendation.
- **RDF 1.2 N-Triples** — **Working Draft, 23 July 2026**, checked the same
  day. It states that the extension "is fully backward compatible: any document
  complying with the old version complies with the new version, and parses to
  the same graph."

**What that means here.** The brief says RDF 1.2 is in the model from the start
so it is not a retrofit, and the conformance target is RDF 1.1. Both hold: the
model carries the 1.2 additions, the parsers accept 1.1 documents exactly as
1.1 defines them, and **the 1.2 additions are unreachable from a 1.1 document**
because 1.2 is backward compatible. Nothing about 1.2's maturity can therefore
affect a 1.1 conformance result.

The 1.2 surface is at Candidate Recommendation for Concepts and Working Draft
for N-Triples, so **it may still change**. Two consequences are accepted
deliberately: the two 1.2 additions below are in the type model from milestone
3a, and if either changes shape before Recommendation, that is an ordinary
breaking change to a package that has not been published.

## 2. Terms

A term is one of four kinds.

| Kind | Content | Reference |
|---|---|---|
| **IRI** | an absolute IRI, optionally with a fragment | Concepts §3.2, and `iri.md` |
| **Blank node** | an identifier with no meaning beyond identity | Concepts §3.3 |
| **Literal** | a lexical form, and either a datatype IRI or a language tag | Concepts §3.3 |
| **Triple term** | a triple used as a term | RDF 1.2 Concepts |

### Literals

A literal is a lexical form (a Unicode string) plus **exactly one** of:

- a **datatype IRI**. A literal written with no datatype and no language tag has
  datatype `xsd:string` (Concepts §3.3) — the absence is a shorthand, not a
  third state.
- a **language tag** (well-formed per BCP 47), in which case the datatype is
  `rdf:langString`, **plus an optional base direction** — RDF 1.2's
  *directional language-tagged string*, `ltr` or `rtl`.

**Language tags compare case-insensitively** (Concepts §3.3: two
language-tagged strings are equal if their language tags match ignoring case),
while **lexical forms and datatype IRIs compare exactly**. This asymmetry is in
the specification and is a real source of bugs; it is stated here so that the
equality implementation has somewhere to point.

Until `Varve.Xsd` exists a datatype is an IRI and nothing more: no value space,
no canonical lexical form, no comparison by value.

**A literal's equality is term equality — permanently, not until 3b.** Same
lexical form, same datatype IRI, same language tag and direction, compared
character by character (Concepts §3.3). That is what the abstract syntax defines
and what the syntax suites test, and `Varve.Xsd` will not change it: value
comparison is the evaluator's (SPARQL 1.1 §17.3, §17.4.1.7) and lives at layer
3, above the model. A change here would change what a graph contains; a change
there changes what a query answers.

### Triple terms

RDF 1.2 allows a triple to appear as the object of another triple. They nest.
The model represents that directly; the parser represents it as an index into a
per-quad arena rather than as a recursive value, because a `ref struct` cannot
contain itself (ADR 0024).

## 3. Triples, quads, graphs, datasets

- A **triple** is (subject, predicate, object). Concepts §3.1.
- A **quad** adds a graph name. A quad whose graph name is absent is in the
  **default graph**.
- A **dataset** is a default graph plus zero or more named graphs. Concepts §4.

Constraints from Concepts §3.1, enforced by the parsers rather than by the type
system:

- a subject is an IRI or a blank node;
- a predicate is an IRI;
- an object is any term;
- a graph name is an IRI or a blank node.

They are checked where the bytes are, because a parse error can then say which
byte was wrong. A type system that made them unrepresentable would need four
term types and a conversion at every boundary, and would still have to report
the error.

## 4. The quad source contract

Defined by **ADR 0022**, recorded here because it is the model's public face.

A quad source hands out **opaque 64-bit term handles**, converts between a
handle and an owned term in both directions, answers containment, and yields
matches. **It supplies its own equality comparer**, and consumers must use it:
a readable private term compares by decrypted value rather than by handle
(specification §6), so comparing handles as integers would report equal terms
unequal in a dataset with erasure mode on.

Milestone 5a widened the contract by two members, each with its own ADR:

- **A cardinality estimate** for a match pattern (ADR 0049): exact, estimated,
  or unknown. An exact answer is the number of quads `Match` yields for the
  same pattern at the source's current state, and a test may assert equality;
  an unknown answer is honest and the consumer falls back to scanning; an
  estimated answer is a count the source has reason to believe, and its
  documentation says how. The in-memory dataset counts by scan; the store sums
  a prefix range per run, exact because each run is an exact delta (I2); an
  overlay adjusts its base by the delta.
- **An inline-value accessor** (ADR 0050): the value a handle encodes in its
  own bits, when it does — the store's canonical `xsd:integer` and
  `xsd:boolean` ids — as BCL primitives, so that a numeric comparison need not
  materialise a term. False means "not inline", never "not a number". The
  in-memory dataset has no inline handles and always answers false.

At milestone 3a the only implementation is an in-memory dataset with its own
interning table. It is a real implementation, not a test double — it is what
the round-trip property tests compare through.

## 5. Owned terms and views

Two representations, for two paths (ADR 0024):

- a **view** is a zero-allocation window over a parser's buffer, valid for the
  duration of the callback that receives it;
- an **owned term** is a heap object with structural equality — term equality,
  in the sense above, and not RDF *value* equality — produced only when someone
  asks for one.

**Allocation per quad on the streaming path is a defect** (constraint 5), so
the view is the default and materialising is opt-in. The allocation test
asserts zero bytes per quad, not a small number.

## 6. What this milestone does not model

- **Value spaces and value comparison** — `Varve.Xsd`, and then the evaluator.
  `"1"^^xsd:integer` and `"01"^^xsd:integer` are *different terms* here and
  stay different terms; they are equal *values*, which is a question only the
  evaluator asks. Nothing may assume the model answers it.
- **RDFC-1.0 canonicalisation** — its own specification, [`rdf-canon.md`](rdf-canon.md) (milestone 5c, ADR 0059).
- **Generalised RDF** (Concepts §5.2), where any term may appear in any
  position. Not supported; the constraints in §3 are enforced.
- **Term identity across a store** — blank node identity at an API boundary is
  Q1, owned by ADR 0012 and due at milestone 4. At 3a a blank node's label is
  scoped to the document that contained it.

## 7. Open questions

None owned here. Q1 is adjacent and belongs to ADR 0012.
