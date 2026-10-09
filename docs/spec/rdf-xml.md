# RDF/XML

What `Varve.RdfXml` reads and writes, under which specification, and the
places where the specification leaves a choice and this package makes one.
Written at milestone 6b (#10), against the suites named in §1. ADR 0111 is the
decision; this page is what it decided, in enough detail that a reader of the
code can check it line by line.

## 1. Normative references

- **RDF 1.2 XML Syntax**, W3C Working Draft of 09 October 2026: the grammar
  (§6), `rdf:parseType="Triple"` (§6.2.19), `rdf:annotation` and
  `rdf:annotationNodeID`, base directions through `its:dir` (§5.1.9), and
  `rdf:version` (§3.1).
- **RDF 1.1 XML Syntax**, W3C Recommendation of 25 February 2014, where the two
  agree: `rdf:ID` uniqueness (§5.3), the reserved `xml*` attribute names
  (§6.1.4), the exclusive canonicalisation of `rdf:parseType="Literal"`
  content (§7.2.17).
- **Extensible Markup Language 1.0 (Fifth Edition)** and **Namespaces in XML
  1.0 (Third Edition)**, done by `System.Xml.XmlReader` and not by this
  package.
- **Exclusive XML Canonicalization 1.0**, W3C Recommendation of 18 July 2002,
  with comments, for an `rdf:XMLLiteral`'s lexical form.
- **RDF 1.2 Concepts**, for the triple term, `rdf:reifies` and the two base
  directions, which `Varve.Rdf` holds.

The suites, from the pinned `w3c/rdf-tests` submodule (revision `369a90d`,
2026-08-28):

| Suite | Manifest | Entries | Of which |
|---|---|---:|---|
| `rdf11/rdf-xml` | `rdf/rdf11/rdf-xml/manifest.ttl` | **166** | 126 `TestXMLEval`, 40 `TestXMLNegativeSyntax` |
| `rdf12/rdf-xml` | `rdf/rdf12/rdf-xml/eval/manifest.ttl` | **31** | 29 `TestXMLEval`, 2 `TestXMLNegativeSyntax` |

The `rdf11` manifest lists seven more entries commented out upstream
(`rdfms-empty-property-elements-error003`, `-test003`, `-test009`,
`rdfms-xml-literal-namespaces-test001` and `-002`, `rdfms-xmllang-test001` and
`-002`); the harness reads the manifest as the W3C publishes it, so they are
not entries and are not counted. The pinned count in `SubmoduleGuardTests` is
the 166 and the 31 above, and both suites pass in full with **no exemption**.

## 2. Grammar

The RDF/XML grammar (§6 of RDF 1.2 XML) is a recursive descent in
`RdfXmlReaderCore` over the events `XmlReader` reports. The productions, in
the order the reader meets them:

- **`doc`**: an optional `rdf:RDF` root; a document whose root is a node
  element itself is read as that one node element (§6.1.2 admits it).
- **`nodeElementList`** and **`nodeElement`**: `rdf:Description` or a typed
  node element, with at most one of `rdf:ID`, `rdf:about`, `rdf:nodeID`, or
  none for a fresh blank node; `rdf:type` as an attribute; and every other
  namespaced attribute as a **property attribute** giving a plain literal.
- **`propertyEltList`** and the forms of **`propertyElt`**, decided by the
  attributes and content of the element: a **resource** form (one node
  element child), a **literal** form (text only), `rdf:parseType="Literal"`
  (the content canonicalised, §2 below), `"Resource"` (a fresh blank node with
  the content as its property elements), `"Collection"` (an `rdf:first` /
  `rdf:rest` list ending in `rdf:nil`), `"Triple"` (one triple read as a
  **triple term**), and the **empty** form, whose object is `rdf:resource`,
  `rdf:nodeID`, a fresh blank node carrying its property attributes, or the
  empty plain literal.
- **`rdf:li`** is numbered `rdf:_1`, `rdf:_2`, … per node element, and the
  counter is the element's.
- **`rdf:ID` on a property element reifies**: the four `rdf:Statement` triples
  are emitted after the triple itself.
- **`rdf:annotation="iri"`** and **`rdf:annotationNodeID="name"`** on a property
  element emit `<r> rdf:reifies <<( s p o )>>` after the triple.
- **`xml:base`**, **`xml:lang`**, **`its:dir`** and **`rdf:version`** are
  scoped by element, from the root down, and are the only attributes `rdf:RDF`
  takes besides namespace declarations.

### Points the grammar makes that are easy to get wrong

- **Property attributes in the RDF namespace are property attributes.** Only
  the core syntax terms (`rdf:RDF`, `rdf:ID`, `rdf:about`, `rdf:parseType`,
  `rdf:resource`, `rdf:nodeID`, `rdf:datatype`, `rdf:version`), `rdf:li` and
  the three withdrawn terms are excluded (§6.2.7). `rdf:value="v"`, `rdf:_3="3"`
  and even `rdf:Seq="string"` on a node element each emit a triple
  (`rdfms-rdf-names-use/test-032`, `warn-003`,
  `rdf-containers-syntax-vs-schema/test006`).
- **An attribute with no namespace whose name begins `xml`, in any case, is
  XML's own and is ignored** (§6.1.4 of RDF 1.1 XML:
  `unrecognised-xml-attributes/test002` has `xmlnewthing`). Every other
  attribute with no namespace is an error, because it has no IRI.
