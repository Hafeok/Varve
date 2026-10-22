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
  - `WorkLimit` — the most steps one canonicalisation may take, **as a
    multiple of the number of blank nodes** that reach §4.4.3 step 5 without
    a unique first-degree hash, where a step is a call to Hash N-Degree Quads
    (§4.8), recursive calls included, or a permutation one of them examines;
    default **1,000**. §4.4.3: "implementations MUST defend against potential
    denial-of-service attacks by raising suitable exceptions and terminating
    early", and "for most typical datasets, more than a couple of iterations
    on Hash N-Degree Quads per blank node would be unusual". Exceeding it
    throws `CanonicalisationLimitException`, which says how many steps were
    taken and what the limit was. The default is measured, not guessed: the
    suite's most demanding computable cases, the three "poison – evil" graphs
    (test044–046), need 279; the next need 15; the ten-node clique of
    test074 meets 1,000 in about 60 ms, and meets 100,000 still unfinished. A
    test finds each positive case's least limit by search and holds the
    default at three times the worst.
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
- a language tag is written **in lowercase**, and a base direction as
  `--ltr` or `--rtl` (N-Quads 1.2). Appendix A says nothing about case, but
  language tags compare case-insensitively (RDF 1.1 Concepts §3.3), so
  `"x"@en` and `"x"@EN` are one term and must have one canonical form —
  which is what RDF 1.2 N-Triples §3 (Working Draft, 24 September 2026)
  requires: "Alphabetic characters in `LANG_DIR` MUST use only the lowercase
  letters". Found by the isomorphism property of §7, whose first run wrote
  one term two ways depending on which spelling a dataset interned first;
  no case in the W3C suite has an uppercase tag;
- within a string, BS, HT, LF, FF, CR, `"` and `\` are written as `ECHAR`;
  U+0000–U+0007, VT, U+000E–U+001F, DEL and every character that does not
  match XML 1.1's `Char` production (the surrogate code points, which a
  well-formed term cannot hold, and U+FFFE, U+FFFF) are written as `UCHAR`
  with a lowercase `\u` and four uppercase hexadecimal digits; every other
  character is written as itself;
- IRIs are written between `<` and `>` as the term holds them; `HEX` is
  uppercase.

**It is the canonical form `n-triples.md` §5 defines**, since ADR 0061.
Until then `Varve.Turtle`'s canonical writer followed RDF 1.1 N-Triples §4:
`ECHAR` for `"`, `\`, LF and CR only, no `UCHAR`, and a tag as held. That
made a literal holding a tab come out differently from the two writers, which
was a finding of milestone 5c. Both now write RDF 1.2 N-Triples §3's form.
They remain two pieces of code, one per layer, and the read-back property of
§7 requires that `Varve.Turtle`'s canonical writer reproduces this output byte
for byte.

## 5. Why it is in `Varve.Rdf`, and bounded

ADR 0030 kept dataset isomorphism out of `Varve.Rdf` because a backtracking
search is exponential in an input nobody bounds. RDFC-1.0's Hash N-Degree
Quads is exponential in the worst case too — permutations of related blank
nodes — and the brief places canonicalisation in the RDF model all the same.
The answer ADR 0059 gives is the work limit of §2: the cost is bounded by a
stated, configurable number of calls, and exceeding it fails explicitly
rather than running without end. That bound is what makes the algorithm
acceptable as public API where the harness's search was not.

## 6. Isomorphism, and where RDFC-1.0 falls short of it — a known limit

> **Corrected 2026-10-22.** This section said the converse below "holds for
> datasets whose graph names are IRIs". It does not: §6.2 is a counterexample
> with IRI graph names only, found by §7's property on CI. The claim is now
> what RDFC-1.0 guarantees, and the class this package tests the converse
> in (§6.3). ADR 0059 carries the amendment.

**What RDFC-1.0 guarantees, for every dataset canonicalised within the work
limit (§2):**

- **Equal canonical forms mean isomorphic datasets.** A canonical form is a
  relabelling of its dataset: §4.4.3's final steps replace each blank node
  with the identifier issued to it, and nothing else.
- **Determinism.** Every step is a function of its input, so the same quads
  in the same order give the same bytes. For `RdfCanonicaliser` the order is
  the order `Match` yields; two sources holding the same quads need not yield
  them in the same order.

**What it does not guarantee: that isomorphic datasets have equal canonical
forms.** §1 of the Recommendation states it as the algorithm's purpose, and
its §4.4.3 note relies on it, but it fails in two known cases. Both are the
same mechanism: a hash omits something a quad says, two blank nodes that are
not automorphic get equal hashes, and §4.4.3 step 5 orders the tied hash path
results "by the hash in result", which leaves the order to the input.

