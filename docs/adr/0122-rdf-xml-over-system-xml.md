# 0122 — RDF/XML over `System.Xml`, with its cost measured and a size at which to revisit

## Status

**Accepted — filed unaccepted by milestone 6b of #10, 2026-10-09** (ADR 0066).
Decided by the maintainer on the 6b plan: "yes, both costs recorded, with a
revisit size in the ADR". Acceptance is the maintainer's act on the pull
request.

**Closes the dated note in ADR [0027](0027-benchmarking.md)** of 2026-09-25:
the N-Triples translations under `tests/fixtures/w3c-rdfxml/` are deleted and
the conformance harness reads the SPARQL suites' RDF/XML originals with
`Varve.RdfXml`.

Written against **RDF 1.2 XML Syntax, W3C Working Draft of 09 October 2026**
(§6 the grammar, §6.2.19 `rdf:parseType="Triple"`, §5.1.9 base directions,
§3.1 `rdf:version`) and **RDF 1.1 XML Syntax**, W3C Recommendation of 25
February 2014, where the two agree. The suites are `rdf/rdf11/rdf-xml` and
`rdf/rdf12/rdf-xml/eval` in the pinned `w3c/rdf-tests` submodule, revision
`369a90d` of 28 August 2026.

## Context

`Varve.RdfXml` has been a named layer-2 package since the layering table was
written (`AGENTS.md`), and milestone 5b left a debt in its name: fourteen
SPARQL evaluation suites carry their data or their expected results as
RDF/XML, and because no Varve package read it, dotNetRDF translated those
files once into N-Triples committed under `tests/fixtures/w3c-rdfxml/` with
the SHA-256 of each original (ADR 0027, amendment of 2026-09-25). The
amendment set its own exit: "the fixtures are deleted when `Varve.RdfXml`
passes its own suite, at which point the harness reads the originals". This
is that milestone.

Every other Varve syntax package owns its lexer to the byte. N-Triples,
Turtle and TriG are grammars a few pages long whose tokens are the RDF terms
themselves, and the constraints — zero allocation per quad, correct
positions at any chunk boundary, Native AOT, the browser — are met by a
hand-written scanner over `ReadOnlySequence<byte>` with nothing beneath it
(ADRs 0030, 0061). RDF/XML is different in kind: beneath its grammar sits
**XML 1.0 with Namespaces**, which is encodings, the five predefined and any
declared entities, character references, CDATA sections, attribute-value
normalisation, namespace scoping, the restricted character ranges and the
well-formedness constraints, every one of them something the W3C suite
exercises (`rdf-charmod-*`, `amp-in-url`, `xml-canon`, `rdfms-xml-literal-*`).
Writing that is writing an XML parser, and the question this ADR answers is
whether to.

Two constraints pull against each other. **Constraint 5** says allocation per
quad is a defect, and the hot-path analyzer (VARVE0003) refuses, in a
`[HotPath]` method, any type not on its allow-list; `System.Xml` is not on it,
and the reader it would admit allocates by design — every name is an
atomised `string`, every text node a `string` unless read in chunks. **The
brief's honesty rule** (`AGENTS.md`: "measure honestly; the first row is the
one that costs") says a figure is stated, not hidden behind a wrapper. The
browser adds a third pull: `System.Private.Xml` is a large assembly that the
WASM build has not needed so far, and the published size is part of what the
browser smoke guards.

## Decision

### `System.Xml` does the XML; this package does RDF/XML

**The reader is `System.Xml.XmlReader`** created over a `Stream` with
`DtdProcessing.Prohibit`, no `XmlResolver`, `CheckCharacters` on, and
comments and processing instructions **kept** (an `rdf:parseType="Literal"`
canonicalises them, so they cannot be dropped beneath it). The RDF/XML
grammar — node elements, property elements in their five forms, `rdf:li`
numbering, collections, reification by `rdf:ID`, `rdf:parseType="Resource"`,
`"Collection"`, `"Literal"` and `"Triple"`, annotations, `xml:base`,
`xml:lang`, `its:dir`, `rdf:version` — is a recursive descent over the events
the reader reports, in `RdfXmlReaderCore`. The three entry points (memory, a
`ReadOnlySequence<byte>`, a `Stream`) all meet the reader as a `Stream`,
through one-way adapters that honour the one direction they exist for and
throw `NotSupportedException` from the others, which is `Stream`'s own
contract for a non-seekable stream (DD0012, the `Compatibility` scope).

**The writer is `System.Xml.XmlWriter`** over the caller's
`IBufferWriter<byte>`: UTF-8 without a byte order mark, indented by default,
and it is `XmlWriter` that escapes, declares namespaces and refuses the
characters XML 1.0 cannot carry.