- **The withdrawn terms** `rdf:aboutEach`, `rdf:aboutEachPrefix` and
  `rdf:bagID` are errors wherever they appear (`rdfms-abouteach`,
  `rdf-containers-syntax-vs-schema/error001`).
- **`rdf:ID` is unique per base**: the same ID twice against the same base is
  `DuplicateId`; the same ID under two different `xml:base`s is two IRIs
  (`rdfms-difference-between-ID-and-about`, `xmlbase`).
- **A fresh blank node is labelled with a number alone**, which no
  `rdf:nodeID` can spell because an `NCName` cannot begin with a digit, so a
  document's own labels and the reader's never collide.
- **The RDF 1.2 forms are gated by `rdf:version`.** A `rdf:parseType="Triple"`
  element is read as a triple term only where `rdf:version` in scope announces
  1.2 (any value but `1.0` or `1.1`); otherwise the element is **skipped with
  its content** and emits nothing, which is what `rdf12-xml-tt-01` ("Ignored
  triple term") expects: its expected N-Triples file is deliberately empty. An
  `its:dir` likewise applies only under a 1.2 version; without one the literal
  keeps its language and drops the direction (`rdf12-xml-dir-02`, "Language
  with direction and no RDF version"). **An annotation is not gated**:
  `rdf12-xml-an-01` has no version and expects the reifier. The reader follows
  the suite in all three.
- **A direction without a language is no term** (RDF 1.2 Concepts §3.3,
  RDF 1.2 XML §5.1.9): the literal is then a plain `xsd:string`.
- **`rdf:parseType="Triple"` wants exactly one triple**, from exactly one node
  element whose property is a single property element, and takes no `rdf:ID`,
  `rdf:annotation` or `rdf:annotationNodeID` (§6.2.19). Anything else is
  `InvalidTripleTerm`.
- **An `rdf:XMLLiteral`'s lexical form is the exclusive canonical form** of the
  element's content, with comments: attributes sorted by namespace then local
  name, namespaces rendered where first visibly used and not before, and
  nothing inherited from outside the literal, which is what makes the
  canonicalisation exclusive. The lists that sort the attributes and track
  the rendered namespaces are the one allocation in proportion to a literal,
  and a literal is not a quad.

## 3. Base, language and namespaces

`RdfXmlOptions.BaseIri` is the document's retrieval IRI, against which a
relative reference and every `rdf:ID` is resolved until an `xml:base` says
otherwise; **empty means there is none**, and then a relative reference is
`RelativeIri` rather than something silently accepted. An `xml:base` that is
itself relative resolves against the base in scope. The values of `xml:base`
and `xml:lang` are copied out of the arena when met, because they outlive the
top-level node element the arena is reset for (decision
`ScopeAttributesAreCopied`); a scope attribute happens once per element, not
once per triple.

Every IRI is checked against RFC 3987 and required to have a scheme once
resolved (`InvalidIri`), unless `ValidateIris` is false. The reader validates
by default because the suite's negative cases need it and because an IRI a
later store cannot hold is better refused at the boundary.

Each namespace declaration the reader meets is reported once through
`RdfXmlOptions.OnNamespace`, prefix and IRI, in document order — the same
contract `TurtleOptions.OnPrefix` has, so a caller writing Turtle from
RDF/XML can carry the prefixes across.

## 4. Position reporting

An `RdfXmlParseError` has a **kind**, a **line**, a **column** and a message.
Line and column are `XmlReader`'s, 1-based, the column counted in **UTF-16
characters** of the decoded document after line-end normalisation. **There
is no byte offset**, because the reader has decoded the document's encoding
before this package sees a character, and a byte offset computed from a
character count would be wrong for every multi-byte character before the
error (ADR 0111, "Byte offsets by re-encoding"). The other Varve syntaxes
report a byte offset because they own their bytes; this one says so rather
than guess.

The kinds are one per way a document can be wrong at the RDF level and one
for the XML beneath it: `MalformedXml` (which includes a DTD, refused),
`UnexpectedEnd`, `ForbiddenElementName`, `ForbiddenAttribute`,
`ConflictingAttributes`, `InvalidName`, `DuplicateId`, `RelativeIri`,
`InvalidIri`, `UnexpectedText`, `UnexpectedContent`, `InvalidLanguageTag`,
`InvalidBaseDirection`, `InvalidTripleTerm`.

## 5. Errors end the parse

**RDF/XML has no recovery unit.** The first error ends the parse, the result
carries it, and the triples handed out before it stand; `QuadCount` says how
many. Turtle resumes after the next `.` at depth zero (ADR 0030) because a
Turtle document has a statement structure to resume at; an ill-formed XML
document has no well-defined continuation, and `XmlReader` itself stops at the
first well-formedness error. Inventing a continuation at the RDF level above a
reader that has none would report triples nobody can trust.

## 6. Writing

`RdfXmlWriter` writes over `System.Xml.XmlWriter` into the caller's
`IBufferWriter<byte>`: UTF-8, no byte order mark, indented unless
`RdfXmlWriteOptions.Indent` is false (the compact form denotes the same graph;
whitespace between elements is not content).

- **The root** is `rdf:RDF`, with `xmlns:rdf` first and then every prefix the
  caller declared through `DeclarePrefix` before the first triple, in
  declaration order; a prefix declared after the root is written is used from
  then on and declared on the element that first uses it. A prefix that is
  reserved (`xml*`) or not an `NCName` is refused as an `ArgumentException`.
- **Consecutive triples of one subject share an `rdf:Description`**; a change
  of subject closes it. The writer does not reorder, so a sorted input gives
  one description per subject and an unsorted one does not; **writing what
  was written reproduces it byte for byte** either way, which the fixed-point
  property asserts.
- **A subject** is `rdf:about` for an IRI and `rdf:nodeID` for a blank node.
  A blank node label that is not an `NCName` (the reader's own are numbers)
  is written with a `b` in front, and the reader reads it back as a different
  label for the same node, which isomorphism does not see.
- **A predicate** is split into namespace and local name at the last
  character after which the rest is an `NCName`; the namespace's prefix is
  the caller's or an invented `nsN`, which `XmlWriter` declares on the element
  that first uses it. The split is done once per distinct predicate and
  cached by its bytes (`PredicateNamesAreCached`).
- **An object**: `rdf:resource` for an IRI, `rdf:nodeID` for a blank node,
  text for a literal with `xml:lang`, `its:dir` and `rdf:datatype` as the
  term has them. An `rdf:XMLLiteral` is written as **a typed literal with its
  lexical form as text**, not as `rdf:parseType="Literal"`, so that the round
  trip holds without the writer canonicalising. A triple term is
  `rdf:parseType="Triple"` around one `rdf:Description`.
- **`rdf:version="1.2"`** is put on the description of the first triple that
  needs it — a directional literal or a triple term — and the description is
  reopened with the attribute if the open one lacks it. The reader gates
  those forms on the attribute (§2), so a document the writer emits always
  reads back as what was written.

**What the writer cannot spell it refuses by name**, as an
`InvalidOperationException` naming the term, and never writes a document
that reads back as something else:

| Refused | Why |
|---|---|
| A quad with a graph label | RDF/XML has no graphs; a dataset wants TriG or N-Quads |
| A triple term as subject or predicate | §6.2.19 admits a triple term in the object position only |
| A predicate IRI with no `NCName` suffix (`<http://a/123>`, `<http://a/p/>`, `<http://a/#>`) | A property element is a qualified name, and the local part must be an `NCName` |
| A term holding a character XML 1.0 cannot carry (`U+0001`, …) | No escape exists for it in XML 1.0 |

The round trip, as a property over generated datasets with IRIs, blank nodes,
plain, language-tagged, directional and typed literals and triple terms
(`PropertyTests`): **write, read back, same dataset under a relabelling of
blank nodes** (2,000 iterations), **write, read, write, same bytes** (1,000
iterations), and **every refusal names its term** (500 and 200 iterations).
The corpus half is `WriterFixedPointTests` over every positive case of both
suites.

## 7. Streaming and allocation

**The honest row first.** This is the one Varve reader whose figure per quad is
a bound and not zero, and the bound is `System.Xml`'s:

| | Bytes per triple, measured | Bound asserted | Fixed cost, bytes |
|---|---:|---:|---:|
| Reader, `XmlReader` included | **16** | 24 | 28,056 |
| Writer, `XmlWriter` included | **34** | 48 | 46,896 |

Measured 2026-10-09 by `AllocationTests` as the difference between a 64- and a
640-description document of six triples each (a typed node, a plain literal,
a language-tagged literal, a typed literal, a resource, a nested blank node),
.NET 10, Release, x64, so that the harness's own cost cancels. **This
package's own code allocates nothing per triple**: element and attribute
names are the reader's atomised strings, compared by reference and equality;
every value is read in chunks with `ReadValueChunk` into one UTF-8 arena that
is reset once per top-level node element; fresh blank node labels are numbers
in the arena; the writer's predicate names are cached once per distinct
predicate. The per-triple bytes are the reader's and the writer's own, per
element, and `[HotPath]` still stands on the hot methods because it is what
makes the analyzer refuse every allocation that *is* this package's. The
allow-list entries that admit `XmlReader`, `IXmlLineInfo`, `XmlNodeType`,
`XmlWriter` and `String`'s comparison members are each a decision key
(`XmlReaderAdmittedOnTheHotPath`, `StringNamesAreCompared`).

Per document, the reader keeps the set of `rdf:ID`s §5.3 requires to be unique
(`IdsAreKeptAsStrings`, bounded by the IDs), the scope stack's copied
`xml:base` and `xml:lang` values, and the capture buffer of one
`rdf:parseType="Triple"` element at a time.

**The chunk-boundary oracle** runs over both suites: the same document is fed
through the `ReadOnlySequence<byte>` entry point split at every offset (or at
a stride above 4 KB), and the output and the reported position must be what
the whole document gives. The sequence reaches `XmlReader` through a one-way
stream adapter (`StreamAdaptersAreOneWay`), so the oracle is checking the
adapter and the reader's buffering, not a scanner of this package's; the
positions are the reader's and do not move with the split.

**A quad's terms are spans valid for the duration of the callback**, as in
every Varve reader; the view is a `ref struct` and cannot be stored.

**The browser already carried `System.Private.Xml`** for the SPARQL XML
results format; this package adds its own assembly, 72,790 bytes compressed,
measured by the browser smoke. The figures and the size at which the decision
is reopened (1.5 MB compressed) are in ADR 0111.

## 8. What this replaced

Until this milestone, the SPARQL suites' RDF/XML files were read from
N-Triples translations under `tests/fixtures/w3c-rdfxml/`, generated once by
dotNetRDF with the SHA-256 of each original (ADR 0027, note of 2026-09-25).
The directory, its guard test and the benchmark project's `convert-rdfxml`
command are deleted; `EvaluationData` parses a `.rdf` file with
`RdfXmlParser`, and the 501 evaluation entries the SPARQL suites contribute
are read from their originals.

## 9. Open questions

None. The hand-written XML reader ADR 0111 rejected is a revisit with a
stated trigger, not an open question.
