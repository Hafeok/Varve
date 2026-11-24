# 0250 — The query editor: a textarea over a coloured layer, coloured and checked by the parser, once a frame

## Status

**Accepted — filed unaccepted by the session for Web UI part 2 (#355, under
#96), 2026-11-19** (ADR 0066), and accepted by the maintainer on
2026-11-24, written by his delegate (ADR 0239). The plan was approved on
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
the explorer's lazy set (ADR 0253).

## Decision

1. **A real textarea over an `aria-hidden` coloured layer.** `QueryEditor`
   puts a `textarea`, a `pre` that repeats its text in coloured spans, and a
   line-number gutter in one grid, with the same font metrics. The browser
   does all the editing: selection, undo, IME, copy and paste. Assistive
   technology sees a labelled text field. The alignment is CSS only, with no
   measuring script. In forced colours, the layer is hidden and the
   textarea's own text shows.
2. **Coloured by ADR 0253's tokenizer, and checked by the parser.**
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
5. **The page knows the answer's form before sending, from the parser.** The
   form is the parsed query's: a `SelectQuery` asks for solutions, an
   `AskQuery` for a boolean, and a `ConstructQuery` or `DescribeQuery` for a
   graph. So the page sends the right `Accept` and draws the right view, and
   the form is that of the very query sent. The check of point 2 and the form
   come from one parse per edit (`QueryColouring.Check`); a text the parser
   refuses has its error and no form, and is never sent. Revised in place on
   2026-11-20, before acceptance, by the architect's review: the first draft
   read the form from the first keyword token.
6. **Up to 25,000 characters the editor colours and checks as it is typed;
   above that it does neither.** The length is the text's in UTF-16 code
   units (`QueryColouring.CheckedLength`).
   - **Above it**, the coloured layer shows the text plain, with no token
     spans and no bracket match, and nothing is tokenized or parsed per edit.
     The help line says why, for example "This query is 30,042 characters,
     over 25,000: it is not coloured or checked as you type. Run checks it
     once before sending it.", followed by the keys.
   - **Run** is not refused for want of a check: it parses the text once.
     What the parser refuses is shown as any error is, at its line and column,
     and not sent; what it accepts is read as usual.
   - **The measurement.** Measured on 2026-11-20 in the published bundle
     against the server, in Chromium on the session's container (4 cores):
     a SELECT of the given length, set into the textarea, then one character
     typed at its end seven times; a keystroke is timed from the input event
     to the coloured layer holding the new text.

     | Characters | Paste, ms | Keystroke, median of 7, ms (two runs) |
     |---:|---:|---|
     | 1,000 | 17–21 | 16.0, 16.0 |
     | 10,000 | 74–108 | 37.0, 37.2 |
     | **25,000** | 121–165 | **51.6, 56.3** |
     | 50,000 | 228–241 | 104.0, 104.6 |
     | 100,000 | 393–435 | 210.3, 212.4 |
     | 200,000 | 811–860 | 530.3, 613.2 |

     The rule, the graph's (ADR 0251 point 6): the largest size on the grid
     whose keystroke re-renders, median, in under 100 ms. 50,000 is over it in
     both runs, so the limit is 25,000. It is raised only from a
     re-measurement on the perf environment, by the same rule.

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

- `QueryEditor`, an organism, knows no tokenizer. `QueryColouring`, a
  part, holds the mapping, the form and the bracket match, with the parser.
  Both are in `Varve.WebUi.Explorer`: only explorer pages use the editor
  (ADR 0252 point 7).
- The editor's colours are the `tok-*` tokens, used nowhere else.
- **Checks:**
  - Against the accepted ADRs: 0200 (no inline style; the layer's spans are
    classes), 0205 (no new package), 0206 (the levels and the cases), 0253
    (the tokenizer). No conflict.
  - Layer ownership: `Varve.WebUi.Explorer` (7), ADR 0252.
  - Analyzer rule: none new.
