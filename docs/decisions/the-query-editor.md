---
set: the-query-editor
namespace: varve
adr: 0250
decisions:
  - key: TextareaOverAColouredLayer
    statement: "The query editor is a real textarea over an aria-hidden coloured layer and a gutter in one grid with the same metrics; the browser does the editing, the alignment is CSS only, and in forced colours the textarea's own text shows"
  - key: ColouredByTheTokenizerCheckedByTheParser
    statement: "The editor's runs are the public tokenizer's tokens over the textarea's text, and its error is the parser's first, with the line, column and message the server's sparql-syntax problem carries; a query the parser refuses is not sent"
  - key: OnceAFrame
    statement: "The page is told the editor's text and caret at most once an animation frame, and passes the text back only to the coloured layer; a page that replaces the text sets it through webui.js"
  - key: TabIndentsEscapeThenTabLeaves
    statement: "Tab inserts two spaces and Shift+Tab removes up to two at the line's start; Escape then Tab leaves the editor, and the help line says so"
  - key: TheFormFromTheParsedQuery
    statement: "The answer's form is the parsed query's, from the same parse that checks the text, so the page asks for solutions, a boolean or a graph for the very query it sends; a text the parser refuses has no form and is not sent"
  - key: ColouredAndCheckedUpTo25000
    statement: "The editor colours and checks a text of up to 25,000 UTF-16 code units as it is typed, the largest size on the measurement's grid whose keystroke re-renders, median, in under 100 ms; above it the text shows plain, the help line says why, and Run parses it once and sends only what the parser accepts"
---

The rulings of [ADR 0250](../adr/0250-the-query-editor.md), filed
unaccepted by the session for #355 (ADR 0066).
