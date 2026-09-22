# 0030 — Turtle's recovery unit, prefix exposure, and where isomorphism lives

## Status

**Accepted.** 2026-09-22.

Records [`docs/spec/turtle.md`](../spec/turtle.md) §3, §5 and §7, and answers
the question [`docs/spec/n-triples.md`](../spec/n-triples.md) §3 deferred to
this milestone.

## Context

`docs/spec/n-triples.md` §3 makes the line the recovery unit and says, in as
many words, that "Turtle at milestone 5 has no line structure to fall back on
and will need a different rule; that is a decision to take then, with a grammar
that needs it." Turtle arrived at 3b rather than 5. This is that decision, plus
two smaller ones the same milestone forces.

## Decision

### 1. The statement is the recovery unit

On an error, the parser resumes **after the next `.` that is at nesting depth
zero and outside a String and an `IRIREF`**, with depth counted over `[ ]`,
`( )` and TriG's `{ }`.

Everything from the start of the failed statement to that point produces **no
quads** — including triples a blank node property list or a collection inside it
had already emitted. A statement is all-or-nothing. Prefix and base bindings
made *before* the failed statement stand; a failed `@prefix` binds nothing.

**Why depth-zero and not simply "the next `.`".** A `.` appears inside a
`DECIMAL`, inside a `PN_LOCAL`, inside any string, and inside a blank node
property list that has not yet closed. Resuming at the first one found would
restart the parser in the middle of a construct and emit triples nobody wrote
— which is worse than losing the statement, because nothing downstream can tell
the difference between a triple the author wrote and one a confused parser
invented.

**Why a statement and not something smaller.** Turtle's smaller units are not
self-delimiting: a predicate-object list continues across `;`, an object list
across `,`, and both may nest. There is no shorter prefix of the input whose
end can be recognised without understanding what came before it.

**Why retracting already-emitted triples is right.** A blank node property list
emits its triples before the statement containing it finishes (`turtle.md` §8),
so an error late in a statement can arrive after quads have gone to the handler.
Those quads describe a blank node the failed statement was about to attach to
something, and keeping them leaves an orphan subgraph that no document asserted.
The parser therefore buffers a statement's quads and releases them on the
terminating `.`.

**This costs the zero-allocation property nothing and costs memory something**,
and the trade is stated here rather than discovered: the buffer is the arena,
which already holds the statement's terms, and it grows to the largest statement
in the document rather than the largest line. A collection of ten thousand
elements is one statement.

### 2. Prefixes and the base are reported as they are declared

`TurtleOptions` carries `OnPrefix` and `OnBase` callbacks, invoked in document
order at the moment each directive is read.

A writer needs the prefixes to compact with. A tool that round-trips a document
needs them to reproduce it. And recovering them afterwards by looking at the
IRIs is guesswork — several prefixes can expand to IRIs sharing a stem, a
declared prefix may be used nowhere, and a redeclaration leaves no trace in the
output at all.

**Reported, not returned.** A document can redeclare a prefix, so there is no
single final map to return; the sequence is the fact, and a caller that wants a
map builds the one it needs.

### 3. Dataset isomorphism lives in the conformance project

The Turtle and TriG evaluation tests — 145 and 143 of them — compare an output
dataset with an expected one **up to a bijection of blank nodes**. Something has
to implement that.

It goes in `Varve.Conformance.Tests`, **not** in `Varve.Rdf`:

- **RDFC-1.0 brings the production version.** Canonicalisation is a later
  milestone and is the right answer to this question: it assigns each blank node
  a canonical label, after which isomorphism is equality. Implementing a second
  answer now means maintaining both and, worse, having to decide which one is
  authoritative when they disagree.
- **The test version may be slow.** A backtracking search over blank-node
  candidates is fine at suite sizes — the largest case has a handful of blank
  nodes — and is not fine as a library API somebody calls on a real graph.
  Shipping it in `Varve.Rdf` would be shipping a function whose cost is
  exponential in an input nobody bounds.
- **It is not needed outside the harness yet.** Nothing in the milestone's
  scope compares two datasets for equality except the tests.

It is not taken from dotNetRDF, which the harness is in the middle of retiring.

## Alternatives considered

- **Resume at the next newline**, as N-Triples does. Simplest, and wrong: a
  Turtle statement routinely spans lines, so resuming at a newline restarts the
  parser inside the statement it just failed on and produces a cascade of
  errors from one fault. The N-Triples rule works because there the line *is*
  the statement.
- **Resume at the next `.` regardless of depth.** Cheaper — no depth tracking —
  and it emits triples nobody wrote, as above.
- **Emit the triples a failed statement had already produced**, on the grounds
  that they were well-formed when emitted. Lost: they are an orphan subgraph
  attached to a blank node the document never finished describing, and a
  consumer cannot tell them from asserted data.
- **Return a prefix map at the end of the parse** rather than reporting
  declarations. Simpler for the common case. Lost to redeclaration: a map
  cannot express `@prefix : <a>` followed by `@prefix : <b>`, and the writer
  that wants to reproduce the document needs both.
- **Put isomorphism in `Varve.Rdf`** so that anyone can compare two datasets.
  Lost on all three counts above, most of all the second: an exponential
  function in a public API is a denial of service waiting for an input.
- **Take isomorphism from dotNetRDF** for the tests. It is right there, and the
  harness already depends on it. Lost because this milestone removes that
  dependency, and because a conformance harness that uses another
  implementation to judge whether we agree with the specification is measuring
  agreement with that implementation.

## Consequences

**The parser needs a statement-level buffer**, which N-Triples did not. It is
the arena, reset per statement rather than per line, and the allocation
assertion is unchanged: zero bytes per triple in steady state, with the arena
growing once to the largest statement.

**An error in a long statement loses a lot.** A collection of ten thousand
elements with one bad IRI in the middle yields nothing for the whole
collection. That is correct — the collection is one assertion — and it is worth
knowing before someone reports it as a bug.

**A Turtle round trip is isomorphic, not byte-identical.** Blank node labels are
the parser's own, so a document naming them `_:b0` may come back naming them
otherwise. The property tests assert isomorphism; the byte-stability property
stays with N-Triples canonical form, where it is well defined.

**The isomorphism check will be deleted, not promoted.** When RDFC-1.0 lands,
the harness switches to canonical labelling and the backtracking version goes.
It is a scaffold with a stated exit, in the same sense as ADR 0007's dotNetRDF
dependency — and that one is being honoured in this milestone, which is the
evidence that this kind of note is worth writing.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0029).
  Touches **0007** (the harness, whose dotNetRDF exit criterion this milestone
  meets), **0024** (the view and the arena, now reset per statement) and
  **0026** (`[HotPath]` on the new parse and write paths). No conflict with any.
- **Layer ownership.** The reader, the writer and the recovery rule are
  `Varve.Turtle`, layer 2. Isomorphism is test code and belongs to no layer.
- **Analyzer rule.** None reserved.
- **Open questions owned.** None. It closes the one `n-triples.md` §3 deferred.
