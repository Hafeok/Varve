# JSON-LD

What `Varve.JsonLd` reads and writes, under which specification, and the
places where the specification leaves a choice and this package makes one.
Written at milestone 6b (#10), against the suites named in §1. ADR 0112 is
the decision; this page is what it decided, in enough detail that a reader
of the code can check it line by line.

## 1. Normative references

- **JSON-LD 1.1**, W3C Recommendation of 16 July 2020: the syntax, the
  keywords, node, value, list, set and graph objects.
- **JSON-LD 1.1 Processing Algorithms and API**, W3C Recommendation of 16
  July 2020: context processing (§4.1), create term definition (§4.2),
  expansion (§5.1), IRI expansion (§5.2), value expansion (§5.3),
  serialize RDF as JSON-LD (§8.1), RDF to object (§8.2), deserialize JSON-LD
  to RDF (§8.3), object to RDF (§8.6), list to RDF (§8.5), the error codes
  (§9.2) and the API options (§6.1).
- **RFC 8785**, JSON Canonicalization Scheme, for the lexical form of an
  `rdf:JSON` literal.
- **RFC 3987** for IRIs and **RFC 3986 §5.2** for resolution, done by
  `Varve.Iri`.
- **RDF 1.2 Concepts** for the directional language-tagged string, which
  `Varve.Rdf` holds.

The suites, from the pinned `w3c/json-ld-api` submodule (revision `5551473`,
2026-10-07), `tests/` of the repository:

| Suite | Manifest | Listed | Run | Excluded by rule |
|---|---|---:|---:|---:|
| `json-ld/toRdf` | `toRdf-manifest.jsonld` | 467 | **448** | 19 |
| `json-ld/expand` | `expand-manifest.jsonld` | 386 | **368** | 18 |
| `json-ld/fromRdf` | `fromRdf-manifest.jsonld` | 54 | **53** | 1 |

**The rule** (ADR 0112, `ProcessingModeIsOnePointOne`): an entry whose
`specVersion` is `json-ld-1.0` tests behaviour JSON-LD 1.1 changed; an entry
whose `processingMode` is `json-ld-1.0` tests the 1.0 mode this processor
does not have; an entry with `produceGeneralizedRdf` asks for a blank node
predicate, which Varve does not emit. Those 38 are not entries of the suites
as wired, and `SubmoduleGuardTests` pins both the number run and the number
excluded, so that a submodule bump that moves either is seen. Every entry
run passes with **no exemption**.

Each entry runs under **its stated options**: `base`, `expandContext`,
`rdfDirection`, `useNativeTypes`, `useRdfType`; where an entry states no
`rdfDirection`, it runs under `RdfDirection.None`, the specification's
default, which is what its expected file assumes (§5). The document loader
the suite runs with reads a published test IRI from the submodule and
nothing else (§3). The `toRdf` results are compared as datasets up to
isomorphism; `expand` and `fromRdf` results by the suite's JSON-LD object
comparison — members in any order, arrays as sets except under `@list`,
language tags without regard to case — and a negative entry by the
specification's error code (§4).

## 2. Expansion

`JsonLdExpander.Expand` writes the expanded document (§5.1) as JSON;
`JsonLdParser.Parse` expands and then deserializes to RDF (§8.3) without
writing it. The algorithms are the specification's, over the tree of ADR
0112, with these points made explicit:

- **Keys are processed in lexicographical order**, which the algorithm
  allows and which makes the output a function of the document and not of
  its member order. Arrays keep the document's order, `@type` included.
- **`@nest`** lifts a nested object's members into the node, with the
  nesting key as the active property, so that a property-scoped context on
  a term aliasing `@nest` applies to what it nests (`expand-c037`, `c038`),
  recursively.
- **`@included`** holds node objects only; a value that expands to null, a
  string or a list is `invalid @included value`.
- **A `@graph` container wraps every value in a graph object**, a graph
  object included (`expand-0081`); only the `@graph` + `@id` and `@graph` +
  `@index` maps ask first.
- **`@id` shaped like a keyword** (`@ignoreMe`) expands to null and is kept
  as JSON null in the expanded document (`expand-0122`); the toRdf path
  emits nothing for such a node.
- **Free-floating values are dropped**: at the top level or under `@graph`,
  a value object, a list object, a lone `@id` and a scalar are not nodes.
- **A reverse term with an `@index` container may name its index property**
  (`expand-0131`), as a forward term may.
- **`@type` may be redefined** only to add `@container: @set` or
  `@protected`; an empty redefinition is `keyword redefinition`.
- **A term with a slash** (`./something`) is expanded against the active
  context alone when its IRI is computed, not against the local context
  that is defining it.

## 3. Contexts

**An active context is an object**, allocated once per `@context` met —
scoped contexts, type-scoped contexts and `@import` included — and its term
definitions likewise (ADR 0112, `ContextsAreNotPerQuad`). Every IRI in it
is a range of the tree's text; no `string` is made for a term or a key.

**Remote contexts come only through `JsonLdOptions.DocumentLoader`**: a
delegate from an IRI to a document's bytes. With none, the default, a
remote `@context` string or `@import` is `loading remote context failed`.
A loaded document must be an object with a `@context`, else `invalid remote
context`; it is cached per document by IRI; up to 32 are followed per
document, beyond which `context overflow`; one that includes itself is
`recursive context inclusion`. Relative IRIs in a remote context resolve
against the IRI it was asked for.

`JsonLdOptions.BaseIri` is the document's IRI, against which `@id`, `@type`
and `@base` values resolve; empty means there is none and a relative IRI
stays relative, which the toRdf path then leaves out as not well-formed.
`JsonLdOptions.ExpandContext` is a context applied before the document's
own (the specification's `expandContext`): a JSON document holding
`@context`, or the context value itself, with `ExpandContextIri` for its
relative references.

## 4. Errors

`JsonLdResult.Error` is a `JsonLdError`: the specification's code
(`JsonLdErrorCode`, one value per code of §9.2, its text through
`JsonLdErrorCodes.Text`) and a message naming the term, IRI or value. **The
first error ends the processing** and the quads handed out before it stand
(`QuadCount` says how many): JSON-LD has no recovery unit, because a term's
meaning is the context's and an ill-formed context has no continuation.
**There is no position**: a context error belongs to a term, not to a byte,
and the expansion that finds it may be far from where the term was written.

A document that is not JSON is `loading document failed`; an `rdf:JSON`
literal that is not JSON, met by the fromRdf writer, is `invalid JSON
literal`, refused by name as an `InvalidOperationException`.

## 5. Deserialization to RDF

The toRdf path walks the expanded tree and hands each quad to the handler as
it is found (§8.3–§8.6), which gives the same dataset as the specification's
node map does, as a set: a document that states a triple twice may hand it
over twice.

- **Only well-formed statements are emitted.** A relative IRI as subject,
  predicate or object, a blank node as predicate (generalized RDF is not
  produced) and a literal with an ill-formed language tag (`toRdf-wf05`)
  each leave their statement out; the nodes beneath them are still walked.
- **Blank nodes are relabelled** `_:b0`, `_:b1`, … in order of first
  appearance, a document's own labels included (`BlankNodesAreRelabelled`).
- **Lists** are chains of fresh blank nodes through `rdf:first` and
  `rdf:rest`, the empty list `rdf:nil`; a list inside a list is a chain
  whose `rdf:first` is a chain.
- **Numbers**: a JSON number with a non-zero fraction, or at or beyond
  1e21, or typed `xsd:double`, is an `xsd:double` in the XSD canonical form
  (`1.5E0`, `1.0E21`); any other is an `xsd:integer` however it was spelt
  (`1.0`, `1E2` and `-0.0` are `1`, `100` and `0`). A boolean is an
  `xsd:boolean`. A string with no `@type` and no `@language` is an
  `xsd:string`.
- **An `rdf:JSON` literal** (`@type: @json`) has the value's RFC 8785 form
  as its lexical form: members sorted by UTF-16 code unit, no whitespace,
  strings escaped only where JSON requires, numbers as ECMAScript's
  `Number::toString` (`1e+21`, `0.1`, `100`). This is the `useJCS` the
  suite's entries ask for, and the only form Varve writes.
- **A direction crosses into RDF by `JsonLdOptions.RdfDirection`**
  (`DirectionDefaultIsNative`):

| `RdfDirection` | `{"@value": "x", "@language": "ar", "@direction": "rtl"}` | `{"@value": "y", "@direction": "rtl"}` |
|---|---|---|
| `Native` (default) | `"x"@ar--rtl` | `"y"` |
| `None` (the specification's default) | `"x"@ar` | `"y"` |
| `I18nDatatype` | `"x"^^<https://www.w3.org/ns/i18n#ar_rtl>` | `"y"^^<https://www.w3.org/ns/i18n#_rtl>` |
| `CompoundLiteral` | `_:b` with `rdf:value "x"`, `rdf:language "ar"`, `rdf:direction "rtl"` | `_:b` with `rdf:value "y"`, `rdf:direction "rtl"` |

  The default departs from the specification's: RDF 1.2 has the directional
  language-tagged string and Varve's model has carried it since milestone
  3a, so dropping the direction would lose what the document said. A
  direction without a language is a plain string under `Native`, because RDF
  1.2 Concepts has no direction without a language. The suite runs under
  each entry's stated option and under `None` where it states none.
- **Language tags are lowercased** on the way through
  (`LanguageTagsAreLowercased`), as the specification permits.

## 6. Writing

`JsonLdWriter` writes an RDF dataset as expanded JSON-LD (§8.1), the
`fromRdf` of the API, into the caller's `IBufferWriter<byte>` through
`Utf8JsonWriter`: UTF-8, non-ASCII text as it is, indented unless
`JsonLdWriteOptions.Indent` is false. The whole dataset must be seen before
a list can be detected or a subject grouped, so quads are buffered and the
document is written on `Flush` or `Dispose`, once
(`FromRdfBuffersTheDataset`).

- **One node object per subject**, `@id` first, members in UTF-16 order,
  values in the order their quads arrived without duplicates; `rdf:type`
  with a node object becomes `@type` unless `UseRdfType`.
- **Subjects are ordered**: IRIs first in lexicographical order, then blank
  nodes in the order they were first a subject. Blank node labels are not
  ordered by their text because a reader renumbers them: this order is the
  one a reader of the document reproduces, which is what makes the fixed
  point below hold.
- **A named graph** hangs off the node of its name in the default graph as
  `@graph`, its nodes ordered likewise; a node that is only an `@id` is
  omitted at either level.
- **A well-formed list** — a chain of blank nodes each with one `rdf:first`
  and one `rdf:rest`, at most `@type: [rdf:List]` besides, each referenced
  exactly once in the whole dataset, ending in `rdf:nil` — becomes a
  `@list`; "referenced once" is counted over the dataset, so a list node two
  graphs share is not a list (`fromRdf-0020`). Lists of lists are lists.
- **A literal** is `{"@value": lexical}` with `@type` for a datatype other
  than `xsd:string`, `@language` (lowercased) for a tag, and `@direction`
  for RDF 1.2's direction under `RdfDirection.Native`; under
  `I18nDatatype` an `i18n` datatype is read back as language and direction,
  under `CompoundLiteral` a blank node with `rdf:value` and `rdf:direction`
  is folded into one value, and under `None` neither is.
- **`UseNativeTypes`**: `xsd:integer`, `xsd:double` and `xsd:boolean`
  literals in their lexical space become JSON numbers and booleans (`"1"`
  and `"0"` are booleans); one outside it stays a typed string
  (`fromRdf-0027`). An `rdf:JSON` literal becomes its JSON value with
  `@type: @json` always.

**What the writer cannot spell it refuses by name**, as an
`InvalidOperationException`: a quad with a triple term (JSON-LD has none), and
an `rdf:JSON` literal whose lexical form is not JSON.

The round trip, as a property over generated datasets with IRIs, blank
nodes, named graphs, plain, language-tagged, directional, typed, boolean,
integer, double and JSON literals (`PropertyTests`): **write, read back, same
dataset under a bijection of blank node labels** (2,000 iterations), **write,
read, write, same bytes up to the blank node labels the reader renumbers**
(1,000 iterations), and **a triple term is refused by name** (200 iterations).
The corpus half is `WriterFixedPointTests` over every positive entry of the
toRdf suite: its quads written as JSON-LD read back as the same dataset, and
written again give the same bytes up to the labels.

## 7. Streaming and allocation

**The honest row first.** A JSON-LD document is a tree before it is a
dataset, so the figure per quad is the tree's growth, not the emitter's:

| Path | Bytes per quad, steady state | Bound asserted | Fixed cost, bytes |
|---|---:|---:|---:|
| toRdf: read, expand, emit | **0** | 64 | 14,400 |
| expand: read, expand, write | **0** | 64 | 2,080 |
| fromRdf: buffer, serialise, write | **286** | 512 | 56,536 |

Measured 2026-10-09 by `AllocationTests` as the difference between a 64- and
a 640-node document of six quads each (a type, a plain literal, a
language-tagged literal, a typed literal, an IRI, a nested blank node), .NET
10, Release, x64, so that the harness's own cost cancels.

- **The reading paths are zero in a steady state**: the processor — name
  table, tree, arenas, context processor, emitter — is kept per thread and
  reset per document (`TheProcessorIsKeptPerThread`), so the second document
  of a size costs nothing per quad. **The first document of a size costs the
  tree's growth**, in proportion to the input, through `ArrayPool`. The quad
  emitter allocates nothing in any state: terms are spans of the tree's
  text copied into one `TermArena` per quad, blank node labels numbers
  written into it. What does allocate, per `@context` met and never per
  quad, is the active context and its term definitions
  (`ContextsAreNotPerQuad`), and per `rdf:JSON` literal its canonical form
  (`JsonLiteralsAreCanonical`).
- **The fromRdf writer is a bound, 286 bytes per quad**, because it buffers
  the dataset: quads as indices into one term arena, terms interned by their
  bytes, node objects and value objects as tree nodes, usage records for
  list detection, and the written document. Its arrays are rented and
  returned on `Dispose`.
- **The allow-list** admits `Utf8JsonReader`, `Utf8JsonWriter`,
  `JsonTokenType`, `JsonReaderState`, `JsonReaderOptions`,
  `JsonWriterOptions`, `ReadOnlySequence<byte>` and `BuffersExtensions` by
  type (`Utf8JsonTypesAdmittedOnTheHotPath`). `[HotPath]` stands on the
  tree, the name table, the tree reader and writer, the emitter and the
  writer's per-quad members; context processing and expansion are not
  marked hot and are cited by `ContextsAreNotPerQuad`.

**Nothing is emitted before the last byte**, whichever entry point is used:
`Parse(ReadOnlyMemory<byte>)`, `Parse(in ReadOnlySequence<byte>)` and
`Parse(Stream)`, the last reading the whole stream into a pooled buffer first
(`TheWholeStreamIsRead`). **The chunk-boundary oracle** runs over the toRdf
suite: the same document fed as a sequence split at every offset gives the
same quads and the same error, which `Utf8JsonReader` owes across its
segments and this package owes above it.

**A quad's terms are spans valid for the duration of the callback**, as in
every Varve reader (`TheQuadHandlerIsTheCallers`).

## 8. Out of scope, and why

- **Compaction, flattening and framing** (`CompactFlattenAndFrameAreOut`):
  the inverse direction, needing the inverse context and the framing
  algorithm, a milestone of its own. `fromRdf` writes expanded JSON-LD,
  which every JSON-LD processor compacts.
- **Fetching remote contexts**: a document loader is the caller's; the
  package opens no connection.
- **The HTML and `remote-doc` suites**: a `script` element is a document
  loader's business and HTTP behaviour an HTTP client's.
- **JSON-LD 1.0 mode and generalized RDF**: §1.

## 9. Open questions

Two, each with its trigger. **Compaction** when a milestone wants JSON for
people out of Varve, with the inverse context as its first piece. **A
streaming subset** if a caller ever needs the first quad before the last
byte, which no one has asked for and which the language does not generally
allow (ADR 0112, alternatives).
