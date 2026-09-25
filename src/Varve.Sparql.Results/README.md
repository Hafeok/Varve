# Varve.Sparql.Results

The SPARQL result formats — XML, JSON, CSV and TSV — read as streams over
UTF-8. Writers follow at milestone 5c.

- **One reader for four formats.** `ReadHead` gives the variables, or the
  boolean of an `ASK`; `Read` advances through the solutions, each a view of
  RDF terms valid until the next `Read`.
- **SPARQL 1.2's additions**: triple terms and base directions in all four.
- **Chunked input.** A `ReadOnlySequence<byte>` is read without being joined.
- **Errors carry byte offset, line and column.** The first error stops the
  reader; a malformed result document has no useful remainder.
- **No `System.Xml`.** The XML format is read by a small non-validating
  reader of the subset it uses, which refuses a DTD.

```csharp
var reader = new SparqlResultsReader(utf8, SparqlResultsFormat.Json);
while (reader.Read())
{
    if (reader.Current.TryGet(0, out RdfTermView term)) { /* … */ }
}
```

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
