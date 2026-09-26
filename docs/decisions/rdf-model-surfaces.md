---
set: rdf-model-surfaces
namespace: varve
origin: "DD0009 and DD0013 findings on Varve.Rdf in session 2 of #43"
decisions:
  - key: QuadCursorIsForwardOnlyAndDisposable
    statement: "A quad source answers a match with IQuadCursor, a forward-only walk with no reset that the caller disposes, not IEnumerator, so a source may hold a lock, a pinned segment or a snapshot for the cursor's lifetime"
  - key: InlineValueIsAUnionOfTypedPrimitives
    statement: "InlineValue is a union over the primitives a handle can encode, and its Integer is the long the evaluator asked for (ADR 0050), not a single-primitive wrapper"
  - key: CanonicalisationWorkIsAnInteger
    statement: "CanonicalisationOptions.WorkLimit and CanonicalisationLimitException's Steps and Limit count RDFC-1.0 work steps as integers (ADR 0059)"
  - key: InMemoryTermCountIsAnInteger
    statement: "InMemoryDataset.TermCount reports the size of the dataset's interning table as an int"
---

# Contracts and primitives on Varve.Rdf's surface

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

Declaring `Varve.Rdf` a `[DomainModel]` namespace (ADR 0064) brings its
interfaces under `DD0009` and its public members under `DD0013`. ADR 0065
decided `QuadCount` for a count of quads and deferred the rest to this sorting,
by the two-bucket rule: a decision filed, or the design change. These four are
filed, each with its alternative written down.

**`QuadCursorIsForwardOnlyAndDisposable`.** `IQuadCursor` has been the return
of `IQuadSource.Match` since milestone 3a, and no ADR names it. ADR 0022
enumerates the quad source contract and never mentions the cursor, so it has no
decision to cite. Its shape is a real choice. It is not `IEnumerator<Quad>`,
because it has no `Reset`. Its disposal is part of the contract, because a store
holds a snapshot for the cursor's lifetime (ADR 0052's pinned read). The
alternative, `IEnumerator<Quad>`, gives up both.

**`InlineValueIsAUnionOfTypedPrimitives`.** ADR 0065 names `InlineValue.Integer`
as undecided: "an inline value is a union over several primitives, so it is not
a single-primitive wrapper, and its `long` is the typed value the evaluator asked
for". The alternative is `XsdInteger`, which `Varve.Rdf` cannot name: it does
not reference `Varve.Xsd`, and both are below the evaluator. That would be a
new reference, at the same layer as `Varve.Iri`, which is legal (0 is below 1)
and not decided anywhere.

**`CanonicalisationWorkIsAnInteger`.** ADR 0065 names these as undecided too.
The limit is a multiple of the non-unique blank nodes (ADR 0059), the steps are
calls and permutations, and nothing else in Varve counts either. The
alternative is a `CanonicalisationWork` wrapper.

**`InMemoryTermCountIsAnInteger`.** ADR 0067 keeps `TermCount` on the immutable
dataset. Only tests read it, to check that interning is idempotent. The
alternatives are a wrapper, or removing the member and letting the tests count
through `TryExternalise`.
