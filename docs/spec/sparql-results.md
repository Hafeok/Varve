# SPARQL result formats

Functional specification for `Varve.Sparql.Results` (layer 2): reading and
writing the four formats in which SPARQL results travel. Milestone 5b built
**the readers**, because the evaluation suite's expected results come in
these forms; milestone 5c built **the writers** (§5).

Status: Accepted. Changes only together with the ADR that motivates the change.

## 1. Normative references

- **SPARQL Query Results XML Format (Second Edition)**, W3C Recommendation,
  21 March 2013 — `application/sparql-results+xml`, `.srx`.
- **SPARQL 1.1 Query Results JSON Format**, W3C Recommendation, 21 March 2013
  — `application/sparql-results+json`, `.srj`.
- **SPARQL 1.1 Query Results CSV and TSV Formats**, W3C Recommendation,
  21 March 2013 — `.csv`, `.tsv`.
- **SPARQL 1.2 Query Results XML, JSON, CSV and TSV Formats**, Working Drafts
  of 2026, for their two additions only: **triple terms** (`<triple>` in XML,
  `"type": "triple"` in JSON, `<<( s p o )>>` in CSV and TSV) and **base
  direction** (`its:dir` in XML and JSON, `--ltr` / `--rtl` in TSV). As
  checked on 2026-09-25: the JSON draft of 13 August 2026 (§3.2.2), the
  CSV and TSV draft of 23 July 2026 (§3.2, §4.2), and the XML draft's §2.3.1.
- **RFC 4180** for the CSV record and quoting rules the CSV format cites.
- **Extensible Markup Language (XML) 1.0**, §2 (well-formedness), for the
  subset §3.1 reads; **RFC 8259** for JSON, through the BCL's
  `Utf8JsonReader`.
- `sparql-grammar.md` §3.2 for the escapes the TSV reader decodes, which are
  Turtle's (the TSV format says terms are "encoded in Turtle syntax").

## 2. The reader

One reader type for the four formats, pull-based, over UTF-8:

```csharp
var reader = new SparqlResultsReader(utf8, SparqlResultsFormat.Json);
if (reader.ReadHead())                  // variables, or a boolean
{
    while (reader.Read())               // one solution
    {
        SolutionView solution = reader.Current;
        if (solution.TryGet(0, out RdfTermView term)) { … }
    }
}
if (reader.Error.IsError) { … reader.Error.Position … }
```

- **Input** is `ReadOnlySequence<byte>` (or `ReadOnlyMemory<byte>`), so a
  document that arrived in chunks is read without being joined. A token that
  straddles two segments is assembled in the reader's scratch buffer.
- **`ReadHead`** reads to the end of the head. After it, `Variables` lists the
  variables in the order the document declares them, and `IsBoolean` says
  whether the document is an `ASK` result, whose value is `Boolean`.
- **`Read`** advances to the next solution. `Current` is a `SolutionView`, a
  `ref struct` valid until the next `Read`, whose `TryGet(i, out
  RdfTermView)` gives the binding of the `i`-th variable, or false when it is
  unbound. A caller that keeps a term calls `Materialise()` on the view (ADR
  0024). Terms are built in a `TermArena` the reader owns and resets per
  solution, so reading allocates nothing per solution once the arena has
  grown to the largest one.
- **Errors** stop the reader. `Error` holds the first, with its kind and its
  **position**: byte offset, 1-based line, and 1-based byte column, as in
  `sparql-grammar.md` §6. There is no recovery: a malformed result document
  has no useful remainder.
- **The reader holds no pooled buffers** and so is not disposable: its arena
  and builders are its own and grow to the largest solution, and the
  allocation test (§4) checks that nothing else is allocated per solution.

## 3. The formats

### 3.1 XML

