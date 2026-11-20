# 0253 — A public SPARQL tokenizer: the parser's lexer, reachable

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-18** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The plan was approved on
[#96](https://github.com/Hafeok/Varve/issues/96#issuecomment-6979264625), with
the rulings of 2026-11-18. Filed as 0248, it became 0253 on 2026-11-20 by the
maintainer's ruling on #387: 0248 is #129's on `main`, and its acceptance there
is that ADR's, not this one's.

It records the decision set
[`a-public-sparql-tokenizer`](../decisions/a-public-sparql-tokenizer.md), and
specifies itself in `docs/spec/sparql-grammar.md` §10. It amends and
supersedes nothing.

## Context

The data explorer's query editor (#96, ruling 4 of 2026-11-02) colours a query
and underlines its parse error from Varve's own lexer and parser, running in
the browser, so that the editor and the server cannot disagree. The parser is
public (`SparqlParser.TryParseQuery`), and its error carries the line, column
and offset the server's `sparql-syntax` problem carries. The lexer is not:
`Lexer`, `Token` and `TokenKind` are internal to `Varve.Sparql`.

An editor needs the terminals, not the tree: where each token starts and ends,
and what kind it is. Writing that again in the Web UI would be a second lexer,
which is exactly the disagreement the ruling forbids. So the lexer becomes
reachable.

Two things the parser's lexer does not do, an editor needs:
- **Comments.** The lexer skips them as trivia. An editor colours them.
- **Keywords.** The lexer returns every word as `Word`, and the parser reads it
  in context (`sparql-grammar.md` §3.4). An editor wants `SELECT` coloured
  before the parser has decided what it is.

And one thing the browser needs: the editor's text is UTF-16 (a `textarea`'s
value), while the lexer reads UTF-8, so positions must be given in the units
the caller holds.

## Decision

1. **`SparqlTokenizer` is the parser's lexer, made reachable.**
   `SparqlTokenizer.Tokenize` in `Varve.Sparql` (layer 2) runs the internal
   `Lexer` over a whole text and returns its tokens. It copies no rule. A flag the parser never sets makes the lexer return a
   comment as a token instead of skipping it, so the parser's path is
   unchanged and stays gated by the syntax suites.
2. **Positions are in the input's units.** Over UTF-8 a token's `Start` and
   `End` are bytes; over UTF-16 they are UTF-16 code units, mapped from the
   transcoded bytes as the tokens advance, in one pass. An error keeps the
   parser's units (bytes, `sparql-grammar.md` §6) for both, because it is the
   parser's error and the server's problem reports the same members.
3. **The kinds are the terminals, plus three classifications** (§10):
   `Comment`; `Keyword`, for a word that matches, ignoring ASCII case, a
   keyword quoted in the grammar of §2 (`a` in lower case only); and
   `Boolean`, for `true` and `false`. Brackets keep their own kinds, so an
   editor can match them; the rest of the punctuation is two kinds,
   `Punctuation` and `Operator`. Classification has no context: a
   keyword is a keyword wherever it stands.
4. **It stops at the first ill-formed terminal**, with the error the parser
   would report for it, and never resynchronises (`sparql-grammar.md` §7).
   Over UTF-16 a lone surrogate is the error wherever it stands, because the
   parser transcodes before it reads and reports it so.
5. **The public API** is `SparqlTokenizer` (two `Tokenize` overloads) and
   `SparqlTokens` (the tokens and the error that ended them), in
   `Varve.Sparql` beside `SparqlParser`; and `SparqlToken` (kind, start, end,
   as `long`) and `SparqlTokenKind`, in the model namespace
   `Varve.Sparql.Algebra` beside `SparqlParseError` and `SourceSpan`, under
   `SourceCoordinatesAreOffsetsLinesAndColumns`. It allocates the token list
   and nothing per token: it runs once per edit, never per quad. The internal `Token` and `TokenKind` stay internal: the
   public kinds are a stable mapping of them, so the lexer can change its
   internal kinds without a public break.

## Alternatives considered

- **A tokenizer in the Web UI.** A second lexer, which the ruling forbids:
  the editor and the server would disagree on the first escape or code point
  one of them gets wrong.
- **A `ref struct` that returns one token per call.** It would allocate
  nothing, but a decision is cited on a class (`[Contract]`), a Blazor
  component holds the tokens across a render anyway, and an editor tokenizes
  a few hundred tokens per frame.
- **Making `Lexer` and `TokenKind` public as they are.** They throw on an
  error, expose the parser's internal kinds (so every refactoring of the
  parser would be a public break), and have no comments or keywords.
- **Colouring from the parse tree.** The tree has spans per node
  (`SourceSpan`), but not per token, and there is no tree for a query with an
  error, which is when an editor is most used.
- **Keywords in context**, from the parser's own reading. That needs the
  parser to run to each word, and stops at the first error; an editor colours
  past an error.

## Consequences

- `Varve.Sparql`'s public API grows by four types; the baseline diff is in
  the pull request.
- The tokenizer is lazy-loaded with the parser in the Web UI's explorer
  (ADR 0252), and measured with it (ADR 0205, amended).
- **Checks:**
  - Against the accepted ADRs: 0003 (layer 2, references only `Varve.Rdf` and
    `Varve.Iri` as before), 0026 (no `[HotPath]`: the tokenizer runs per
    keystroke frame, not per quad), 0048 (the parser is unchanged). No
    conflict.
  - Layer ownership: `Varve.Sparql`, layer 2.
  - Analyzer rule: none new; the public types are held by the API baseline
    (`RS0016`, `RS0017`) and the freeze gate (ADR 0227).
  - Tests: the three guarantees of `sparql-grammar.md` §10 over every case of
    the syntax suites, in `Varve.Sparql.Tests`.
