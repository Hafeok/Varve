# Turtle and TriG

Functional specification for the Turtle and TriG reader and writer in
`Varve.Turtle` (layer 2).

Status: Accepted. Changes only together with the ADR that motivates the change.

## 1. Normative references

- **RDF 1.1 Turtle** — W3C Recommendation, 25 February 2014. §6 the grammar,
  §6.3 IRI resolution, §7 parsing.
- **RDF 1.1 TriG** — W3C Recommendation, 25 February 2014. §2.6 the grammar.
- **RFC 3986 §5.2** for relative resolution, through `Varve.Iri`.

Conformance is measured by `rdf/rdf11/rdf-turtle` (**313** cases: 74
positive-syntax, 94 negative-syntax, 145 evaluation) and `rdf/rdf11/rdf-trig`
(**357**: 99, 115, 143) in the pinned `w3c/rdf-tests` submodule.

**Unlike the N-Triples suites, most of these are evaluation tests.** An
evaluation test names an input and an expected result — N-Triples for Turtle,
N-Quads for TriG — and passes only when the two are **isomorphic**: equal after
some bijection of blank nodes. So these suites test the quads produced and not
only which documents are accepted, which is what makes them the first real
check on the term model.

**RDF 1.2 Turtle and TriG are not implemented.** Their suites exist in the
submodule and are not wired. `rdf12-turtle` is a W3C Working Draft of
**14 September 2026** and `rdf12-trig` of **15 September 2026**; they add
reifiers, annotation syntax and a version directive. Implementing a grammar
that recent against a ratchet is churn a later milestone should absorb. The
1.2 constructs already in `Varve.Turtle` — base direction and triple terms in
N-Triples and N-Quads — are gated by their own suites (`n-triples.md` §1).

## 2. Grammar

Turtle §6, reproduced so that an implementer can check the code against
something in the repository. Terminals shared with N-Triples — `IRIREF`,
`BLANK_NODE_LABEL`, `UCHAR`, `ECHAR`, `HEX`, `PN_CHARS_BASE`, `PN_CHARS_U`,
`PN_CHARS`, `LANGTAG` — are in `n-triples.md` §2 and are not repeated.

```
[1]    turtleDoc             ::= statement*
[2]    statement             ::= directive | triples '.'
[3]    directive             ::= prefixID | base | sparqlPrefix | sparqlBase
[4]    prefixID              ::= '@prefix' PNAME_NS IRIREF '.'
[5]    base                  ::= '@base' IRIREF '.'
[5s]   sparqlBase            ::= "BASE" IRIREF
[6s]   sparqlPrefix          ::= "PREFIX" PNAME_NS IRIREF
[6]    triples               ::= subject predicateObjectList
                              | blankNodePropertyList predicateObjectList?
[7]    predicateObjectList   ::= verb objectList (';' (verb objectList)?)*
[8]    objectList            ::= object (',' object)*
[9]    verb                  ::= predicate | 'a'
[10]   subject               ::= iri | BlankNode | collection
[11]   predicate             ::= iri
[12]   object                ::= iri | BlankNode | collection
                              | blankNodePropertyList | literal
[13]   literal               ::= RDFLiteral | NumericLiteral | BooleanLiteral
[14]   blankNodePropertyList ::= '[' predicateObjectList ']'
[15]   collection            ::= '(' object* ')'
[16]   NumericLiteral        ::= INTEGER | DECIMAL | DOUBLE
[128s] RDFLiteral            ::= String (LANGTAG | '^^' iri)?
[133s] BooleanLiteral        ::= 'true' | 'false'
[17]   String                ::= STRING_LITERAL_QUOTE | STRING_LITERAL_SINGLE_QUOTE
                              | STRING_LITERAL_LONG_SINGLE_QUOTE | STRING_LITERAL_LONG_QUOTE
[135s] iri                   ::= IRIREF | PrefixedName
[136s] PrefixedName          ::= PNAME_LN | PNAME_NS
[137s] BlankNode             ::= BLANK_NODE_LABEL | ANON
[139s] PNAME_NS              ::= PN_PREFIX? ':'
[140s] PNAME_LN              ::= PNAME_NS PN_LOCAL
[19]   INTEGER               ::= [+-]? [0-9]+
[20]   DECIMAL               ::= [+-]? [0-9]* '.' [0-9]+
[21]   DOUBLE                ::= [+-]? ([0-9]+ '.' [0-9]* EXPONENT
                              | '.' [0-9]+ EXPONENT | [0-9]+ EXPONENT)
[154s] EXPONENT              ::= [eE] [+-]? [0-9]+
[23]   STRING_LITERAL_SINGLE_QUOTE      ::= "'" ([^#x27#x5C#xA#xD] | ECHAR | UCHAR)* "'"
[24]   STRING_LITERAL_LONG_SINGLE_QUOTE ::= "'''" (("'" | "''")? ([^'\] | ECHAR | UCHAR))* "'''"
[25]   STRING_LITERAL_LONG_QUOTE        ::= '"""' (('"' | '""')? ([^"\] | ECHAR | UCHAR))* '"""'
[161s] WS                    ::= #x20 | #x9 | #xD | #xA
[162s] ANON                  ::= '[' WS* ']'
[167s] PN_PREFIX             ::= PN_CHARS_BASE ((PN_CHARS | '.')* PN_CHARS)?
[168s] PN_LOCAL              ::= (PN_CHARS_U | ':' | [0-9] | PLX)
                                ((PN_CHARS | '.' | ':' | PLX)* (PN_CHARS | ':' | PLX))?
[169s] PLX                   ::= PERCENT | PN_LOCAL_ESC
[170s] PERCENT               ::= '%' HEX HEX
[172s] PN_LOCAL_ESC          ::= '\' ('_' | '~' | '.' | '-' | '!' | '$' | '&' | "'" | '('
                              | ')' | '*' | '+' | ',' | ';' | '=' | '/' | '?' | '#' | '@' | '%')
```

