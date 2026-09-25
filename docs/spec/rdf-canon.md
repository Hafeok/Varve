# RDF dataset canonicalisation

Functional specification for canonicalisation in `Varve.Rdf` (layer 1): RDF
Dataset Canonicalization, RDFC-1.0, over any quad source.

Status: Accepted. Changes only together with the ADR that motivates the
change. Decision: [ADR 0059](../adr/0059-rdfc-in-varve-rdf-and-its-work-limit.md).

## 1. Normative references, and their status

- **RDF Dataset Canonicalization (RDFC-1.0)**, W3C Recommendation, 21 May
  2024. Section numbers below are its own. It is defined over RDF 1.1
  datasets.
- **RDF 1.1 N-Quads** for the grammar the canonical form restricts (Appendix
  A); `n-triples.md` for this repository's N-Triples and N-Quads.
- **FIPS 180-4** for SHA-256 and SHA-384, through the BCL.

## 2. What it computes

```csharp
CanonicalDataset result = RdfCanonicaliser.Canonicalise(source, options, cancellationToken);
ReadOnlyMemory<byte> nquads = result.NQuads;                              // canonical N-Quads, UTF-8
IReadOnlyDictionary<string, string> issued = result.IssuedIdentifiers;   // input label → c14nN
```

- **Input** is any `IQuadSource` (ADR 0022): every quad `Match` yields under
  `GraphPattern.Any` — the default graph and every named graph. An input
  blank node is identified by its handle under the source's `TermComparer`,
  and named in the result's map by the label `TryExternalise` gives it. A
  store's labels are unique within the dataset (ADR 0044); an in-memory
  dataset's are the labels it was given.
- **Output** (§4.4.3, final steps):
  - **the canonical N-Quads form** of the normalised dataset, as UTF-8 bytes:
    each quad with its blank nodes relabelled by the canonical issuer, in
    Appendix A's form, one per line, the lines sorted in Unicode code point
    order and duplicates removed;
  - **the issued identifiers map**: each input blank node's label to its
    canonical identifier, `c14n0`, `c14n1`, … in issue order (§4.2's
    canonical issuer, §4.5).