**The reader's positions are the positions.** An error reports the 1-based
line and 1-based column `IXmlLineInfo` gives, counted in UTF-16 characters
after the reader has decoded the document's encoding, and **no byte offset**,
because this package never sees a byte of the document; the rule is stated in
`rdf-xml.md` §4 rather than imitated badly. The chunk-boundary oracle still
runs over both RDF/XML suites: a split sequence feeds the same reader
through the sequence adapter, and the oracle asserts that the output and the
reported position are the same at every split, which is what the adapter owes.

### The cost is measured and stated, not hidden

`System.Xml.XmlReader`, `IXmlLineInfo`, `XmlNodeType` and `XmlWriter` are
**added to the VARVE0003 allow-list by type**, and `String`'s `Equals`,
equality operators and `Length` **by member**, so that the hot methods can
compare the reader's atomised names without the analyzer treating a `string`
comparison as an allocation (it is not one). What the package's own code
allocates per triple is **nothing**: element and attribute names are the
reader's atomised strings, compared by reference and equality; every value is
read in chunks with `ReadValueChunk` into one arena of UTF-8 that is reset
once per top-level node element; blank node labels are numbers written into
the arena; the only `string` kept per document is the set of `rdf:ID`s that
§5.3 of RDF 1.1 XML requires to be unique, bounded by the IDs and not the
triples. What `XmlReader` and `XmlWriter` allocate per element is theirs,
and it is the figure this ADR states:

| Direction | Bytes per triple, measured | Bound asserted | Fixed cost |
|---|---:|---:|---:|
| Reader, `XmlReader` included | **16** | 24 | 28,056 |
| Writer, `XmlWriter` included | **34** | 48 | 46,896 |

Measured 2026-10-09 by `tests/Varve.RdfXml.Tests/AllocationTests.cs` as the
difference between a 64- and a 640-description document of six triples each,
.NET 10, Release, x64, so that the harness's own cost cancels. The bound is
set with headroom over the measurement so that an unrelated runtime change
does not teach everyone to raise it, and is tightened when the measurement
moves down. This is the one Varve reader whose per-quad figure is a bound and
not zero, and `rdf-xml.md` §7 says so in its first row; the `[HotPath]`
attribute still stands on the reader's hot methods because it is what makes
the analyzer refuse every allocation that *is* this package's.

### The browser pays for `System.Private.Xml`, up to a stated size

`Varve.RdfXml` is built for the browser like every layer-2 package, and the
browser smoke parses an RDF/XML document. **Measured 2026-10-09** by building
`tests/Varve.WasmSmoke` with and without the two milestone 6b packages, .NET
10 Release, `_framework/` gzip-compressed file by file: `System.Private.Xml`
**was already in the bundle** — `Varve.Sparql.Results` reads and writes the
SPARQL XML results format through it, 3,096,345 bytes trimmed, 1,049,734
compressed — so RDF/XML adds no BCL assembly. What the two packages add is
themselves: `Varve.RdfXml.wasm` 258,837 bytes (72,790 compressed) and
`Varve.JsonLd.wasm` 295,701 bytes (88,439 compressed), with their symbol
files; the bundle grows from 11,717,386 to 12,154,221 bytes compressed,
**436,835 bytes**, 3.7 %. **The revisit size is 1.5 MB compressed** of
growth attributable to RDF/XML. A measured growth above it, which a future
runtime that drops `System.Private.Xml` from the results package could
produce, reopens the alternative this ADR rejected — a hand-written XML
reader restricted to what RDF/XML needs — as its own ADR, because at that
size the browser would be paying for the two thirds of `System.Private.Xml`
(XSD validation, XSLT, XPath, serialisation) that RDF/XML never touches.

### Two decisions of the reader's own, where the specification leaves a choice

**An `rdf:XMLLiteral`'s lexical form is its content in Exclusive XML
Canonicalization 1.0 with comments**, written from the reader's events as RDF
1.1 XML §7.2.17 requires, and it is the one place a list is allocated in
proportion to the input: the attributes of each element are sorted and the
namespaces rendered so far are tracked. A literal is not a quad, so the cost
is per literal and bounded by the literal.

**The RDF 1.2 forms are read where `rdf:version` in scope announces 1.2**,
and not otherwise: a `rdf:parseType="Triple"` element without it is skipped
with its content, and an `its:dir` without it is dropped from the literal,
which is what the `rdf12/rdf-xml` suite expects (`tt-01`, "Ignored triple
term", whose expected file is **deliberately empty**; `dir-02`, "Language with
direction and no RDF version"). An annotation (`rdf:annotation`,
`rdf:annotationNodeID`) is read whether or not a version is announced, which
is also what the suite expects (`an-01` has no version and expects the
reifier). The three cases are in `rdf-xml.md` §2 as the pitfalls they are.

### The writer refuses what it cannot spell, by name

