# Varve.Sparql.Results

The SPARQL result formats — XML, JSON, CSV and TSV — read and written as
streams of UTF-8.

- **One reader for four formats.** `ReadHead` gives the variables, or the
  boolean of an `ASK`; `Read` advances through the solutions, each a view of
  RDF terms valid until the next `Read`.
- **SPARQL 1.2's additions**: triple terms and base directions in all four.
- **Chunked input.** A `ReadOnlySequence<byte>` is read without being joined.
- **Errors carry byte offset, line and column.** The first error stops the
  reader; a malformed result document has no useful remainder.
- **No `System.Xml`.** The XML format is read by a small non-validating
  reader of the subset it uses, which refuses a DTD.
- **One writer for four formats**, into an `IBufferWriter<byte>` or a
  `Stream` (flushed when you choose, synchronously or not), taking each
  binding as an `RdfTerm` or as a reader's `RdfTermView`, and allocating
  nothing per solution. What a format cannot carry is stated: CSV drops
  datatypes and language tags by design, and XML 1.0 cannot hold U+0000.

```csharp
var reader = new SparqlResultsReader(utf8, SparqlResultsFormat.Json);
while (reader.Read())
{
    if (reader.Current.TryGet(0, out RdfTermView term)) { /* … */ }
}

using var writer = new SparqlResultsWriter(stream, SparqlResultsFormat.Tsv);
writer.WriteHead(["s"]);
writer.StartSolution();
writer.WriteBinding(0, RdfTerm.Iri("http://example.org/s"u8));
writer.EndSolution();
writer.WriteEnd();
await writer.FlushAsync();
```

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
