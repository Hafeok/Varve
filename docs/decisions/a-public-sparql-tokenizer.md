---
set: a-public-sparql-tokenizer
namespace: varve
adr: 0253
decisions:
  - key: TheTokenizerIsTheParsersLexer
    statement: "SparqlTokenizer is the parser's internal lexer made reachable, with no rule copied; a flag the parser never sets returns comments as tokens, so the parser's path is unchanged"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: TokenPositionsInTheInputsUnits
    statement: "A token's start and end are bytes over UTF-8 and UTF-16 code units over UTF-16; an error keeps the parser's units, bytes, for both"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: WordsClassifiedWithoutContext
    statement: "A word matching a keyword quoted in the grammar, ignoring ASCII case, is Keyword (a only in lower case), true and false are Boolean, and comments are Comment; classification has no context"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
  - key: TokenizerStopsAtTheFirstError
    statement: "The tokenizer stops at the first ill-formed terminal with the error the parser reports for it, and never resynchronises"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-24T00:00:00Z
---

The rulings of [ADR 0253](../adr/0253-a-public-sparql-tokenizer.md), filed
unaccepted by the session for #355 (ADR 0066) and accepted by the maintainer
on 2026-11-24, written by his delegate (ADR 0239).
