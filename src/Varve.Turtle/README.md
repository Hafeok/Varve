# Varve.Turtle

N-Triples and N-Quads, read and written over UTF-8, allocating **exactly zero
bytes per quad**.

- **All 213 cases** of the W3C `rdf11` and `rdf12` N-Triples and N-Quads syntax
  suites pass, with no exemptions.
- **Five ways in**: a span, a `ReadOnlySequence`, a `Stream`, a `Stream`
  asynchronously, and a `PipeReader` — plus `NQuadsReader` for pulling one quad
  at a time. A test drives one document through all of them and compares.
- **The line is the recovery unit.** With no error handler the first error ends
  the parse; with one, the rejected line is reported and parsing resumes after
  the next newline. A rejected line produces no quad rather than a partial one.
- **Canonical output** per N-Triples §4, so two canonical documents are
  comparable byte for byte.

```csharp
ParseResult result = NQuadsParser.Parse(
    utf8,
    static (in QuadView quad) => Console.WriteLine(quad.Subject.Lexical.Length),
    new ParseOptions { Syntax = RdfSyntax.NQuads });
```

The quad handed to the callback points into the parser's own buffer and is
valid only for that call — which the type system enforces, because a
`ref struct` cannot be captured or stored. Call `Materialise()` to keep one.

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. Apache-2.0.
