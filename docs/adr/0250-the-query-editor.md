# 0250 — The query editor: a textarea over a coloured layer, coloured and checked by the parser, once a frame

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. The plan was approved on
[#96](https://github.com/Hafeok/Varve/issues/96#issuecomment-6979264625) with
the maintainer's rulings of 2026-11-18. The syntax colours came from the
design side (D8), and the error cases are held to the parser in both
directions (the ruling on #355).

It records the decision set
[`the-query-editor`](../decisions/the-query-editor.md).

## Context

The Query screen needs a SPARQL editor. It has to:

- colour the query;
- show a syntax error before the query is sent, in the words the server
  would use;
- match brackets;
- stay an ordinary text field to a screen reader, to the browser's undo and
  IME, and to copy and paste.

An editor library (a script editor, or a component suite) would bring its own
DOM, its own accessibility and its own package to the Web UI's bundle, CSP
and size gate (ADRs 0200 and 0205). The parser and its lexer are already in
the explorer's lazy set (ADR 0248).

## Decision

1. **A real textarea over an `aria-hidden` coloured layer.** `QueryEditor`
   puts a `textarea`, a `pre` that repeats its text in coloured spans, and a
   line-number gutter in one grid, with the same font metrics. The browser
   does all the editing: selection, undo, IME, copy and paste. Assistive
   technology sees a labelled text field. The alignment is CSS only, with no
   measuring script. In forced colours, the layer is hidden and the
   textarea's own text shows.
2. **Coloured by ADR 0248's tokenizer, and checked by the parser.**
   - `QueryColouring` makes the runs from `SparqlTokenizer` over the very
     text the textarea holds: one span per token, its kind mapped to a
     `vw-tok--` class.
   - The parser's first error is shown at its offset, with the parser's
     line, column and message, which are the server's `sparql-syntax`
     members.
   - The editor and the server therefore say the same thing, and the page
     refuses to send a query the parser refuses.
   - Every case is held to the parser both ways: a case that draws an error
     parses to it, and a case that draws none parses, unless it is blank.
3. **Once a frame.** `webui.js` reports the text and the caret to the page
   at most once per animation frame, so a burst of typing is tokenized and
   parsed once. The textarea keeps its own value: the page passes the text
   back only to the coloured layer, and once the person has typed, the
   browser no longer lets the textarea's content override its value. A page
   that replaces the text (a query from the history) sets it through
   `webui.js`.
4. **Keyboard.** Tab inserts two spaces, and Shift+Tab removes up to two at
   the line's start. Escape, then Tab, leaves the editor (WCAG 2.1.2), and
   the help line says so. The bracket at the caret and its partner are
   outlined, not only filled.
5. **The page knows the answer's form before sending.** The first query
   keyword gives the form: SELECT asks for solutions, ASK for a boolean, and
   CONSTRUCT or DESCRIBE for a graph. So the page sends the right `Accept`
   and draws the right view.

## Alternatives considered

- **An editor library.** It means a new package and an ADR, its own DOM and
  ARIA to audit, and bytes in the lazy set for a second lexer that would
  disagree with the server's.
- **`contenteditable`.** The browser's editing would be replaced by ours:
  undo, IME and paste become the page's problem, and a screen reader reads a
  styled region, not a field.
- **Colouring on every keystroke.** It is correct, but a paste or a held key
  would tokenize and parse once per character.
- **Checking on the server only.** A query would have to be sent to learn it
  is malformed, and the error would arrive after a round trip.

## Consequences

- `QueryEditor` (an organism in `Varve.WebUi.Components`) knows no
  tokenizer. `QueryColouring` (a part in `Varve.WebUi.Explorer`) holds the
  mapping, the form and the bracket match, with the parser.
- The editor's colours are the `tok-*` tokens, used nowhere else.
- **Checks:**
  - Against the accepted ADRs: 0200 (no inline style; the layer's spans are
    classes), 0205 (no new package), 0206 (the levels and the cases), 0248
    (the tokenizer). No conflict.
  - Layer ownership: `Varve.WebUi.Components` (6) and
    `Varve.WebUi.Explorer` (7), ADR 0252.
  - Analyzer rule: none new.