TriG (§2.6) replaces the document production and adds graphs:

```
[1g]   trigDoc               ::= (directive | block)*
[2g]   block                 ::= triplesOrGraph | wrappedGraph | triples2
                              | "GRAPH" labelOrSubject wrappedGraph
[3g]   triplesOrGraph        ::= labelOrSubject (wrappedGraph | predicateObjectList '.')
[4g]   triples2              ::= blankNodePropertyList predicateObjectList? '.'
                              | collection predicateObjectList '.'
[5g]   wrappedGraph          ::= '{' triplesBlock? '}'
[6g]   triplesBlock          ::= triples ('.' triplesBlock?)?
[7g]   labelOrSubject        ::= iri | BlankNode
```

**One parser reads both**, as it already reads N-Triples and N-Quads, with the
syntax as an option.

### Points the grammar makes that are easy to get wrong

- **`[3g] triplesOrGraph` needs one token of lookahead past a whole term.** An
  IRI or blank node at the start of a TriG block is either a graph label — if
  `{` follows — or a subject. The term has to be parsed before the question can
  be answered, so the parser reads the term into the arena and then decides,
  rather than guessing and backtracking.
- **`PN_LOCAL` may contain `.`, `:` and escaped punctuation, but may not end
  with `.`** — the same trailing-dot ambiguity as `BLANK_NODE_LABEL`, resolved
  the same way: scan, then retreat to the last character the production allows
  to be final.
- **A `.` is a statement terminator and also part of `DECIMAL` and of
  `PN_LOCAL`.** `:a.b` is one prefixed name; `:a. b` is a prefixed name and a
  terminator; `1.` is an integer and a terminator, while `1.0` is a decimal.
- **`'a'` is `rdf:type`** only as a verb, and is an ordinary prefixed-name local
  part elsewhere.
- **A long string may contain one or two quote characters** without terminating,
  so `"""a"b"""` is one literal. Terminating on the first quote is the classic
  bug and the suites test it.
- **A collection produces triples of its own**, an `rdf:first`/`rdf:rest` chain
  ending in `rdf:nil`, and an empty collection `()` *is* `rdf:nil` with no
  triples at all.
- **`[]` in the subject position is a fresh blank node**; `[ :p :o ]` is a fresh
  blank node **and** the triples of its property list, which are emitted before
  the statement that uses it.