- **Options** (`CanonicalisationOptions`, immutable):
  - `HashAlgorithm` — SHA-256 by default; SHA-384 as §3.1 requires ("MUST
    support SHA-256 and SHA-384"); SHA-512 as well, because the BCL has it
    and §3.1 says other algorithms SHOULD be selectable. Anything else is
    refused with `ArgumentException`. Through `IncrementalHash`, so one hash
    object serves one call.
  - `WorkLimit` — the most calls to Hash N-Degree Quads (§4.8) one
    canonicalisation may make, counting recursive calls, **as a multiple of
    the number of blank nodes** that reach §4.4.3 step 5 without a unique
    first-degree hash; default **12**. §4.4.3: "implementations MUST defend
    against potential denial-of-service attacks by raising suitable exceptions
    and terminating early", and "for most typical datasets, more than a couple
    of iterations on Hash N-Degree Quads per blank node would be unusual".
    Exceeding it throws `CanonicalisationLimitException`, which says how many
    calls were made and what the limit was. The default is measured, not
    guessed: §8 states how many calls each suite case needed.
- **Cancellation** is checked at every call to Hash N-Degree Quads and every
  permutation.

## 3. The algorithm — §4

Implemented as §4.4.3 (Canonicalization Algorithm), §4.5 (Issue Identifier),
§4.6.3 (Hash First Degree Quads), §4.7.3 (Hash Related Blank Node) and §4.8.3
(Hash N-Degree Quads) state it, step for step, with the step numbers in the
code's comments. Points where an implementation can go wrong without the
specification saying so twice:

- **Code point order.** Every sort the algorithm makes — hashes, N-Quads
  lines, related-hash keys, paths — is Unicode code point order (§4.4.3,
  referring to XPath's codepoint collation). The hashes are lowercase
  hexadecimal ASCII and the lines are UTF-8, and UTF-8 byte order *is* code
  point order, so every comparison is an ordinal comparison of bytes. No
  UTF-16 string is compared: UTF-16 order differs from code point order above
  U+FFFF.
- **The first-degree serialisation** (§4.6.3) replaces the reference blank
  node with `_:a` and every other blank node with `_:z`, including a blank
  node in the graph position, serialises each quad in Appendix A's form with
  its line ending, sorts, joins and hashes.
- **Related hashes** (§4.7.3) use the position letter `s`, `o` or `g` — never
  `p`, which cannot be a blank node — and prefer the canonical issuer's
  identifier, then the path issuer's, then the first-degree hash.
- **Permutations** (§4.8.3 step 5.4) are generated in lexicographic order of
  the related blank nodes' list, and the "chosen path" comparison is
  shortest-then-code-point order, with the early exit of step 5.4.4.2.2 when a
  path under construction is already longer than the chosen one.
- **Duplicates.** The input is a set; a source that yields a quad twice has
  it once in the output (test076).

### 3.1 Triple terms

RDFC-1.0 predates RDF 1.2 and says nothing about triple terms. A triple term
**with no blank node anywhere inside it** is a ground term: it is serialised
in N-Quads 1.2's `<<( s p o )>>` form, and hashes like any other ground term.
A triple term that contains a blank node is not covered by any sentence of
§4, and **is refused** with `ArgumentException` naming the gap, rather than
canonicalised by an extension that would be ours and not the specification's.

## 4. The canonical N-Quads form — Appendix A

Written by `Varve.Rdf` itself: the N-Quads writer is `Varve.Turtle`'s, at
layer 2, and layer 1 cannot reference it (ADR 0003). The form is Appendix A's,
which extends canonical N-Triples with the graph label:

- one space after the subject, the predicate, the object and the graph label,
  and no other white space; each line ends with a single LF, the last
  included;
- an `xsd:string` literal is written without its datatype;
- a language tag is written as the term holds it, and a base direction as
  `--ltr` or `--rtl` (N-Quads 1.2);
- within a string, BS, HT, LF, FF, CR, `"` and `\` are written as `ECHAR`;
  U+0000–U+0007, VT, U+000E–U+001F, DEL and every character that does not
  match XML 1.1's `Char` production (the surrogate code points, which a
  well-formed term cannot hold, and U+FFFE, U+FFFF) are written as `UCHAR`
  with a lowercase `\u` and four uppercase hexadecimal digits; every other
  character is written as itself;
- IRIs are written between `<` and `>` as the term holds them; `HEX` is
  uppercase.

**This is not the canonical form `n-triples.md` §5 defines**, and the
difference is a finding of this milestone. That form is RDF 1.1 N-Triples §4:
`ECHAR` for `"`, `\`, LF and CR only, and never `UCHAR`. Appendix A adds BS,
HT and FF to the `ECHAR` set and requires `UCHAR` for the other control
characters and DEL, which RDF 1.1's form writes raw. A literal holding a tab
is therefore written differently by `Varve.Turtle`'s canonical writer and by
the canonicaliser. `n-triples.md` is not changed by this milestone; §8 of
the report proposes aligning it with RDF 1.2 N-Triples, whose canonical form
is Appendix A's.

## 5. Why it is in `Varve.Rdf`, and bounded

ADR 0030 kept dataset isomorphism out of `Varve.Rdf` because a backtracking
search is exponential in an input nobody bounds. RDFC-1.0's Hash N-Degree
Quads is exponential in the worst case too — permutations of related blank
nodes — and the brief places canonicalisation in the RDF model all the same.
The answer ADR 0059 gives is the work limit of §2: the cost is bounded by a
stated, configurable number of calls, and exceeding it fails explicitly
rather than running without end. That bound is what makes the algorithm
acceptable as public API where the harness's search was not.

## 6. Isomorphism

Two datasets are isomorphic if and only if their canonical N-Quads forms are
equal (§1's claim of the algorithm, for datasets within the work limit). The
conformance harness compares `CONSTRUCT` results and update results that way,
and runs its backtracking check beside it as a cross-check (ADR 0059).

## 7. Tests, and the gate

- **The W3C suite**, `w3c/rdf-canon` as a second submodule
  (`tests/w3c/rdf-canon`, pinned, ADR 0007's pattern): every
  `RDFC10EvalTest` compares the canonical form with the expected file byte
  for byte; every `RDFC10MapTest` compares the issued identifiers map with
  the expected JSON; every `RDFC10NegativeEvalTest` must fail with
  `CanonicalisationLimitException` under the default limit. `rdfc:hashAlgorithm`
  selects the hash. The inputs are read with `Varve.Turtle`'s N-Quads reader.
  Guard counts, and the ratchet.
- **Isomorphism as a property.** For generated datasets and generated
  relabellings and reorderings of them, `iso(A, B) ⇔ canon(A) = canon(B)`,
  with `iso` the harness's backtracking check — which is therefore also that
  check's differential test. The generator produces isomorphic pairs by
  permuting labels and quad order, and non-isomorphic pairs by one edit, and
  counts both kinds; each kind must occur.
- **Idempotence**: `canon(canon(A)) = canon(A)`, reading the canonical form
  back with `Varve.Turtle`.
- **The canonical form parses**: every canonical output read back with
  `Varve.Turtle`'s N-Quads reader gives the dataset it was computed from, up
  to the issued relabelling.
- **SHA-384 in the browser** is measured by the browser smoke app (ADR 0028's
  table gains the row).

## 8. Open questions

1. **Triple terms containing blank nodes** (§3.1): refused until RDF 1.2
   says how RDFC applies to them.
2. **`n-triples.md`'s canonical form** (§4) — whether it becomes RDF 1.2's.
   Proposed in this milestone's report; decided by the maintainer.