A **non-validating reader of the subset of XML the format uses**, written for
this package rather than taken from `System.Xml`: the format needs elements,
attributes, character data with the five predefined entities and numeric
character references, `CDATA` sections, comments, processing instructions
(skipped) and namespace declarations; it needs no DTD, and a document with a
`<!DOCTYPE` is refused (which is also what keeps entity expansion out). Two
reasons for not using `XmlReader`: it reports positions in UTF-16 characters
rather than bytes, and it brings `System.Private.Xml` into every browser
build that links this package.

Elements are matched by **namespace and local name**: the results namespace
`http://www.w3.org/2005/sparql-results#` for the format's own elements, the
XML namespace for `xml:lang`, and `http://www.w3.org/2005/11/its` for
`its:dir`. `<head>` holds `<variable name="…"/>` and `<link href="…"/>`
(read and ignored); then either `<boolean>` or `<results>` with one
`<result>` per solution and one `<binding name="…">` per bound variable,
holding one of `<uri>`, `<bnode>`, `<literal>` (with `xml:lang`, `its:dir`,
or `datatype`), or `<triple>` with `<subject>`, `<predicate>`, `<object>`.
Whitespace between elements is ignored; the text of `<uri>`, `<bnode>` and
`<literal>` is taken exactly. A binding for an undeclared variable, or two
for one variable in one result, is an error.

### 3.2 JSON

`Utf8JsonReader` over the sequence, with the byte position of an error taken
from its `BytesConsumed` and the line and column computed from the bytes read
so far. `head.vars` gives the variables; `boolean` or `results.bindings`
the body, whichever appears — members may come in any order, including
`results` before `head`, in which case the bindings are held until the head
is read. A term object is `type` (`uri`, `bnode`, `literal`, the legacy
`typed-literal`, or `triple`), `value`, and for a literal `xml:lang`,
`its:dir` and `datatype`; for a triple, `value` is an object with
`subject`, `predicate` and `object`. Members of a term object may come in any
order.

### 3.3 TSV

The first line is the variables, each written `?name`; each further line is
one solution, fields separated by tab, an empty field unbound. A field is an
RDF term in Turtle syntax: `<iri>`, `_:label`, a quoted literal with
`@lang`, `@lang--dir` or `^^<datatype>`, a bare number (`1`, `1.5`, `1e0`,
typed `xsd:integer`, `xsd:decimal` and `xsd:double` as Turtle does) or
`true`/`false`, or a triple term `<<( s p o )>>`. String escapes are
Turtle's `ECHAR` and `UCHAR`. Lines end with LF or CRLF; a final line ending
is optional. `Varve.Sparql.Results` is at layer 2 beside `Varve.Turtle` and may
not reference it, so the term reader here is its own.

### 3.4 CSV

RFC 4180 records: the first is the variable names (without `?`), each further
one a solution. **CSV is lossy by design**: the format writes an IRI as its
text, a literal as its lexical form, and a blank node as `_:label`, with no
datatype or language. The reader therefore returns a field beginning `_:` as a
blank node, an empty field as unbound, and **every other field as a simple
literal** holding the text — it does not guess which fields were IRIs. A
comparison against CSV results compares lexical forms (`sparql-evaluation.md`
§12.1).

## 4. Tests

- **The corpus.** Every result document in the pinned `sparql/` tree — 508
  files of the four extensions — reads without error, and a result that
  exists as both `.srx` and `.srj` reads to the same solutions. The count is
  pinned, so a submodule bump that changes it is looked at.
- **The chunk-boundary oracle** (`docs/testing.md` §2), as for every
  streaming reader, over the same corpus: each document read whole and read
  as a sequence split at every byte offset gives the same solutions, and a
  malformed one the same error at the same position. It found, on its first
  run, a JSON binding name split across segments read as empty.
- **Positions.** Malformed documents of each format, with the expected byte
  offset, line and column of the error stated in the test.
- **Allocation.** Reading a document of many solutions allocates a bounded
  amount independent of the number of solutions, beyond the arena's growth.

## 5. The writers

One writer type for the four formats, push-based, writing UTF-8:

