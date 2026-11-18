---
set: a-public-sparql-tokenizer
namespace: varve
adr: 0248
decisions:
  - key: TheTokenizerIsTheParsersLexer
    statement: "SparqlTokenizer is the parser's internal lexer made reachable, with no rule copied; a flag the parser never sets returns comments as tokens, so the parser's path is unchanged"
  - key: TokenPositionsInTheInputsUnits
    statement: "A token's start and length are bytes over UTF-8 and UTF-16 code units over UTF-16; an error keeps the parser's units, bytes, for both"
  - key: WordsClassifiedWithoutContext
    statement: "A word matching a keyword quoted in the grammar, ignoring ASCII case, is Keyword (a only in lower case), true and false are Boolean, and comments are Comment; classification has no context"
  - key: TokenizerStopsAtTheFirstError
    statement: "The tokenizer stops at the first ill-formed terminal with the error the parser reports for it, and never resynchronises"
---

The rulings of [ADR 0248](../adr/0248-a-public-sparql-tokenizer.md), filed
unaccepted by the session for #355 (ADR 0066).
