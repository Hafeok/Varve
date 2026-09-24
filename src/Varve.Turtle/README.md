# Varve.Turtle

N-Triples, N-Quads, Turtle and TriG, read and written over UTF-8, allocating
**exactly zero bytes per quad**.

- **All 883 cases** of the W3C `rdf11` Turtle, TriG, N-Triples and N-Quads
  suites and the `rdf12` N-Triples and N-Quads syntax suites pass, with no
  exemptions. Syntax and evaluation both: an evaluation test's dataset is
  compared up to a bijection of blank nodes.
- **Five ways in**: a span, a `ReadOnlySequence`, a `Stream`, a `Stream`
  asynchronously, and a `PipeReader` — plus `TurtleReader` and `NQuadsReader`
  for pulling one quad at a time. **The answer does not depend on how the input
  arrived**: every suite input is parsed whole and then again split at each byte
  offset, and required to give the same quads, or the same error kind and
  position; and every suite input is read both ways round, push and pull, and
  required to give the same answer again.
- **The recovery unit fits the syntax.** N-Triples resumes after the next
  newline; Turtle, which has no line structure, resumes after the next `.` at
  nesting depth zero and outside a string or IRI, and a failed statement
  produces no quads at all — including ones a blank node property list inside
  it had already emitted.
- **Canonical N-Triples output** per §4, so two canonical documents are
  comparable byte for byte. A Turtle round trip is isomorphic rather than
  byte-identical, because blank node labels are the parser's; **writing what
  was written reproduces it exactly**.
- **RDF 1.2** base direction and triple terms are read and written in
  N-Triples and N-Quads, where the `rdf12` suites gate them. RDF 1.2 Turtle and
  TriG are not accepted at all — not half-accepted.

```csharp
TurtleOptions options = new() { Syntax = RdfSyntax.Turtle };

ParseResult result = TurtleParser.Parse(
    utf8,
    static (in QuadView quad) => Console.WriteLine(quad.Subject.Lexical.Length),
    in options);
```

Writing needs a `TurtleWriter`, because a Turtle document has state a single
statement does not — the prefixes in scope, and in TriG the graph block that is
open. It declares no prefix it was not given: a prefix nobody declared is one
the reader of the output has to guess the meaning of.

```csharp
using TurtleWriter writer = new(output, new TurtleWriteOptions());
writer.DeclarePrefix("ex"u8, "http://example.org/"u8);
writer.Write(in quad);
```

The quad handed to the callback points into the parser's own buffer and is
valid only for that call — which the type system enforces, because a
`ref struct` cannot be captured or stored. Call `Materialise()` to keep one.

Layer 2 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
