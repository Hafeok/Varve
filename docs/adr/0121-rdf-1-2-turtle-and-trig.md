# 0121 — RDF 1.2 Turtle and TriG are accepted; the edition decides a surrogate escape

## Status

**Accepted — filed unaccepted by milestone 6b of #10, 2026-10-09** (ADR 0066).
Decided by the maintainer on the 6b plan. Acceptance is the maintainer's act
on the pull request.

**Supersedes the decision [`docs/spec/turtle.md`](../spec/turtle.md) §9
recorded** (milestone 3b, commit `0a66031`), that no RDF 1.2 construct is
accepted by the Turtle and TriG reader and that the writer refuses a literal
with a base direction. **Amends ADR [0030](0030-turtle-recovery-and-prefixes.md)**:
its recovery depth is counted over three more pairs of brackets.

Written against **RDF 1.2 Turtle, W3C Working Draft of 07 October 2026**
(§6.5 the grammar, §7.3 the parsing of reifiers, reified triples, triple
terms and annotations), **RDF 1.2 TriG, W3C Working Draft of 07 October
2026** (§4.5), and **RDF 1.2 Concepts** for `rdf:reifies` and the two base
directions. The suites are `rdf/rdf12/rdf-turtle` and `rdf/rdf12/rdf-trig`
in the pinned `w3c/rdf-tests` submodule, revision `369a90d` of 28 August
2026.

## Context

`turtle.md` §9 refused every RDF 1.2 construct for one stated reason: at
milestone 3b the Working Drafts were days old, and "implementing a grammar
that recent against a ratchet is churn a later milestone should absorb". The
model, N-Triples and N-Quads carried triple terms and base directions from the
start (milestone 3a), so what a term could be and what Turtle could spell
diverged on purpose, and the writer refused a directional literal rather than
emit a document its own reader rejected.

The reason has expired. The drafts are a year old and have settled on the
shape the suites test: reified triples `<< s p o ~ r >>`, triple terms
`<<( s p o )>>`, annotations `{| … |}`, reifiers `~`, a version directive in
two spellings, and `LANG_DIR`. Their cost is now known: **167 cases** (Turtle
74 syntax and 32 evaluation, TriG 35 syntax and 26 evaluation). And they are
in the way: **41 SPARQL 1.2 evaluation cases** load their data from RDF 1.2
Turtle or TriG and have been pinned as blocked since milestone 5b, with this
slice named as the one that unblocks them.

Wiring the suites found two things the plan had not foreseen.

1. **The two editions contradict each other in one place.** RDF 1.1 Turtle's
   `test-38` is a positive evaluation test whose literal is `"𐑩"`:
   a character outside the basic plane written as two `\u` escapes forming a
   surrogate pair. RDF 1.2 Turtle's `surrogate-pair-bad-01` and `-02` are
   negative syntax tests with exactly that spelling: a surrogate code point is
   not a character, so an escape naming one is an error, paired or not. Every
   other RDF 1.2 addition is a superset; this one is a contradiction, and no
   single grammar passes both suites.
2. **A blank node inside a triple term has no identity but the store's.** Four
   `eval-triple-terms` cases match a triple term pattern whose component is a
   variable bound to, or a blank node standing for, a node inside the term
   (`<< << :s :p2 :o >> :p3 :z >> :q ?q`). The evaluator matched such a
   pattern by externalising the triple term and unifying its components as
   terms, which works over `InMemoryDataset` and fails over the store: ADR
   0044 decides that a label is never internalised back to a handle, so the
   destructured blank node is a term the store does not know, and the next
   pattern finds nothing. Over `InMemoryDataset` the same cases pass.

## Decision

1. **RDF 1.2 Turtle and TriG are accepted, as one grammar.** The reader reads
   RDF 1.2 Turtle §6.5 and TriG §4.5 in full: [29] reified triples in subject
   and object position, nested to any depth; [32] triple terms as objects,
   nested in their object position; [35] annotations after each object of an
   object list, each a sequence of reifiers [28] and annotation blocks [36];
   [6] `@version` and [9] `VERSION` with a short-form string [10]; and [42]
   `LANG_DIR`, with `ltr` and `rtl` the only directions. There is no RDF 1.1
   mode of the grammar: 1.2 is 1.1's superset everywhere but point 2, and a
   document that announces `"1.1"` is read like any other, because the
   announcement is a hint (RDF 1.2 Turtle §2.4) and the directive's value is
   reported to the caller and refused on no value.
   - The parsing rules are §7.3's, to the letter. A reified triple yields
     `reifier rdf:reifies tt`, with a fresh blank node when no reifier is
     named, and **does not assert the triple inside it**; the node it stands
     for is the reifier. An annotation clears the current reifier; each `~`
     sets it and yields the `rdf:reifies` triple at once; each `{| … |}` takes
     the current reifier or mints one, yields the triple if it minted, uses
     the reifier as the block's subject, and clears it. An annotated triple
     **is** asserted, by the object list that carries it.
   - Fresh reifiers are invented nodes under `turtle.md` §4's naming rules,
     `g0`, `g1`, …, and a document's own labels pass through.
   - Triple terms are the model's (`RdfTerm.TripleTerm`, milestone 3a);
     nothing in the term model changes.
   - **The suites `rdf12/turtle-syntax`, `rdf12/turtle-eval`,
     `rdf12/trig-syntax` and `rdf12/trig-eval` are under the ratchet** with
     guard counts 74, 32, 35 and 26, and the chunk-boundary oracle, the
     fixed-point property and the pull/push agreement run over every one of
     their inputs as they run over the rdf11 suites. The 41 blocked cases
     join the evaluation run over all four subjects, and the guard that pinned
     them is deleted.
