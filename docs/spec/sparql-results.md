# SPARQL result formats

Functional specification for `Varve.Sparql.Results` (layer 2): reading, and
at milestone 5c writing, the four formats in which SPARQL results travel.
Milestone 5b builds **the readers**, because the evaluation suite's expected
results come in these forms; the writers are 5c's.

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
  `"type": "triple"` in JSON, `<<( s p o )>>` in TSV) and **base direction**
  (`its:dir` in XML and JSON, `--ltr` / `--rtl` in TSV).
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
- **The reader is disposable**, returning its pooled buffers.

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

- **Round trip against the other formats.** Every result file of the
  evaluation suite is read, and a file that exists in two formats for the
  same test reads to the same solutions.
- **The chunk-boundary oracle** (`docs/testing.md` §2), as for every
  streaming reader: each document read whole and read as a sequence split at
  every byte offset gives the same solutions, and a malformed one the same
  error at the same position.
- **Positions.** Malformed documents of each format, with the expected byte
  offset, line and column of the error stated in the test.
- **Allocation.** Reading a document of many solutions allocates a bounded
  amount independent of the number of solutions, beyond the arena's growth.

## 5. Open questions

1. **The writers** are 5c's, with the protocol's content negotiation at
   milestone 7. Owner: 5c.
