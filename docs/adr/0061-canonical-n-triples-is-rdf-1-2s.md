# 0061 — Canonical N-Triples and N-Quads follow RDF 1.2; where 1.1 and 1.2 differ, 1.2 wins

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5c report.
Specification: [`docs/spec/n-triples.md`](../spec/n-triples.md) §5. Gate: the
RDF 1.2 `c14n` suites for N-Triples and N-Quads, under the ratchet.

## Context

`Varve.Turtle`'s canonical writer, the default of `WriteOptions`, wrote RDF 1.1
N-Triples §4's canonical form:
- `ECHAR` for `"`, `\`, LF and CR, and nothing else;
- never a `UCHAR`;
- a language tag as the term held it;
- a triple term as `<<(s p o)>>`, with no space inside the brackets.

N-Quads has no canonical section in 1.1, so `n-triples.md` §5 applied the same
rules with the graph label added. It said that this was an extension of the
specification, not a reading of it.

Milestone 5c's RDFC-1.0 writes RDFC-1.0 Appendix A's form:
- `ECHAR` for BS, HT, LF, FF, CR, `"` and `\`;
- `UCHAR` with uppercase hex for the other C0 controls, DEL, U+FFFE and U+FFFF;
- lowercase language tags.

RDF 1.2 N-Triples §3 (Working Draft, 24 September 2026) defines the same form,
and it requires lowercase: "Alphabetic characters in `LANG_DIR` MUST use only
the lowercase letters".

Varve therefore had two canonical forms that disagreed. A literal holding a tab
or an uppercase tag came out differently from each, and the lowercase rule is
not cosmetic. `"x"@en` and `"x"@EN` are one term (RDF 1.1 Concepts §3.3), so a
form that writes the tag as held gives one term two spellings. RDFC-1.0 found
exactly that in 5c.

The pinned `rdf-tests` submodule already carries RDF 1.2's canonical-form
suites, `rdf/rdf12/rdf-n-triples/c14n` and `rdf/rdf12/rdf-n-quads/c14n`, with
41 cases each, and neither was wired. They test the differences above
directly.

## Decision

1. **Canonical N-Triples is RDF 1.2 N-Triples §3's, and canonical N-Quads is
   RDF 1.2 N-Quads' (the same form, plus the graph label).**
   - One space after the subject, the predicate and the object, and after the
     graph label.
   - A single LF at the end of each line.
   - A lowercase language tag, with `--ltr` or `--rtl` for a direction.
   - A triple term as `<<( s p o )>>`: one space inside each bracket, as the
     suite's expected files write it.
   - A literal of `xsd:string` without its datatype.
   - Within a string: `ECHAR` for BS, HT, LF, FF, CR, `"` and `\`; `UCHAR`
     with uppercase hex for U+0000–U+0007, VT, U+000E–U+001F, DEL, U+FFFE and
     U+FFFF; every other character written as itself.
   - An IRI written as held.
2. **Where RDF 1.1 and 1.2 differ, 1.2 wins.** There are three reasons.
   - **A canonical form exists to be one spelling per term.** RDF 1.1's form
     is not that for language tags, and 1.2 corrects it.
   - **1.2's form is the one the rest of the stack already speaks.** It is
     RDFC-1.0 Appendix A's, which `Varve.Rdf` writes and whose suite gates
     it. Two canonical forms in one library means a caller has to know which
     one a document is in, and a canonical form that needs that knowledge has
     failed at its one job.
   - **1.2 has a test suite for its form, and 1.1 had none.** The 1.1 form
     was held only by our own properties; the 1.2 form is held by the W3C's
     cases.

   Everything RDF 1.1 N-Triples accepts is still read. Only the writer's
   default changes.
3. **The two writers stay two, and a property holds them byte-identical.**
   `Varve.Rdf` cannot call `Varve.Turtle` (ADR 0003), so RDFC-1.0 keeps its
   own term writer. The existing property that the canonical form reads back
   as its dataset (`CanonPropertyTests`, 20,000 generated datasets a run) now
   also requires that `Varve.Turtle`'s canonical writer reproduces RDFC-1.0's
   output byte for byte.
4. **The 82 `c14n` cases are under the ratchet, with guard counts** of 41 per
   manifest. Each manifest lists a 42nd entry, `lantag_with_subtag`, which is
   commented out.
5. **Turtle is unchanged.** Turtle has no canonical form. Its writer keeps RDF
   1.1's narrower escape set, which every Turtle reader accepts.

## Alternatives considered

- **Keep RDF 1.1's form as the default and offer 1.2's as an option**, wiring
  the `c14n` suites against the option. Rejected: it keeps two canonical
  forms, which is the problem.
- **Move RDFC-1.0's writer up to `Varve.Turtle`** and have RDFC-1.0 call it.
  Rejected: RDFC-1.0 is in `Varve.Rdf` (ADR 0059), which is below
  `Varve.Turtle`, and the writer is small. Holding two copies to one form with
  a property costs less than a layering exception.
- **Wait for RDF 1.2 to become a Recommendation.** Rejected. Nothing is
  published, so no consumer's bytes change, and the Working Draft's form is
  already RDFC-1.0's, which is a Recommendation. If 1.2 changes before it is
  final, the suite will change with it and the ratchet will say so.

## Consequences

- **`Varve.Turtle`'s default output changes** for:
  - literals holding BS, HT, FF, other C0 controls, DEL, U+FFFE or U+FFFF;
  - literals with an uppercase language tag;
  - triple terms;
  - an explicit `^^xsd:string` that a parser read.

  The byte-stability round trip holds under the new form.
- **A reader defect found by the new suite is fixed in the same change.** The
  line parser read a literal's `^^` or language tag only immediately after the
  closing quote, so it rejected `"Alice" @en` and `"2" ^^ <…>`. `'^^'` and
  `LANG_DIR` are terminals, and whitespace may separate terminals. The Turtle
  reader already accepted both.
- `n-triples.md` §5, `rdf-canon.md` §4 and the writers' own documentation
  describe one form.
- `n-triples.md` still cites RDF 1.1 N-Triples for the grammar. The two
  grammars agree on everything this reader tests, apart from the RDF 1.2
  additions, which are already listed there.