2. **The edition decides a surrogate escape, and the default is RDF 1.2.**
   `TurtleOptions.Version` and `ParseOptions.Version` take an `RdfVersion`:
   `Rdf12` (the default) refuses every `\u` or `\U` escape naming a surrogate
   code point, paired or not; `Rdf11` reads a well-formed pair as the
   character it encodes, and refuses a lone surrogate as before. The harness
   reads the rdf11 suites under `Rdf11` and the rdf12 suites under `Rdf12`,
   and a result file in the edition of its suite. The option exists for this
   one contradiction and decides nothing else; a second contradiction would be
   a second clause here, never a second option.
3. **The writer writes RDF 1.2, and the version directive is never silently
   absent.** The Turtle and TriG writer emits a triple term as
   `<<( s p o )>>` — the spacing of RDF 1.2's canonical N-Triples (ADR 0061)
   — and a directional literal as `"x"@en--ltr`, where it refused one before.
   It writes `VERSION "1.2"` **once, immediately before the first statement
   whose object is a triple term or a directional literal**, which the grammar
   allows because a directive may stand wherever a statement may ([2]), and
   in TriG it closes an open graph block first, as a prefix declaration does.
   A document that needs no RDF 1.2 construct gets no directive and is read
   by every RDF 1.1 reader. `TurtleWriteOptions.AlwaysDeclareVersion` forces
   the directive to the top. The alternative, buffering the whole document to
   decide the first line, was rejected: streaming is the property the writer
   exists to keep (`turtle.md` §7).
   - **A triple term in subject or predicate position is refused by name.**
     RDF 1.2 Turtle [15] and [17] give a triple term one position, and a
     writer that spelt one elsewhere would produce a document no reader
     accepts; dropping it would write a different dataset.
   - No annotation syntax is written. `rdf:reifies` triples are statements
     like any other, and the writer makes no output-shape decisions
     (`turtle.md` §7, no pretty-printing).
4. **ADR 0030's recovery depth counts the RDF 1.2 brackets.** On an error the
   parser resumes after the next `.` at depth zero, with depth counted over
   `[ ]`, `( )`, `{ }` and now `<< >>`, `<<( )>>` and `{| |}`, outside a
   String and an IRIREF. A `.` inside a reified triple's literal or inside an
   annotation block is not a statement's end, and resuming there would emit a
   triple nobody wrote — the failure 0030 was written to prevent.
