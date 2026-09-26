---
set: syntax-model-surfaces
namespace: varve
origin: "DD0013 findings on the syntax packages' model namespaces, after ADR 0064's 2026-09-26 amendment, in session 2 of #43"
decisions:
  - key: SourceCoordinatesAreOffsetsLinesAndColumns
    statement: "A source position or span in a syntax package's model namespace exposes its byte offsets as long and its line and column as int: coordinates into the caller's own input, used to point at or slice that input and never passed on as a quantity of anything else"
  - key: ErrorMessagesAreDisplayText
    statement: "A parse error's Message is a string: text for a person, which nothing parses or branches on, because the error's Kind is the half a program reads"
---

# The primitives on the syntax packages' model surfaces

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

ADR 0064's 2026-09-26 amendment gives each syntax package one
`[DomainModel]` namespace for its public data: `Varve.Turtle.Model`,
`Varve.Sparql.Algebra` (which takes `SourceSpan`, `SparqlParseError` and
`SparqlErrorKind`) and `Varve.Sparql.Results.Model`. That brings their public
members under `DD0013`, which reports sixteen members. Every one is a source
coordinate or an error message. Two questions, each filed with its
alternative.

**`SourceCoordinatesAreOffsetsLinesAndColumns`.** `ParsePosition`,
`ResultsPosition` and `SourceSpan` are nothing but coordinates:
`ByteOffset` / `Start` / `End` / `Length` as `long`, and `Line` and `Column`
as `int`. `SparqlParseError` carries the same three directly (`Offset`,
`Line`, `Column`). Every parser counts them the same way
(`docs/spec/sparql-grammar.md` §6): bytes from the start of the UTF-8 input,
and a 1-based line and byte column. A caller uses them in two ways. It slices
its own input (`input[span.Start..span.End]`), which is the span-boundary
argument of `SpanBoundaryCounts.SpanWriterCountsAreInt` again. Or it shows
`line:column` to a person or an editor, whose protocols take integers. None
of them is stored as, or compared with, a quantity of anything else. The
constructors, where a swap could happen, are exempt from `DD0013` as the
boundary. A reader gets each coordinate by name. The types cite this key on
the type, with `Scope = ExceptionScope.Boundary`; `SparqlParseError` cites it
on the three members.

The alternative is three wrappers, `ByteOffset` (`long`), `LineNumber` and
`ColumnNumber` (`int`), declared once at layer 1 so the three syntax
packages share them. Every current use would unwrap them. It would also be
the first public type below layer 4 with a name ADR 0065 reserves for the
log's own offsets (`ByteCount`, `Position`), so the names would need care.

**`ErrorMessagesAreDisplayText`.** `SparqlParseError.Message` and
`SparqlResultsError.Message` name the token and the production that was
expected. They exist for the person reading the error. `Kind` is the
machine-readable half, and tests and callers branch on it, never on the text.
The members cite this key with `Scope = ExceptionScope.Boundary`.

The alternative is a `DiagnosticText` wrapper with one member, `Value`, and
`ToString`. A wrapper can mean something when two strings could be swapped,
but a parse error holds one string, so this one would not.

**What it does not cover.** A count that means something beyond the input,
such as `ParseResult.QuadCount` (which stays outside the model namespace), or
anything in the algebra that is not a coordinate. Those are
`SparqlAlgebraSurfaces`' and ADR 0065's.