### 6.1 Blank nodes as graph names (#36)

Found by the 5c isomorphism property (§7); the smallest instance is

```
<http://example.org/a> <http://example.org/p> _:n1 _:n1 .
_:n4 <http://example.org/p> _:n0 _:n2 .
_:n4 <http://example.org/q> _:n3 _:n4 .
_:n3 <http://example.org/p> _:n2 _:n0 .
```

`_:n0` and `_:n2` are not automorphic — swapping them needs `_:n3` and
`_:n4` swapped, which the third quad forbids — but their first-degree hashes
are equal (§4.6.3 replaces every other blank node with `_:z`) and so are
their N-degree hashes: a related hash records the related node's position
(§4.7.3), here the subject in both quads, and not the reference node's,
which is the object in one quad and the graph name in the other. §4.4.3 step
5.4 orders the tied results "by the hash in result", which leaves the order
to the input, so two relabellings of this dataset get two canonical forms.
Only a quad with three blank nodes — subject, object and graph name — can
hide the reference node's position this way. Raised with the RDF & SPARQL
Working Group under ADR 0038, D2: `spec-gap`;
[#36](https://github.com/Hafeok/Varve/issues/36) tracks it and carries the
report and, once filed, its URL.

**It is the algorithm's, not this implementation's.** Over 300 random
relabellings and quad orders of the dataset above (measured 2026-09-25):

| Implementation | Canonical forms | Split |
|---|---:|---|
| Varve (this package) | 2 | 151 / 149 |
| pyoxigraph 0.5.11, `RDFC_1_0` | 2 — the same two, byte for byte | 147 / 153 |
| dotNetRDF 3.5.2, `RdfCanonicalizer("SHA256")` | 1 — neither of the two | 300 |

Varve and pyoxigraph produce the same pair of forms; which relabelling gets
which form differs between them, because the tie falls to each one's internal
order. dotNetRDF is stable on this dataset but disagrees with both from the
first issued identifier; why is not investigated here, and nothing is
concluded from it.

### 6.2 Blank nodes related across graphs (#142)

With IRI graph names only. Found by §7's property in the CI of #140 (seed
`1Axba7DbGjr2`, reproduced on `main`, after many passing runs) and minimised
to one dataset whose two quad orders get two canonical forms:

```
_:c <http://example.org/p> _:x <http://example.org/g> .
_:d <http://example.org/p> _:x .
_:c <http://example.org/p> _:y .
_:d <http://example.org/p> _:y <http://example.org/g> .
<http://example.org/a> <http://example.org/q> _:c .
```

`_:x` and `_:y` are not automorphic: `_:c`, the node `<a>` names, reaches
`_:x` in `<g>` and `_:y` in the default graph. Their first-degree hashes are
equal — each is the object of one quad in `<g>` and one in the default graph,
with a `_:z` as subject (§4.6.3) — and so are their N-degree hashes, because
a related hash (§4.7.3) is the position letter, the predicate and the related
node's identifier: **the graph of the quad that relates the two nodes is not
in it.** Both relate to `_:c` and to `_:d` as subjects of `<p>`; which of the
two relating quads is in `<g>` is lost. Hash N-Degree Quads (§4.8.3) builds
only from related hashes, so the tie reaches §4.4.3 step 5.

It needs two blank nodes related to the same nodes through quads that differ
only in their graphs. It is rare in random data: §7's generator met it once in
many runs of 20,000 pairs.

