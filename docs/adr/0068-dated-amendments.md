# 0068 — Dated amendments: add-only, dated, never a change of meaning

## Status

**Accepted.** 2026-09-26. Decided by the maintainer on the session 1 report of
the `DecisionDriven.Analyzers` adoption (issue
[#43](https://github.com/Hafeok/Varve/issues/43)). **Amends ADR
[0001](0001-record-architecture-decisions.md)**: its rule that an accepted ADR
is not edited and that only a superseded ADR's Status line changes. The rest of
0001 stands.

## Context

ADR 0001 says an accepted ADR is not edited. The one change it allows is to a
superseded ADR's Status line, plus typo and broken-link fixes that change no
meaning. `GOVERNANCE.md` says something wider: "A decision that still stands
but needs detail gets a **dated amendment** inside it". The repository follows
`GOVERNANCE.md`. Fifteen ADRs carry a dated amendment block: 0003, 0004,
0006, 0009, 0012, 0016, 0017, 0027, 0028, 0032, 0034, 0048, 0051, 0057 and
0059. Several ADRs of the `DecisionDriven.Analyzers` adoption add Status notes
to the ADRs they amend.

So a practice that nearly a quarter of the ADRs rely on is decided by no ADR,
and contradicts the one that governs ADRs. Enumerating the ADRs into the
decision ledger (ADR [0062](0062-adopting-decisiondriven-analyzers.md)) found
it. A ruling an amendment changed has to carry the amendment's date, which
presumes amendments are legitimate, and no key could be filed for that
without a decision behind it.

The practice has earned its place. An amendment records evidence (0028's
browser probe, 0051's suite verification), a correction the author found in
their own reasoning (0004's withdrawn exemption, 0006's version choice), or a
detail a later decision needed stated (0016's projection refusal, 0048's extra
reference). Writing each as a superseding ADR would have produced ADRs that
say "0016, plus one sentence", which is the bookkeeping ADR 0009 rejected for
package tables.

## Decision

1. **A dated amendment is add-only.** It is a block added to an accepted ADR,
   headed with its date (`Amended YYYY-MM-DD`, or `### Amendment, YYYY-MM-DD`
   as a section), saying what it adds and why. It never edits or deletes the
   accepted text it amends. Where accepted text is wrong, the amendment says
   so beside it and the text stays readable, struck through if need be.
2. **An amendment adds detail, records evidence or a discharged condition,
   corrects the ADR's own reasoning, or states a consequence a later decision
   needs.** It does not change what the ADR decided. **A change of meaning is
   a supersession**: a new ADR that names the one it supersedes, in whole or
   in part, as ADR 0001 requires. Where it is unclear which, it is a
   supersession, as 0001 already says of edits.
3. **The Status line names every amendment and every superseding ADR**, with
   its date. A reader who reads only the Status line knows that the body has
   been added to, and where.
4. **A ruling an amendment adds or changes enters the ledger with the
   amendment's date.** In `docs/decisions/` it keeps its key in the amended
   ADR's set, with `accepted-at` the amendment's date, not the ADR's (ADR
   0062). A ruling a superseding ADR changes moves to that ADR's set under the
   same key.
5. **The amendments made before this ADR are recognised as they stand.** The
   fifteen listed above were checked while their rulings were enumerated
   (session 1 of #43), and none changed what its ADR decided. Where one was
   close, the successor that reversed a ruling already exists and is named in
   the Status line: 0003's table by 0060, 0026's placement by 0064.

## Alternatives considered

- **Stop the practice and supersede instead.** Strictly 0001, and one
  mechanism instead of two. Rejected: each amendment above would become an
  ADR whose decision is "the same as NNNN, with this added". The ADR set would
  fill with bookkeeping, and a reader would have to read two ADRs to learn one
  decision. The reason for a no-edit rule is that reasoning stays readable,
  and an add-only amendment keeps it readable.
- **Allow edits, with a changelog in the ADR.** Rejected: an edited ADR shows
  its current text and hides the reasoning that was wrong, which is the thing
  0001 exists to keep. A changelog beside rewritten text is a second record of
  the same change, and it drifts.
- **Put amendments in `GOVERNANCE.md` only**, leaving 0001 as it is. That is
  the status quo, and it is the defect. A rule that governs ADRs and is not in
  one cannot be cited from the ledger, and contradicts the ADR that is.

## Consequences

- **ADR 0001's rule is amended, not superseded**: ADRs remain five sections,
  numbered, never renumbered, accepted text never edited. This ADR says what
  "not edited" permits beside the text. 0001's Status line names this ADR.
- **`GOVERNANCE.md` and this ADR now say the same thing**, and
  `CONTRIBUTING.md`'s "dated amendment inside it" points here.
- **Every amendment is a ledger event.** When an amendment adds or changes a
  ruling, the same change updates the ADR's set file: a new key, or an
  existing key's statement and `accepted-at`. `eng/decision-sets.cs` cannot
  see an ADR's body, so this is a review obligation.
- **Judging whether an amendment changes meaning is still judgement.** Point 2
  does not make it mechanical. "When unclear, supersede" is the tie-breaker,
  and it errs toward the more expensive, safer form.

## Checks

- **Checked against the accepted ADRs** (0001, 0003–0005, 0007–0018,
  0021–0067) and `GOVERNANCE.md`. Touches:
  - **0001**: amended as above.
  - **0062**: the ledger's transcription rule for amended rulings, now resting
    on a decision.
  - The fifteen ADRs with amendments: recognised.
- **Layer ownership.** None.
- **Analyzer rule.** None. The ADR text is prose that no compiler reads.
  Whether an amendment is add-only is visible in its diff, and the review of
  that diff is where it is checked.
- **Open questions owned.** None.
