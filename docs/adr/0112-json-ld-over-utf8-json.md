# 0112 — JSON-LD 1.1 over `Utf8JsonReader`, as a tree in an arena; the direction is RDF 1.2's by default

## Status

**Accepted — filed unaccepted by milestone 6b of #10, 2026-10-09** (ADR 0066).
Decided by the maintainer on the 6b plan, with one reversal of the plan's
proposal: the default for `@direction` is Varve's native directional
literal, and the specification's modes and its default are options.
Acceptance is the maintainer's act on the pull request.

Written against **JSON-LD 1.1**, W3C Recommendation of 16 July 2020, and
**JSON-LD 1.1 Processing Algorithms and API**, W3C Recommendation of 16 July
2020 (§4 context processing, §5 expansion, §8 RDF serialization and
deserialization, §9 the error codes), with **RFC 8785** for the lexical form
of `rdf:JSON` and **RDF 1.2 Concepts** for the directional language-tagged
string. The suites are `toRdf`, `expand` and `fromRdf` of the
**json-ld-api** repository, pinned as the third submodule at
`tests/w3c/json-ld-api`, revision `5551473` of 7 October 2026.

## Context

`Varve.JsonLd` has been a named layer-2 package since the layering table was
written, and nothing read JSON-LD in Varve. JSON-LD is the syntax the web
speaks: schema.org, the activity streams, every API that returns linked data
as JSON. It is also unlike every other Varve syntax in one way that decides
the whole design: **a JSON-LD document is not a sequence of statements**. A
term's meaning comes from a `@context` that may be declared after it, in a
parent, in a type-scoped or property-scoped context, or in a remote document;
the expansion algorithm (§5.1) is defined over the whole object, orders keys,
rewrites values and lifts nested entries; and the result is a tree from which
the quads are read. Nothing can be emitted before the last byte of the
document has been seen, whichever way the bytes arrive.

Every other Varve reader is a scanner over `ReadOnlySequence<byte>` that
emits a quad per statement with zero allocation per quad (ADRs 0030, 0061,
0111). The brief holds JSON-LD to the same constraints: zero allocation per
quad measured honestly, the chunk-boundary oracle, Native AOT and the
browser, the DD rules from the first line, and "no `JsonDocument` on the hot
path". The brief also fixes the scope — `toRdf`, `fromRdf`, `expand`; not
compaction, flattening or framing; remote contexts only through a
caller-supplied loader — and asks that the departure on `@direction` be
stated.

## Decision

### The document is a tree in pooled arrays, read by `Utf8JsonReader`

**`Utf8JsonReader` does the JSON** — the grammar, the escapes, the chunk
boundaries of a `ReadOnlySequence<byte>` — and the package reads what it
reports into **its own tree**: index-linked nodes in pooled arrays, every
string unescaped into one UTF-8 arena, every member name interned to a small
integer by its bytes, so that a keyword, a term and a member name are
compared as integers and no `string` is made for them
(`Utf8JsonReaderBuildsTheTree`). `JsonDocument` and `JsonNode` are not used:
they are an object per node, and they are not the shape the expansion
algorithm wants to write into. **The expansion algorithm writes its result as
new nodes of the same tree** (`ExpansionWritesIntoTheTree`), the tree is
reset per document, and the processor that owns the tree, its name table
and its arenas is **kept per thread** and reused, as `ArrayPool` keeps its
buffers (`TheProcessorIsKeptPerThread`), so that a steady state of documents
allocates only what grows.

`Utf8JsonReader`, `Utf8JsonWriter`, their token, state and option types, and
`ReadOnlySequence<byte>` with `BuffersExtensions` **join the VARVE0003
allow-list by type** (`Utf8JsonTypesAdmittedOnTheHotPath`): the reader and
the writer are the BCL's and allocate nothing per token the package does not
ask for, and the sequence is the caller's input.

The hot path marked `[HotPath]` is the tree, the name table, the tree
reader and writer, the quad emitter and the fromRdf writer's per-quad
members. **Context processing and expansion are not marked hot**: an active
context and its term definitions are objects allocated once per `@context`
met, scoped contexts included, as a prefix directive is in Turtle
(`ContextsAreNotPerQuad`), and expansion is per node, not per quad. The
figure per quad excludes them and `json-ld.md` §7 says so.

### The cost is measured as a difference, and the honest row is the first

| Path | Bytes per quad, steady state | Bound asserted | Fixed cost, bytes |
|---|---:|---:|---:|
| toRdf (read, expand, emit) | **0** | 64 | 14,400 |
| expand (read, expand, write JSON) | **0** | 64 | 2,080 |
| fromRdf (buffer, serialise, write JSON) | **286** | 512 | 56,536 |

