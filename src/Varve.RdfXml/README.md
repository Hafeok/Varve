# Varve.RdfXml

RDF/XML, read and written over `System.Xml`'s streaming reader and writer,
with RDF 1.2's triple terms (`rdf:parseType="Triple"`), annotations and base
directions (`its:dir`).

- **All 166 cases** of the W3C `rdf11` RDF/XML suite and the 31 of the
  `rdf12` evaluation suite pass, with no exemption, under the same ratchet as
  every other syntax; `docs/spec/rdf-xml.md` says what is refused and why.
  Per triple it costs what `XmlReader` costs, and the figure is stated there
  rather than hidden (ADR 0111).
- **The BCL does the XML**: encodings, entities, namespaces and
  well-formedness are `XmlReader`'s, with DTDs refused. This package does the
  RDF/XML grammar over the events it reports, and an `rdf:XMLLiteral` is the
  exclusive canonical form of its content.
- **Nothing per triple is this package's to allocate**: names are the
  reader's atomised strings and values are read in chunks into one arena.
  What `XmlReader` itself costs per element is measured and stated in the
  specification, not hidden.
- **The writer refuses what the syntax cannot spell**, by name: a quad with
  a graph, a triple term outside the object position, a predicate with no
  suffix that is an XML name, a character XML cannot carry. It invents a
  namespace prefix where the syntax leaves it no choice.

```csharp
RdfXmlOptions options = new() { BaseIri = "http://example.org/doc"u8.ToArray() };

RdfXmlParseResult result = RdfXmlParser.Parse(
    stream,
    static (in QuadView quad) => Console.WriteLine(quad.Subject.Lexical.Length),
    in options);
```

```csharp
using RdfXmlWriter writer = new(output, new RdfXmlWriteOptions());
writer.DeclarePrefix("ex"u8, "http://example.org/"u8);
writer.Write(in quad);
```

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