**It is the algorithm's, not this implementation's.** Over 300 random
relabellings and quad orders of the dataset above (measured 2026-10-22; each
implementation's own random sequence):

| Implementation | Canonical forms | Split |
|---|---:|---|
| Varve (this package) | 2 | 155 / 145 |
| pyoxigraph 0.5.11, `RDFC_1_0` | 2 — the same two, byte for byte | 151 / 149 |
| dotNetRDF 3.5.2, `RdfCanonicalizer("SHA256")` | 2 — the same two, byte for byte | 157 / 143 |

This is a second gap in the Recommendation, not a case of #36's: no graph name
is blank, and no position is hidden. Tracked as
[#142](https://github.com/Hafeok/Varve/issues/142), beside #36, under ADR
0038, D2: `spec-gap`; #142 carries the report. `RdfCanonicaliser` stays on
RDFC-1.0 as written and does not hash the graph itself.

### 6.3 Where this package claims the converse

**Where every quad that mentions a blank node is in one graph — the default
graph or one IRI — and no graph name is blank.** There every relating quad has
the same graph, so §6.2's omission loses nothing, and §6.1's quad cannot occur.
It is a class in which no counterexample is known and neither mechanism
applies. §7's property asserts the converse there by test, not by proof.

**Known limit, stated for callers.** Outside that class, equal canonical forms
still mean isomorphic datasets and the output is deterministic, but
isomorphic datasets may have different canonical forms, and so may one
dataset read in two orders. A caller comparing such datasets must not rely on
canonical inequality, and a caller that needs stable bytes across sources must
not rely on canonical form. The callers in this repository:

- **The management plan** (`ManagementPlan.CanonicalLines`, the diff of
  `apply --plan`) canonicalises one graph at a time, and management
  declarations refuse blank nodes. Inside the class.
- **The SHACL report digest** (ADR 0173, `ReportGraph`) canonicalises the
  report as triples in the default graph, with every data blank node replaced
  by a fixed IRI first. Inside the class.
- **The conformance harness** (`DatasetComparison`, ADR 0059) can compare
  datasets with blank nodes across named graphs. There a §6.2 split shows as
  the comparisons disagreeing, which fails the case naming both verdicts: a
  false failure, never a false pass, and this section is where its
  explanation is. No suite case meets it today.
- **Entity export** (#107, planned) promises RDFC-1.0 labels and
  byte-deterministic fixtures of a boundary that can span named graphs.
  Determinism from one source holds; equal bytes for the same entity read from
  two sources, or a stale-fixture check by bytes, do not, outside the class.
  Its design has to compare by isomorphism there or keep the boundary inside
  the class.

## 7. Tests, and the gate

- **The W3C suite**, `w3c/rdf-canon` as a second submodule
  (`tests/w3c/rdf-canon`, pinned, ADR 0007's pattern): every
  `RDFC10EvalTest` compares the canonical form with the expected file byte
  for byte; every `RDFC10MapTest` compares the issued identifiers map with
  the expected JSON; every `RDFC10NegativeEvalTest` must fail with
  `CanonicalisationLimitException` under the default limit. `rdfc:hashAlgorithm`
  selects the hash. The inputs are read with `Varve.Turtle`'s N-Quads reader.
  Guard counts — 64 evaluation cases, 21 map cases, one negative, two of them
  on SHA-384 — and the ratchet.
- **Isomorphism as a property.** For generated datasets and generated
  relabellings and reorderings of them, with one quad changed or removed in
  some: in §6.3's class, `iso(A, B) ⇔ canon(A) = canon(B)`, with `iso` the
  harness's backtracking check — which is therefore also that check's
  differential test; for any dataset, blank graph names and blank nodes
  across graphs included, canonicalisation terminates under the default work
  limit, gives the same bytes twice, and `canon(A) = canon(B) ⇒ iso(A, B)`,
  and the pairs where RDFC-1.0 separates isomorphic datasets are counted and
  reported. The generators count isomorphic and non-isomorphic pairs, and
  both must occur. §6.1's and §6.2's counterexamples are pinned as tests.
- **Idempotence**: `canon(canon(A)) = canon(A)`, reading the canonical form
  back with `Varve.Turtle`, in §6.3's class — outside it, it inherits §6's
  shortfall, since the canonical form is a relabelling.
- **The canonical form parses, and the writers agree**: every canonical
  output read back with `Varve.Turtle`'s N-Quads reader gives the dataset it
  was computed from, up to the issued relabelling; and writing what was read
  with `Varve.Turtle`'s canonical writer gives the output back byte for byte
  (ADR 0061). Case folding cannot show there, since the output is already
  lowercase; RDF 1.2's `c14n` suites and `Varve.Turtle`'s own tests hold it.
- **SHA-384 in the browser** is measured by the browser smoke app (ADR 0028's
  table gains the row).

## 8. Open questions

1. **Triple terms containing blank nodes** (§3.1): refused until RDF 1.2
   says how RDFC applies to them.
2. ~~**`n-triples.md`'s canonical form** (§4) — whether it becomes RDF
   1.2's.~~ Decided: it does, and 1.2 wins where 1.1 differs (ADR 0061).
3. ~~**§6's counterexample** — whether and how to raise it with the RDF &
   SPARQL Working Group.~~ Decided on the 5c report: raised, with the minimal
   pair (#36), and recorded here as a known limit. §6.2's case is a
   separate report, #142.