- **Comments are whitespace** (§6.2), outside an `IRIREF` or a String, to the
  end of the line — so `#` inside `<...>` or `"..."` is content.

## 3. Prefixes and base

- **`@prefix`/`PREFIX` bind a prefix to an IRI**, and a later binding of the
  same prefix replaces the earlier one for everything after it. Bindings are
  document-scoped and do not nest.
- **A prefixed name's IRI is the concatenation** of the bound IRI and the local
  part, with `PN_LOCAL_ESC` escapes removed and `PERCENT` escapes left as they
  are. It is **not** resolved as a relative reference: concatenation is the
  whole rule (§6.3), and a prefixed name whose expansion is not a legal IRI is
  an error rather than something to be resolved.
- **`@base`/`BASE` set the in-scope base IRI**, and each is resolved against the
  previous one (§6.3) — so `@base <a/> .` twice gives `a/a/`, not `a/`.
- **Every relative `IRIREF` is resolved against the in-scope base** with RFC
  3986 §5.2, through `Varve.Iri`, with no normalisation beyond what resolution
  performs.
- **With no base and no directive, a relative IRI is an error.** The caller may
  supply one — the document's retrieval IRI, §6.3 — and the W3C suites do:
  each entry's action IRI is its base.
- **A parser reports prefixes and the base as they are declared**, in order,
  through callbacks. A writer needs them to compact, a tool needs them to
  round-trip, and recovering them afterwards from the IRIs is guesswork.

## 4. Blank node scoping

- **A `BLANK_NODE_LABEL` is document-scoped.** Two occurrences of `_:b` in one
  document are the same node; the same label in another document is not.
- **`[]`, `[ … ]` and each `(` … `)` element position produce a fresh node**,
  distinguishable from every label and from each other.
- **Labels are not preserved across a parse.** A blank node's identity is the
  document's, and `Varve.Rdf` blank nodes carry the label as written for
  diagnostics only — which is why the evaluation tests compare up to
  isomorphism rather than by label.

## 5. Error recovery

**The statement is the recovery unit** (ADR 0030). Turtle has no line
structure, so N-Triples' rule does not transfer.

- On an error, the parser reports it and **resumes after the next `.` that is
  at nesting depth zero and outside a String and an `IRIREF`** — depth counted
  over `[ ]`, `( )` and TriG's `{ }`.
- Everything from the start of the failed statement to that point produces **no
  quads**, including the triples a blank node property list or a collection
  inside it had already generated. A statement is all-or-nothing.
- Prefix and base bindings made **before** the failed statement stay in effect.
  A failed `@prefix` binds nothing.
- If no such `.` exists, the parse ends.
- The parser does not attempt to repair anything, ever.

Recovery is **off by default**, exactly as for N-Triples: with no
`OnError` handler the first error stops the parse.

### Why this rule

A parser that resynchronises anywhere else has to guess where the author meant
a term to end, and a wrong guess emits a triple nobody wrote — worse than
losing the statement, because nothing downstream can tell. Depth-zero is what
makes the rule safe inside a blank node property list, where a `.` may appear
in a `PN_LOCAL` or a decimal and where an inner `]` has not yet closed.

## 6. Position reporting

As N-Triples (`n-triples.md` §4): byte offset, 1-based line, and 1-based column
**counted in bytes**. A statement may span lines, so an error carries the
position of the offending byte rather than the start of the statement.

**The position is the document's, not the buffer's.** A streaming reader holds
a fragment at a time, and the obvious implementation counts lines from the
start of whatever it is holding — which reports the same fault at different
places depending on how the input was delivered. The reader therefore carries
the document offset, line number and line start across chunks, and a line that
began in an earlier chunk is still measured from where it began.

## 7. Writing

Both syntaxes, streaming, from a view or from a quad and its source.

- **Prefix compaction.** A writer is told its prefixes up front and uses them;
  it does not invent them, because a prefix nobody declared is a prefix the
  reader of the output has to guess the meaning of.
- **Canonical form is N-Triples §4's**, extended to Turtle: one space between
  terms, no comments, a character written directly rather than as a `UCHAR`,
  and `ECHAR` only for the four characters that need it. Turtle has no
  canonical form of its own, and saying so is better than implying one.
