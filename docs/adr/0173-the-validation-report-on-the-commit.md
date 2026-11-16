# 0173 — The validation report on the commit: a reference and a digest

## Status

**Accepted — filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13), 2026-10-20** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan (#13, 2026-10-20):
reference plus digest, one term per commit per binding, and a digest mismatch
after an upgrade read as an engine change. Amends **0154** (the restricted
report) by a dated amendment.

## Context

The brief: a commit may land "with the validation report attached as commit
metadata". ADR 0017's validator attachment is on `main`: an accepting
validator returns a term, the sequencer records it in the commit header, the
header hash covers it, and the commits resource and the change feed serve it.
The current report is maintained by the validation projection (part 2b), so
storing the report again on every commit would store it twice.

## Decision

1. **The mechanism is ADR 0017's validator attachment.** #134's host
   attachment (ADR 0165) is not needed: the validator decides what it attaches.
2. **One attachment per binding per accepted commit**, under `report` and
   `reject` (ADR 0169), in the order of the bindings' IRIs:

   ```
   <<( <binding> varve:affectedReport "sha256:<hex>" )>>
   ```

   a triple term whose object is the SHA-256, in lower-case hex, of the
   canonical N-Quads (ADR 0154) of the report **restricted to the commit's
   affected focus nodes** in the post-state: the results of every affected
   pair (ADR 0171) that is a target in the post-state, as one report.
3. **A reference and a digest.**
   - The reference: the binding, at the commit's position, names the
     projection's report there — the whole report.
   - The digest: it pins the slice of it the commit decided on. Restricting
     the projection's report at the same position to the same pairs gives the
     same bytes, by ADR 0171's rule (an unaffected pair keeps its results), so
     a reader can check any commit's attachment against the projection.
   - The report is stored once, in the projection; the attachment is the
     binding's IRI and a 71-character literal.
4. **A digest mismatch after an upgrade is how an engine change shows up.**
   The digest is over the engine's report, so a release that changes any
   result for the same shapes and data — a component's fix, a new reading of a
   section — makes a recomputed slice differ from the recorded one. That is
   the record working, not a corruption: **a release that changes SHACL
   results says so in its notes**, and names the components.
5. **`ValidationReport.Restrict`**, internal to `Varve.Shacl`'s incremental
   validation, builds the restricted report: its results are a subset of the
   whole report's, in the same order, with `sh:conforms` computed over them.

## Alternatives considered

- **The binding alone**, one term per binding, bounded: nothing to check. A
  reader of a commit could not tell which report it was decided against, nor
  see an engine change.
- **The report itself, as triple terms.** Self-contained, and the report twice
  — once in the log for ever and once in the projection — growing the log by
  the report's size on every commit.
- **A digest of the whole report.** It would need the whole report at commit
  time, which is the full validation ADR 0171 avoids.
- **A typed literal** `"<binding> sha256:…"^^varve:affectedReport`. One term
  either way; the triple term reads as the statement it is.

## Consequences

- **One dictionary term per commit per binding**, unbounded, unlike ADR
  0165's management position. Stated in the operator's guide.
- `docs/vocabulary.md` gains `varve:affectedReport`.
- The release notes' template gains a line for a change in SHACL results.

## Checks

- **Checked against the accepted ADRs** (0001–0157, and #134's 0163–0168).
  Touches **0017** (the attachment, amended by ADR 0169), **0044** (the data's
  blank nodes keep their labels, so the slice is the projection's), **0154**
  (amended: the restricted report), **0165** (distinguished). No conflict.
- **Layer ownership.** `Varve.Shacl` (3) restricts the report;
  `Varve.Shacl.Store` (5) attaches it.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-30 — who receives the attachment

Filed by milestone 9, part 1 (#202, under #14), unaccepted until the maintainer accepts it (ADR 0066). The ruling is ADR [0237](0237-rows-on-every-surface.md)'s `ReportWithheldFromRowRestricted`.

A shapes binding's attachment is delivered on the commits resource, the change
feed and the diff only to a caller who may read the binding's report graph
(ADR 0174 point 9, as amended on 2026-10-30). To any other caller the commit
carries no such attachment.
