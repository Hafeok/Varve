# N-Triples and N-Quads

Functional specification for the N-Triples and N-Quads reader and writer in
`Varve.Turtle` (layer 2).

Status: Accepted. Changes only together with the ADR that motivates the change.

## 1. Normative references

- **RDF 1.1 N-Triples** — W3C Recommendation, 25 February 2014. §2 the grammar,
  §4 canonical N-Triples.
- **RDF 1.1 N-Quads** — W3C Recommendation, 25 February 2014. §4 the grammar.
- **RDF 1.2 N-Triples** — Working Draft, 23 July 2026, checked 2026-09-21.
  Backward compatible; see `rdf-model.md` §1.

Conformance is measured by `rdf/rdf11/rdf-n-triples` and
`rdf/rdf11/rdf-n-quads` in the pinned `w3c/rdf-tests` submodule: 70 and 87
cases, **all of them positive or negative syntax tests**. There are no
evaluation tests in these two suites, so the suites prove that we accept and
reject the right documents and prove nothing about the quads we produce. That
is what the round-trip property tests are for.

## 2. Grammar

N-Triples (§2), reproduced so that an implementer can check the code against
something in the repository:

```
[1]    ntriplesDoc          ::= triple? (EOL triple)* EOL?
[2]    triple               ::= subject predicate object '.'
[3]    subject              ::= IRIREF | BLANK_NODE_LABEL
[4]    predicate            ::= IRIREF
[5]    object               ::= IRIREF | BLANK_NODE_LABEL | literal
[6]    literal              ::= STRING_LITERAL_QUOTE ('^^' IRIREF | LANGTAG)?
[144s] LANGTAG              ::= '@' [a-zA-Z]+ ('-' [a-zA-Z0-9]+)*
[7]    EOL                  ::= [#xD#xA]+
[8]    IRIREF               ::= '<' ([^#x00-#x20<>"{}|^`\] | UCHAR)* '>'
[9]    STRING_LITERAL_QUOTE ::= '"' ([^#x22#x5C#xA#xD] | ECHAR | UCHAR)* '"'
[141s] BLANK_NODE_LABEL     ::= '_:' (PN_CHARS_U | [0-9]) ((PN_CHARS | '.')* PN_CHARS)?
[10]   UCHAR                ::= '\u' HEX HEX HEX HEX | '\U' HEX HEX HEX HEX HEX HEX HEX HEX
[153s] ECHAR                ::= '\' [tbnrf"'\]
[162s] HEX                  ::= [0-9] | [A-F] | [a-f]
```

`PN_CHARS_BASE` [157s], `PN_CHARS_U` [158s] and `PN_CHARS` [160s] are the
Unicode ranges in §2; they are transcribed in the implementation and tested
against the suite's blank-node cases rather than repeated here.

N-Quads (§4) differs in exactly two productions:

```
[1] nquadsDoc ::= statement? (EOL statement)* EOL?
[2] statement ::= subject predicate object graphLabel? '.'
[6] graphLabel ::= IRIREF | BLANK_NODE_LABEL
```

Everything else is shared. **One parser reads both**, with the syntax as an
option: the only difference is whether a fourth term is permitted before the
`.`, and in N-Triples encountering one is an error rather than an unknown
production.

### Points the grammar makes that are easy to get wrong

- **A comment is `#` to end of line**, permitted where whitespace is (§2), and
  a `#` inside an `IRIREF` or a string is not a comment. The suite tests this
  directly (`comment_following_triple`).
- **`EOL` is `[#xD#xA]+`** — one or more, in any mix. A bare `\r` ends a line.
- **`UCHAR` is resolved before validation**, so `<http://example/ >` is a
  bad IRI and not a valid one containing an escape.
- **A `UCHAR` may encode a surrogate code point.** An unpaired surrogate is not
  a Unicode scalar value and cannot be encoded in UTF-8; a `\uD800` that is not
  followed by a low surrogate escape is an error.
- **Literals cannot contain a raw newline** (§9 excludes `#xA` and `#xD`), so a
  newline in a literal must be an `ECHAR`. The N-Triples specification says so
  explicitly: "as only the `STRING_LITERAL_QUOTE` production is allowed new
  lines in literals MUST be escaped."

## 3. Error recovery

**The line is the recovery unit.** Both formats are line-based by construction,
which makes the rule short enough to be correct:

- on an error, the parser reports it and **resumes at the byte after the next
  `EOL`**;
- a line that failed produces no quad — never a partial one;
- the parser does not attempt to repair anything, ever.

Recovery is **off by default**. `ParseOptions.OnError` is null unless a caller
supplies a handler, and with no handler the first error stops the parse. A
caller that wants to read a damaged file opts in and gets told about every
line it lost.

`ParseResult` reports the quad count, the error count and the first error.
"Succeeded" means no errors, not "some quads were produced".

### Why this rule and not a smarter one

A parser that tries to resynchronise inside a line has to guess where the
author meant a term to end, and a wrong guess produces a quad that was never
written — which is worse than losing the line, because nothing downstream can
tell. Turtle at milestone 5 has no line structure to fall back on and will need
a different rule; that is a decision to take then, with a grammar that needs it.

## 4. Position reporting

Every error carries **byte offset, line and column**.

- **Byte offset** is from the start of the input, and is what a tool that wants
  to seek needs.
- **Line** is 1-based, counting `EOL` occurrences as the grammar defines them.
- **Column** is 1-based and counted **in bytes**, not in characters or grapheme
  clusters.

Bytes, because the parser works in UTF-8 and a column measured in characters
would require decoding the line twice — once to parse and once to count — and
because the offset a byte-oriented tool wants is the byte. This is stated rather
than left to be discovered from a surprising column number in a file of Arabic
literals.

## 5. Writing

Both formats, from a view (zero allocation) or from a quad plus its source.

**Canonical form** is N-Triples §4, and it is the default for anything that
must be comparable:

- exactly one space (U+0020) after subject, predicate and object; no whitespace
  anywhere else it would be allowed;
- no comments;
- `HEX` in uppercase;
- **characters must not be represented by `UCHAR`** — write the character;
- within a string, `ECHAR` for U+0022, U+005C, U+000A and U+000D **and nothing
  else**. A character that can be written directly must be.

**N-Quads has no canonical form section.** We apply N-Triples §4's rules to it
unchanged, with the graph label written after the object separated by a single
space. That is an extension of a specification rather than a reading of one,
and is recorded as such.

A round trip through the canonical writer is therefore byte-stable: parsing and
re-writing a canonical document reproduces it exactly, which is what the
property tests assert.

## 6. Streaming and allocation

**Allocation per quad is a defect** (constraint 5), and the assertion is zero
bytes per quad on the view path, not a small number.

The formats being line-based is what makes this straightforward. The parse unit
is a line; a line within one buffer segment is parsed in place, and a line
spanning segments is copied into a **pooled scratch buffer** rented once and
reused. Steady state allocates nothing. The same structure serves push and
pull, synchronous and asynchronous, span, `ReadOnlySequence`, `Stream` and
`PipeReader`.

A quad's terms are spans over the parser's buffer and are **valid only for the
duration of the callback**. Holding one past that is a use-after-free that the
type system prevents — `ref struct` cannot be captured or stored — which is why
the view is a `ref struct` and not a convenience.

## 7. Open questions

None. Turtle's error-recovery rule (§3) is milestone 5's, not an open question
here.