- **No pretty-printing** in this milestone: no nested blank node property
  lists, no collection syntax, no predicate or object lists. Every statement is
  written out in full. Those are output-shape decisions with their own
  trade-offs, and a writer that makes them badly is harder to undo than one
  that does not make them at all.
- **TriG opens a block when the graph changes**, `<label> { … }`, with the
  default graph written unwrapped. Not *one block per graph*: that needs the
  whole dataset in memory before the first byte, and streaming is the property
  the writer exists to keep. A caller whose quads are grouped by graph gets one
  block per graph; a caller whose quads are interleaved gets several. Both
  denote the same dataset. Declaring a prefix or a base also closes an open
  block, because a directive inside one is not Turtle.
- **A local name is used when the remainder can be spelt as one**, escaping
  with `PN_LOCAL_ESC` where the grammar allows it, and the full IRI is written
  when it cannot. The check is on the bytes, not on an assumption about what
  IRIs look like: a `%` in the remainder falls back, because `%20` written
  literally reads back as a percent-escape and denotes a different IRI.

A round trip through the writer is **isomorphic**, not byte-identical: blank
node labels are the parser's, and a document that named them `_:b0` may come
back naming them something else. Byte stability is an N-Triples property, and
`n-triples.md` §5 keeps it.

Concretely, and worth knowing before someone reports it: §4's scheme gives a
document's own label `x` the name `bx`, so a second round trip names it `bbx`,
and a label grows by one byte per trip. That is the cost of the scheme's
guarantee — a document label and a parser-minted one can never collide — and
the alternative, reserving a naming space no document may use, is not something
a streaming parser can enforce, since it would have to know every label the
document uses before emitting the first fresh one.

## 8. Streaming and allocation

**Allocation per triple is a defect**, and the assertion is zero bytes on the
view path, measured as in `n-triples.md` §6.

Turtle is harder than N-Triples here and the difference is worth stating. A
statement is not a line, so the buffering unit is a statement and a statement
can be arbitrarily long — a collection of ten thousand elements is one. The
pooled scratch therefore grows to the longest *statement* rather than the
longest line, and a document with one enormous collection costs that much
memory once.

A blank node property list produces triples **before** the statement that
contains it finishes, which is why ADR 0030 makes the parser hold a statement's
quads and release them together on the terminating `.`: an error later in the
statement has to be able to take them back. The buffer is the arena, so the
cost is bounded by the statement's text and not paid per triple.

### The chunk-boundary rule

**A token whose end is settled by the byte after it must not be decided at the
end of a buffer that can still grow.** Turtle has several: a number (`1.` is an
integer and a statement's dot, or the start of `1.5`), a language tag, a
prefixed name's local part, a blank node label, the `@` or `^^` that may follow
a string, a keyword (`@prefix`, `PREFIX`, `BASE`, `GRAPH`, `true`, `false`, `a`),
an `ANON`'s closing `]`, a multi-byte character, and a comment with no newline
yet. Each of them, cut in the wrong place, has a plausible wrong answer — and
for a number the wrong answer is a **quad nobody wrote** rather than an error,
which is the worst kind.

The reader answers this with one mechanism: the scanner knows whether its
buffer is the document's last, waits when it is not, and decides when it is. So
`<s> <p> 1.` is a complete document and `1.` at a chunk boundary is an
unfinished decimal.

This is asserted by an **oracle** rather than by cases: for every input in
every wired manifest, the conformance project parses the file whole and then
again split at each byte offset, and requires the same quads, or the same error
kind and position, every time. It never asks whether the answer is right — the
suites do that — only whether the parser agrees with itself, which makes the
expected value computable and the corpus free. It found six defect classes in
this reader that hand-written tests had missed, and it is wired for every
format rather than for Turtle alone: N-Triples and N-Quads are correct here by
construction, because their line buffer never hands the parser a partial line,
and the oracle is what turns that argument into a measurement.

## 9. Open questions

None. RDF 1.2 Turtle and TriG are deferred rather than open: the decision is
recorded in `docs/roadmap.md`, and the reason is the age of the drafts.