Measured 2026-10-09 by `tests/Varve.JsonLd.Tests/AllocationTests.cs` as the
difference between a 64- and a 640-node document of six quads each, .NET 10,
Release, x64. The zero of the two reading paths is a **steady state**: the
per-thread processor keeps the arrays a document grew, so the second
document of a size costs nothing, and the first costs the growth of the tree
in proportion to the input — which is the honest statement, and the one the
spec page makes first. The fromRdf writer costs **286 bytes per quad**
because it must buffer the whole dataset before it can detect a list or
group a subject (`FromRdfBuffersTheDataset`): quads as indices into one term
arena, terms interned by their bytes, node objects as tree nodes. That is a
bound and not zero, stated rather than hidden; the emitter of the toRdf path
allocates nothing per quad in any state.

### `@direction` is RDF 1.2's by default; the specification's modes are options

The specification's `rdfDirection` option has three values — `null` (drop
the direction), `i18n-datatype`, `compound-literal` — and `null` is its
default, because RDF 1.1 had no direction. Varve's model has had the
directional language-tagged string since milestone 3a, so **the default is
`RdfDirection.Native`**: a value with `@language` and `@direction` becomes
`"x"@en--rtl`, and a value with a direction and no language becomes a plain
string, because RDF 1.2 Concepts has no direction without a language. The
specification's three are `RdfDirection.None`, `I18nDatatype` and
`CompoundLiteral`, and the suite runs each entry under its stated option
and under `None` where it states none, which is what the suite's expected
files assume. This is a departure from the specification's default,
decided by the maintainer on the plan, and stated in `json-ld.md` §5
(`DirectionDefaultIsNative`).

### What the processor is, and is not

**JSON-LD 1.1 only.** There is no `json-ld-1.0` processing mode:
`@version: 1.1` is accepted and never conflicts, and the suite's 1.0-only
cases — `specVersion: json-ld-1.0` (behaviour 1.1 changed),
`processingMode: json-ld-1.0` (the mode this processor does not have) — and
its `produceGeneralizedRdf` cases (Varve emits RDF, not generalized RDF, so a
blank node predicate is left out) **are excluded by a stated rule rather than
exempted one by one** (`ProcessingModeIsOnePointOne`): 19 of toRdf's 467, 18
of expand's 386, 1 of fromRdf's 54, the rule in the harness and the figures
in `json-ld.md` §1. Every other case passes with no exemption.

**Remote contexts come only through the caller's `JsonLdDocumentLoader`**
(`RemoteContextsNeedALoader`): with none, the default, a remote `@context` or
`@import` is the error `loading remote context failed`, and nothing in this
package opens a connection. Up to 32 remote contexts are followed per
document (`context overflow` beyond), a context including itself is
`recursive context inclusion`, and a loaded context is cached per document.
The harness supplies a loader rooted at the submodule.

**Compaction, flattening and framing are not implemented**
(`CompactFlattenAndFrameAreOut`). They are the inverse direction — JSON for
people, from RDF — and they need the inverse context and the framing
algorithm, which is a milestone of its own; `fromRdf` writes expanded JSON-LD,
which every JSON-LD processor compacts. The HTML (`script` element) and
`remote-doc` suites are likewise out: the first is a document loader's
business, the second an HTTP client's.

### Errors are the specification's codes, and the first ends the parse

`JsonLdErrorCode` is the specification's `JsonLdErrorCode` enumeration, one
value per code with its text (`ErrorsAreTheSpecificationsCodes`), so that a
caller and the W3C suite name the same thing; the result carries the first
error and the quads handed out before it stand (`TheFirstErrorEndsTheParse`).
There is no position: a context error belongs to a term, not to a byte, and
the expansion that finds it may be far from where the term was written.

### Three smaller decisions the specification leaves open

- **Blank nodes are relabelled** `_:b0`, `_:b1`, … in order of first
  appearance, a document's own labels included, as the specification's node
  map does (`BlankNodesAreRelabelled`); the dataset is the same up to
  isomorphism and the labels never collide with the generated ones.
- **An `rdf:JSON` literal's lexical form is RFC 8785** — members sorted by
  UTF-16 code unit, no whitespace, ECMAScript number formatting
  (`JsonLiteralsAreCanonical`); the sort and the number formatting are the
  one allocation in proportion to a JSON literal, which is not a quad. The
  suite's `useJCS` entries are the only form Varve writes.