```csharp
using var writer = new SparqlResultsWriter(output, SparqlResultsFormat.Json);
writer.WriteHead(["s", "o"]);            // or writer.WriteBoolean(true), and nothing else
foreach (…)
{
    writer.StartSolution();
    writer.WriteBinding(0, term);        // an RdfTerm, or an RdfTermView
    writer.WriteBinding(1, view);        // an unwritten variable is unbound
    writer.EndSolution();
}
writer.WriteEnd();
await writer.FlushAsync(cancellationToken);
```

- **Output** is an `IBufferWriter<byte>`, written as the calls are made, or
  a `Stream`, which the writer buffers in a pooled buffer and writes on
  `Flush` or `FlushAsync`. `BytesPending` says how much is buffered, so a
  caller writing to a network stream flushes asynchronously when it chooses;
  nothing is written to a stream synchronously except by `Flush`, because a
  host such as ASP.NET Core forbids synchronous I/O on its response body.
  Disposing returns the buffer; disposing without `WriteEnd` leaves the
  document incomplete, which is the caller's to avoid.
- **The row is the caller's.** A binding is given by variable index, as an
  `RdfTerm` or as an `RdfTermView` — the reader's own view type, so a reader
  can be copied into a writer without materialising a term — and bindings of
  one solution are given in ascending index order, because CSV and TSV are
  positional. The writer keeps nothing per solution: it allocates nothing per
  row beyond what the caller's row costs, which the allocation test of §6
  asserts as zero bytes per solution.
- **The call sequence is checked**: `WriteHead` or `WriteBoolean` once and
  first, bindings only inside a solution, indexes in range and ascending,
  `WriteEnd` last; a call out of order throws `InvalidOperationException`,
  and a term the format cannot carry throws `ArgumentException` naming the
  rule (below).
- **Blank node labels are written as given.** A label is scoped to the
  document (XML §2.3.1, JSON §3.2.2, CSV §3.2, TSV §4.2), so the caller
  chooses them, and the store's labels (ADR 0044) are already unique within
  one dataset.

### 5.1 XML — SPARQL Query Results XML Format §2

The declaration `<?xml version="1.0"?>`, then `<sparql>` in the results
namespace (§2.1), `<head>` with one `<variable name="…"/>` per variable
(§2.2), and either `<boolean>true</boolean>` (§2.3.2) or `<results>` with one
`<result>` per solution and one `<binding name="…">` per bound variable
(§2.3.1). Terms as §2.3.1: `<uri>`, `<bnode>`, `<literal>` with `xml:lang`,
or `datatype` for a datatype other than `xsd:string`. 1.2: `its:dir` on a
directional literal, with `xmlns:its` declared on
`<sparql>` — written on every document, because the root is written before
the writer knows whether a directional literal will follow, and `its:version`
is optional (draft §2.3.1) — and
`<triple>` with `<subject>`, `<predicate>`, `<object>`, recursively. Text
escapes `&`, `<` and `>`; attribute values also `"`. XML 1.0 cannot carry
U+0000 or the other characters outside its `Char` production (XML 1.0 §2.2),
even as a character reference, so such a term throws `ArgumentException`.

### 5.2 JSON — SPARQL 1.1 Query Results JSON Format