RDF/XML has no graph, cannot put a triple term anywhere but the object, and
spells a predicate as a qualified name whose local part must be an `NCName`.
The writer refuses each — a quad with a graph label, a triple term as subject
or predicate, a predicate IRI with no `NCName` suffix, a term holding a
character XML 1.0 cannot carry — with an `InvalidOperationException` naming
the term, and never mangles one into something that reads back differently.
Where the syntax leaves it no choice it invents: a namespace prefix `nsN` for
a predicate namespace the caller did not declare, and a `b` before a blank
node label that is not an `NCName` (the reader's own labels are numbers, which
no `rdf:nodeID` can spell, so the two never collide). An `rdf:XMLLiteral` is
written as a typed literal with its lexical form as text, not as
`rdf:parseType="Literal"`, so that the round trip holds without the writer
having to canonicalise.

## Alternatives considered

- **A hand-written XML reader restricted to what RDF/XML needs.** The one
  alternative with a real case: it would be zero-allocation per triple like
  every other Varve reader, would report byte offsets, would cost the browser
  nothing it does not already pay, and would be under VARVE0003 without an
  allow-list entry. Rejected for now on size and risk: RDF/XML's XML is not a
  subset — `rdf:parseType="Literal"` needs comments, processing instructions,
  CDATA, namespace scoping and exclusive canonicalisation; the charmod suite
  needs every encoding declaration the XML spec admits; the well-formedness
  constraints are the negative half of the suite. A second XML parser in the
  .NET ecosystem that gets those right is a project, and a wrong one reports
  confident wrong triples. The decision is explicitly **revisitable at a
  stated size**, above, rather than closed.
- **`XDocument` or `XmlDocument`.** Simpler to write the grammar over a tree.
  Rejected: both materialise the document, which is an allocation in
  proportion to the input before the first triple, and neither streams.
- **`XmlReader` with `ReadContentAsString` and strings throughout.** The
  obvious shape and the one most RDF/XML readers have. Rejected because it
  makes the per-triple figure a `string` per literal plus a `string` per IRI,
  which would be hiding the cost the brief says to measure; reading values in
  chunks into an arena keeps this package's own figure at zero and leaves only
  the reader's.
- **A new recovery unit, as Turtle has.** Rejected: an ill-formed XML document
  has no well-defined continuation, and the reader itself stops at the first
  well-formedness error. The first error ends the parse; the triples handed
  out before it stand, and the result says how many.
- **Byte offsets by re-encoding the reader's positions.** It would give the
  error struct the same three fields every other syntax has. Rejected because
  it would be a guess: the reader has decoded the document's encoding and
  normalised its line ends before this package sees a character, and a byte
  offset computed from a character count is wrong for every multi-byte
  character before the error. A field that is sometimes wrong is worse than a
  field that is absent and documented.

## Consequences

**The allow-list has its first BCL I/O type on it, and the reason is written
down.** The entries are by type for `System.Xml` and by member for `String`,
and each is a decision key the analyzer can cite (`XmlReaderAdmittedOnTheHotPath`,
`StringNamesAreCompared`). The place this could erode is a later package
taking the entries as precedent for `System.Text.Json`'s document model or a
`StreamReader`; it is not one, and the JSON-LD reader of the same milestone
(ADR 0123) is over `Utf8JsonReader`, which allocates nothing.

**`tests/fixtures/w3c-rdfxml/` is gone, with its guard and its generator.**
The conformance harness reads the SPARQL suites' `.rdf` files with
`RdfXmlParser`, the `convert-rdfxml` command left the benchmark project, and
dotNetRDF is back to the one use ADR 0027 left it: the benchmark baseline.

**The browser carries `System.Private.Xml` and the smoke watches its size**, with
the number at which this is reopened written here rather than remembered.

**RDF 1.2 in RDF/XML is gated by a declaration, which no other Varve syntax
is.** Turtle (ADR 0121) and N-Triples read the 1.2 forms unconditionally and
use the version only to decide one escape. RDF/XML's suite decides otherwise
for triple terms and directions, and this ADR follows the suite; a document
that wants them says `rdf:version="1.2"`, and the writer puts that attribute
on every description that needs it.

## Checks

- **Checked against the accepted ADRs** (0001–0109). Touches **0027** (its
  dated note of 2026-09-25 closed by a dated note in that file), **0009**
  (no package added: `System.Xml` is the BCL), **0061** (the N-Triples
  rendering of a triple term and a directional literal is what the round-trip
  tests compare), and **0066** (filed unaccepted; `CS0618` red by design until
  acceptance). No conflict with any.
- **Layer ownership.** `Varve.RdfXml` is layer 2 and references `Varve.Rdf`
  and `Varve.Iri` only. Nothing in `Varve.Rdf` changes.
- **Analyzer rule.** None new. VARVE0003's allow-list gains the four
  `System.Xml` types and four `String` members, each justified by a decision
  key in `docs/decisions/rdf-xml-over-system-xml.md`.
- **Open questions owned.** One, stated with its trigger: the hand-written
  reader, reopened above 1.5 MB compressed of browser growth.
