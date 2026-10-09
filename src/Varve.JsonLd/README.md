# Varve.JsonLd

JSON-LD 1.1, read and written over `System.Text.Json`'s `Utf8JsonReader` and
`Utf8JsonWriter`: expansion, `toRdf` and `fromRdf`, with RDF 1.2's base
directions and `rdf:JSON` literals in RFC 8785 canonical form.

- **All 448 toRdf, 368 expand and 53 fromRdf cases** of the W3C JSON-LD 1.1
  API suite that a 1.1 processor can run pass, with no exemption, under the
  same ratchet as every other syntax; the 38 cases a stated rule excludes
  (1.0-only behaviour, the 1.0 processing mode, generalized RDF) are counted
  in `docs/spec/json-ld.md`.
- **The document is a tree before it is a dataset**: the whole input is read
  into one index-linked tree in pooled arrays, expanded into the same tree,
  and walked for quads. The figure per quad is the tree's growth, measured
  and stated in the specification page rather than hidden, and the emitter
  itself allocates nothing (ADR 0112).
- **Remote contexts come only through the caller's document loader**; with
  none, the default, a remote context is the error `loading remote context
  failed`. Nothing in this package opens a connection.
- **`@direction` is RDF 1.2's own by default**: a directional language-tagged
  string. The specification's `i18n-datatype` and `compound-literal` modes
  and its default of dropping the direction are options.
- **Errors are the specification's codes**, one enum value per
  `JsonLdErrorCode`, so a caller and the W3C suite name the same thing.
- **Compaction, flattening and framing are not implemented**; the three are
  recorded as out of scope, with the reason, in the specification page.

```csharp
JsonLdOptions options = new() { BaseIri = "http://example.org/doc"u8.ToArray() };

JsonLdResult result = JsonLdParser.Parse(
    stream,
    static (in QuadView quad) => Console.WriteLine(quad.Subject.Lexical.Length),
    in options);
```

```csharp
using JsonLdWriter writer = new(output, new JsonLdWriteOptions { UseNativeTypes = true });
writer.Write(in quad);
```

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
