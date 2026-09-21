# 0024 — RDF term representation

## Status

**Accepted.** 2026-09-21.

Records [`docs/spec/rdf-model.md`](../spec/rdf-model.md) §5.

## Context

`docs/brief.md`, constraint 5: use `Span`, `Memory`, pipelines, `ref struct`s
and pooled buffers in parsers, the term dictionary and index scans —
**allocation per quad is a defect**.

That is a hard constraint on the type that a parser hands out. It is not a
constraint on the type someone stores in a dictionary, puts in a list, or
compares across a join, and those are different jobs. A single representation
would have to be either allocation-free and unstorable, or storable and
allocating — and the second loses the constraint outright, because a parse of
ten million quads would allocate at least ten million terms.

There is a second force, easy to miss until it bites. **RDF 1.2 triple terms
nest**: a triple term's object may itself be a triple term. A `ref struct`
cannot contain a field of its own type, so a recursive value representation of
a view is not merely awkward — it does not compile.

## Decision

**Two representations, for two paths.**

### The view — zero allocation, unstorable

`RdfTermView` and `QuadView` are `readonly ref struct`s handed to a parse
callback. Their spans point into the parser's own buffer and are **valid for
the duration of the callback and no longer**.

Nesting is by **arena**, not by value: the parser holds a pooled array of term
slots for the current quad, and a view is `(slots, text, index)`. A triple
term's components are other slots, addressed by index. That is what makes
`RdfTermView.Subject` able to return an `RdfTermView`.

`ref struct` is doing real work here and is not decoration. It is what makes a
use-after-free **fail to compile**: a view cannot be captured by a lambda,
stored in a field, boxed, or put in a collection. The alternative — a normal
struct with a documented lifetime — puts that rule in a comment.

### The owned term — allocating, storable, value equality

`RdfTerm` is a sealed class with static factories and no public constructor. It
owns its bytes, compares by value with a cached hash, and nests naturally
because a class can reference a class.

**It is produced only when someone asks**: `RdfTermView.Materialise()`, or
`IQuadSource.TryExternalise`. Nothing on the streaming path calls either.

### The seam between them

`Materialise()` is the only crossing, and it is explicit at the call site. A
caller that writes `quad.Subject.Materialise()` inside a hot loop has written
an allocation they can see, which is the most a type system can reasonably do.

## Alternatives considered

- **A class hierarchy only** — `abstract RdfTerm` with `Iri`, `BlankNode`,
  `Literal` and `TripleTerm` subclasses. The familiar shape, and what almost
  every RDF library in every language does. Lost to constraint 5 alone: a parse
  allocates one object per term, so four per quad plus the literal's datatype,
  and a ten-million-quad load allocates forty million objects it immediately
  discards. Equality also becomes a virtual call on the hottest comparison in
  the system.
- **A single struct with a payload reference** — one `readonly struct` with a
  kind tag, a `ReadOnlyMemory<byte>` and an `object?` for the datatype or the
  nested triple. Storable, no allocation for the struct itself. Lost on the
  nesting: the `object?` slot becomes a union of "datatype IRI" and "nested
  triple", discriminated by the kind tag, which is a tagged union simulated in
  a language that does not have one — and every access to it is a cast that the
  compiler cannot check. It also still allocates for the payload, so it does
  not actually win the constraint it was proposed for.
- **A view-only design, with no owned type at all.** Maximally honest about
  the streaming model. Lost because there is then no way to hold a term:
  `IQuadSource.TryExternalise` has nothing to return, a test cannot keep a term
  to compare against, and an in-memory dataset cannot have an interning table.
  Everything that is not a parser needs a term that outlives a callback.
- **Generic over the buffer type**, so a view could be backed by a span, an
  array or a sequence. Lost on ADR 0022's argument: every instantiation is real
  code under Native AOT, and the binary is downloaded in a browser. One backing
  representation, and the pooled scratch buffer handles the segment-spanning
  case.

## Consequences

**Two types where one would be simpler**, and a reader of the API meets both.
The naming carries the distinction — `RdfTermView` versus `RdfTerm` — and the
lifetime rule is in the type system rather than in the documentation, which is
the best available answer to "someone will hold on to it".

**The parser's arena is an implementation detail that shapes the public type.**
A view is `(slots, text, index)` because triple terms nest, and that is visible
in the API only as the fact that `RdfTermView.Subject` exists and returns a
view. If RDF 1.2 drops nesting before Recommendation — unlikely, but Concepts
is only at Candidate Recommendation — the arena could be simplified away and
the public shape would not change.

**`Materialise()` is a cost the caller can see and the compiler cannot stop.**
There is no rule that prevents materialising in a loop. VARVE0006, when it
lands, is the mechanism that could: a `[HotPath]` method calling `Materialise`
is exactly the shape it is meant to catch, and the parse and write paths are
marked now so that the rule has something to check (ADR 0026).

**The owned term's equality is term equality, not value equality.**
`"1"^^xsd:integer` and `"01"^^xsd:integer` are different terms here and will be
equal values once `Varve.Xsd` exists at milestone 3b. Nothing at 3a may assume
otherwise, and `rdf-model.md` §6 says so.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0020–0023).
  Touches **0022** (the owned term is what `TryInternalise` takes and
  `TryExternalise` returns, so these two ADRs meet at that contract) and
  **0012** (nothing here sees a `TermId`; ids are the store's, terms are the
  model's). No conflict with any.
- **Layer ownership.** Both representations are **`Varve.Rdf`, layer 1**. The
  view is produced by `Varve.Turtle` at layer 2, which is a downward reference.
- **Analyzer rule.** None new. **VARVE0006** (hot path discipline) is the
  reserved rule that bears on this, and ADR 0026 decides where its attribute
  lives so the marking can start now.
- **Open questions owned.** None.