5. **`IQuadSource` answers a triple term's component handles.**
   `TryGetTripleTermComponents(handle, out s, out p, out o)` is a member of
   the quad source contract with a default implementation that answers
   through `TryExternalise` and `TryInternalise` — the path a consumer took
   before, so every existing source keeps its behaviour — and that a source
   whose dictionary holds composite entries overrides to answer from the
   entry. The evaluator unifies a nested pattern by handles when the source
   answers and as an externalised term when it does not. Over
   `InMemoryDataset`, which internalises every term it holds, the default
   suffices and the four cases pass. **`Varve.Store` does not implement it in
   this milestone**: the package was another session's in parallel with 6b,
   and the implementation is one dictionary-entry read in `Views.IndexSource`
   and `Views.PendingSource` (and a forward in `StagingView`). It is
   [#87](https://github.com/Hafeok/Varve/issues/87), due before milestone 8.
   Until it lands, the four cases over the store, its graph scope and the
   protocol are **twelve justified exemptions** in `baseline/exemptions.txt`,
   with pyoxigraph 0.5.11's agreement with the suite recorded as the
   tie-breaker.

## Alternatives considered

- **Keep refusing RDF 1.2 Turtle** and translate the 41 cases' data to
  N-Triples offline, as ADR 0027's dated note did for RDF/XML. Rejected: the
  reason for refusing has expired, and a translation is a third party's
  reading of the syntax Varve is meant to read.
- **A full RDF 1.1 grammar mode** rather than one option for one point. The
  SPARQL parser has versions because 1.2 changes the meaning of 1.1 text
  (`sparql-grammar.md` §1); Turtle 1.2 changes the meaning of nothing 1.1
  accepts except the surrogate pair, and the rdf11 negative suites pass whole
  under the 1.2 grammar. A mode that gates the whole grammar would be a mode
  nobody needs, maintained for ever.
- **Accept surrogate pairs in both editions and exempt the two 1.2 cases.**
  Rejected by the tie-breaker rule: Oxigraph (oxttl) refuses an escape naming
  a surrogate code point, and "1.2 wins where the editions differ" is ADR
  0061's rule for the canonical form, applied here to the reader's default.
- **`VERSION "1.2"` always, or never.** Always makes every document unreadable
  by an RDF 1.1 reader for no gain; never leaves a document that needs it
  silently announcing nothing. The maintainer decided the automatic form on
  the plan.
- **Fix the four store cases in the evaluator alone**, by comparing a
  destructured blank node with a store handle through its externalised label.
  Rejected: equality by label is sound within one store but has no hash the
  evaluator could compute without externalising every handle it hashes, so a
  hash join between such a term and a handle would miss. A correct answer
  needs the handle, and the handle is the store's to give.
- **Make `TryGetTripleTermComponents` abstract** so that every source answers.
  Rejected for this milestone only: the store's four sources are another
  session's files, and an abstract member would not compile without them.
  The default is the honest interim: it changes no existing behaviour, and
  the exemptions name what is owed.

## Consequences

- **`turtle.md` §9 is rewritten**: RDF 1.2 is accepted here, with the one
  point on which the edition decides. §1, §2, §4, §5, §7 and §8 carry the
  additions; §10 has no open question left.
- **`Varve.Turtle`'s public API grows** by `RdfVersion`,
  `TurtleOptions.Version`, `ParseOptions.Version`, `TurtleOptions.OnVersion`
  and `VersionHandler`, `TurtleWriteOptions.AlwaysDeclareVersion`, and four
  `ParseErrorKind` members (`UnterminatedReifiedTriple`,
  `UnterminatedAnnotation`, `ExpectedReifier`, `InvalidVersion`). Its output
  changes for a directional literal (written, where it threw) and for a
  triple term (`<<( s p o )>>`, spaced). **`Varve.Rdf`'s contract grows** by
  `IQuadSource.TryGetTripleTermComponents`, with a default; `QuadOverlay` and
  `GraphScopedQuadSource` forward it.
- **The default refuses a document RDF 1.1 Turtle accepted**: one carrying a
  surrogate pair as two `\u` escapes, which no conforming serialiser writes
  and RDF 1.2 forbids. A caller with such data names `RdfVersion.Rdf11`.
- **The harness's comparator sees inside triple terms.** `Isomorphism` and
  `ParsedQuad` found blank nodes only at the top of a term; the first RDF 1.2
  evaluation run showed that `_:g0` inside `<<( _:g0 :p :o )>>` was
  invisible to the bijection, and that a dataset was compared as a list where
  RDF makes it a set (`annotation-07` states one triple twice). Both are
  fixed, and RDFC-1.0's refusal of a blank node inside a triple term
  (`rdf-canon.md` §6) now falls back to a comparator that handles it.
- **The ratchet grows by 319 lines**: 167 for the four suites and 152 for the
  41 cases over four subjects less the twelve exemptions, to **4,441**.
- **The arena's fixed cost per parse rises** in the allocation test, from
  about 6.3 KB to about 10.4 KB on the span path, because the test's
  statement now carries fourteen quads of RDF 1.2 constructs; the per-quad
  figure stays zero on every path (`turtle.md` §8).

## Checks

- **Checked against the accepted ADRs** (0001–0109). Touches **0030**
  (amended, dated, in that file), **0044** (unchanged: the store still
  refuses a label, and point 5 is how a consumer lives with that), **0061**
  (the spacing of a written triple term follows its form; Turtle keeps its
  narrower escape set), **0003** (the contract member is in layer 1 where the
  contract is; nothing references upward). No conflict with any.
- **Layer ownership.** `Varve.Turtle` (2), `Varve.Rdf` (1, the contract
  member), `Varve.Sparql.Evaluation` (3, the unification), the harness.
- **Analyzer rule.** None new. The default member cites
  `Rdf12TurtleAndTrig.TripleTermComponentsByHandle` under `VARVE0003`'s hot
  path scope; the forwarders are `[HotPath]`.
- **Open questions owned.** None. The store's implementation of point 5 is
  owed work, [#87](https://github.com/Hafeok/Varve/issues/87), due before
  milestone 8, not an open question.