`{"head":{"vars":[…]},"results":{"bindings":[…]}}` (§2, §3.1.1, §3.2.1), or
`{"head":{},"boolean":true}` (§4). A term is an object with `type` and
`value`, and `xml:lang` or `datatype` (§3.2.2); `datatype` is omitted for
`xsd:string`; a blank node's `value` is its label without `_:`. 1.2:
`its:dir`, and `{"type":"triple","value":{"subject":…,"predicate":…,"object":…}}`
(draft §3.2.2). Written by hand rather than with `Utf8JsonWriter`, whose
default encoder escapes every non-ASCII character: strings escape `"`, `\`
and U+0000–U+001F (RFC 8259 §7), with `\b`, `\f`, `\n`, `\r`, `\t` where
they exist and `\u00XX` otherwise, and nothing else. No whitespace is written.

### 5.3 CSV — SPARQL 1.1 Query Results CSV and TSV Formats §3

The variable names without `?`, then one record per solution; an unbound
variable is an empty field (§3.1). A field is the term's string value
(§3.2): an IRI's text, a literal's lexical form, a blank node's `_:label`.
1.2: a triple term is `<<( s p o )>>` with its parts written recursively the
same way and separated by single spaces (draft §3.2); **a base direction is
not written**, because the draft says nothing about it and CSV writes no
language tag either. A field containing `"`, `,`, LF or CR is quoted with
`""` for `"` (§3.2, RFC 4180 §2 rules 6–7). **Records end with CRLF**, RFC
4180 §2 rule 1, whose record rules the format adopts; the W3C expected
files end their lines with LF, which is why the writer check of §6 compares
lines. Boolean results have no CSV form in the Recommendation; the writer
writes the header `_askResult` and one record, `true` or `false`, which is
the convention Oxigraph and Jena share and the reader here does not read
back as a boolean (§3.4 has no boolean form either). *(Oxigraph is the
tie-breaker where the specification is silent; said here, not matched
silently.)*

### 5.4 TSV — SPARQL 1.1 Query Results CSV and TSV Formats §4

The variables as `?name`, tab-separated; one line per solution; an unbound
variable is an empty field; lines end with LF, including the last (§4.1).
A term in Turtle syntax without the triple-quoted forms (§4, §4.2):
`<iri>`, `_:label`, `"…"` with `@lang`, `@lang--dir` (1.2), or `^^<datatype>`
for a datatype other than `xsd:string`, strings escaping `"`, `\`, LF, CR
and tab with `ECHAR` and nothing else; and a literal of `xsd:integer`,
`xsd:decimal`, `xsd:double` or `xsd:boolean` whose lexical form matches
Turtle's `INTEGER`, `DECIMAL`, `DOUBLE` or `BooleanLiteral` exactly is
written bare, as the §4.3 example and the suite's expected files write it.
1.2: `<<( s p o )>>` (draft §4.2). Boolean results are the header `?_askResult`
and one line, by the same convention as CSV.

### 5.5 What each format cannot carry

| Term | XML | JSON | CSV | TSV |
|---|---|---|---|---|
| A character outside XML 1.0 `Char` | refused | written | written | written |
| Language tag, datatype | written | written | **dropped** | written |
| Base direction | written | written | **dropped** | written |
| IRI versus literal | written | written | **dropped** | written |
| Unbound versus `""` | written | written | **same field** | written |
| Triple term | written | written | written, as text | written |

CSV is lossy by the format's own statement (§3.2); nothing else is.

## 6. Tests of the writers

- **The round trip, as a property.** For generated solution sequences —
  IRIs with non-ASCII characters, blank nodes, simple, language-tagged,
  directional and typed literals with every character class a string escape
  touches, nested triple terms, and unbound variables — writing then reading
  with this package's reader gives the same solutions, term for term, for XML,
  JSON and TSV. For CSV it gives what CSV carries: each term replaced by the
  simple literal of its string value, a blank node kept, and an unbound
  variable and an empty string both empty. XML skips the characters it
  cannot carry. Booleans likewise, in every format.
- **The W3C cases.** For each `csv-tsv-res` and `json-res` case, the query is
  evaluated over the store and its results written in the case's format;
  for TSV and CSV the written lines equal the expected file's lines up to a
  bijection of blank node labels and, for CSV, the line ending (§5.3); for
  JSON, whose whitespace is not canonical, reading the written document and
  the expected one gives the same solutions. The report says which cases are
  byte-equal and which re-parsed.
- **Allocation.** Writing many solutions of views allocates nothing per
  solution, measured as a difference (`docs/testing.md` §4).
- **Call order.** Each out-of-order call throws.

## 7. Open questions

1. **Content negotiation** — which format, from an `Accept` header, with the
   1.2 `version` parameter (JSON draft §6) — is the protocol's, at milestone 7.