- **Numbers are XSD canonical** (`NumbersAreXsdCanonical`): a JSON number
  with a non-zero fraction, or at or beyond 1e21, or typed `xsd:double`, is
  an `xsd:double` in the canonical form `Varve.Xsd` formats (`1.5E0`); any
  other is an `xsd:integer`, however it was spelt (`1.0` and `-0.0` are `1`
  and `0`). **Language tags are lowercased** on the way through
  (`LanguageTagsAreLowercased`), as the specification permits and its
  expected files assume.

## Alternatives considered

- **A streaming JSON-LD reader.** It would make the figure per quad zero in
  every state and let the first quad out before the last byte. Rejected
  because the language does not allow it: a `@context` may follow the keys it
  defines, a type-scoped context changes the meaning of sibling keys, and
  `@nest`, `@reverse`, `@included` and the index containers restructure the
  object. A streaming subset would read some documents and silently misread
  others, which is worse than reading all of them with a bound.
- **`JsonDocument` as the tree.** The obvious shape, and "no `JsonDocument`
  on the hot path" rules it out: it is an object graph the expansion
  algorithm cannot write into, so expansion would build a second one, and
  every string is a `string`.
- **`JsonNode` (the mutable DOM).** Expansion could write into it. Rejected
  for the same reason as `JsonDocument` and one more: it is an object per
  node and a `string` per value, which is the per-quad allocation the brief
  measures.
- **Strings for member names and term definitions.** Simpler to write and
  the shape every other JSON-LD processor has. Rejected because the keys of a
  JSON-LD document are its vocabulary and would be a `string` per member per
  document; interning them by bytes to integers makes every keyword and term
  comparison an integer comparison and costs one entry per distinct name.
- **The specification's `rdfDirection: null` as the default.** Faithful to
  the letter and lossy: it drops a direction RDF 1.2 can carry and Varve's
  model has carried since milestone 3a. The maintainer reversed the plan's
  proposal to keep it, and the departure is stated.
- **Exempting the 1.0 and generalized-RDF cases one by one.** The ratchet
  allows it. Rejected because they are not failures of this processor but
  cases for a processor it is not; a rule names the reason once, and a
  submodule bump that changes the count is seen by the guard.
- **Compaction now.** It is half of the API. Deferred, not refused: it is the
  inverse context and framing, a milestone's work, and `fromRdf` writes a
  document every processor compacts.

## Consequences

**Varve has a reader whose figure per quad is a steady-state zero and a
first-document growth**, and says which is which. The emitter is zero in
every state; the tree is what grows; the writer is a bound.

**A third submodule.** `tests/w3c/json-ld-api` joins `rdf-tests` and
`rdf-canon`, pinned, guarded by count, with the exclusion rule's figures
pinned beside the enumerated ones.

**The suite's expected documents are compared by its own rule**, the JSON-LD
object comparison: members in any order, arrays as sets except under
`@list`, language tags without regard to case. The harness implements it
over `System.Text.Json`, which is test code and may.

**The browser carries `System.Text.Json`**, which it already did for the
SPARQL results formats; the smoke parses a JSON-LD document.

**The direction default is Varve's and not the specification's**, and anyone
reading JSON-LD into Varve and writing N-Quads will see `--rtl` where another
processor would show nothing. The option is there for them; the default is
the one that loses nothing.

## Checks

- **Checked against the accepted ADRs** (0001–0111). Touches **0009** (no
  package added: `System.Text.Json` is the BCL), **0061** (the canonical
  N-Quads rendering of directional literals is what the round-trip tests
  compare), **0110** (the `RdfVersion` option is Turtle's; JSON-LD has no
  edition), **0111** (the allow-list gains BCL types for the same reason and
  under the same honesty rule), and **0066** (filed unaccepted; `CS0618` red
  by design until acceptance). No conflict with any.
- **Layer ownership.** `Varve.JsonLd` is layer 2 and references `Varve.Rdf`,
  `Varve.Iri` and `Varve.Xsd` only. Nothing in `Varve.Rdf` changes.
- **Analyzer rule.** None new. VARVE0003's allow-list gains the six
  `System.Text.Json` types and the two `System.Buffers` types, each justified
  by a decision key in `docs/decisions/json-ld-over-utf8-json.md`.
- **Open questions owned.** Two, each with its trigger stated in
  `json-ld.md` §9: compaction, flattening and framing when a milestone wants
  JSON for people; a streaming subset if a caller ever needs the first quad
  before the last byte, which no one has.
