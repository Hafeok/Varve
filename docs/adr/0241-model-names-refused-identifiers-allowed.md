# 0241 — A commit message never names a model; a model's identifier is allowed

## Status

**Accepted — filed unaccepted by the session for #257, 2026-11-10** (ADR 0066),
its ruling accepted on #259 on 2026-11-11, at the maintainer's word, by his
delegate (ADR 0239). Decided
by the maintainer on 2026-10-27, in the scope of
[#169](https://github.com/Hafeok/Varve/issues/169). Acceptance is the
maintainer's act, or his delegate's for a decision he has read (ADR 0239).

**Supersedes one ruling of [0033](0033-commit-traceability.md)**,
`ModelNamedInTheRecordNotTheCommit`, as its amendment of 2026-10-02 stated it.
The half about the record is restated here unchanged; the half about the
commit message is reversed. Under ADR 0068 point 2, that reversal is a change of
meaning, so it is a supersession, not an amendment. The ruling moves to this
ADR's set under the same key (ADR 0068 point 4).

## Context

ADR 0033's amendment of 2026-10-02 said a commit message never carries a
model's *identifier*, the string a provider's API takes to select a model,
and allowed a co-author trailer that names the product. Its reason was that a
commit message cannot be corrected without rewriting the trunk (ADR 0032),
and the record is where the fact can be corrected.

In practice the trailer did the opposite. The tool these sessions use writes a
co-author trailer that names the model by its marketing name, family and
version (`Claude Opus 5.5`). `main` carried 241 such commits by 2026-10-27,
all signed and merged, so they stay. The name was in the commit, and the
identifier was barred. The identifier is what a reader can resolve exactly
and what the record holds; a name is the form that drifts and gets
advertised.

On 2026-10-27 the maintainer ruled the other way round, for #169's gate:
**names refused, identifiers allowed.** `AGENTS.md` and `CONTRIBUTING.md`
still said the reverse, and a gate built to the ruling would contradict an
accepted decision. This ADR records the ruling so that the gate, the rule
files and the ledger agree.

## Decision

1. **A commit message never carries a model's name.** A name is a model
   family word (`Opus`, `Sonnet`, `Haiku`, `Fable`, `Mythos`, and any family
   the gate's configuration adds), alone or after `Claude`, followed by a
   version: `Opus 5.5`, `Claude Sonnet 5.5`. The rule covers the subject, the
   body and every trailer.
2. **A model's identifier is allowed**, in the API form a provider takes
   (`claude-<family>-<major>-<minor>`), as is the bare product name in a co-author trailer:
   `Co-Authored-By: Claude <noreply@anthropic.com>`.
3. **The traceability record still names the tool and the model exactly**,
   unchanged from 0033's amendment: the model is the identifier the session's
   metadata reports. Where the tool will not write it into a pushed file, the
   record says so where the model belongs and the maintainer fills it in. A
   blank model is an incomplete record.
4. **History is left as it is.** #169's gate checks the commits a pull
   request or a `land/` branch adds over `main`, never `main`'s history.

## Alternatives considered

- **Amend 0033 instead.** Rejected: ADR 0068 point 2 makes a change of meaning
  a supersession, and this reverses which form is allowed.
- **Refuse both names and identifiers.** Rejected by the maintainer's ruling.
  An identifier in a commit is harmless and exact. Refusing it would also
  refuse the one form a tool can write mechanically from its own metadata.
- **Keep 0033's rule and fix the trailer.** Not chosen. It leaves the rule
  barring the precise form and allowing nothing that says which model wrote
  the commit, while the gate that would enforce it does not exist.

## Consequences

- **`AGENTS.md` and `CONTRIBUTING.md` change with this ADR.** **Every
  commit** and **Never** state the ruling, and so does `CONTRIBUTING.md`'s
  traceability paragraph.
- **#169's gate implements this ADR**, not 0033's amendment. Its rule page
  cites both: 0033 for the record, 0241 for the commit message.
- **The 241 commits on `main` that name a model stay.** Nothing rewrites the
  trunk (ADR 0032).
- **Until this ADR is accepted**, `ModelNamedInTheRecordNotTheCommit` is filed
  unaccepted in this ADR's set, and a citation of it is `CS0618` (ADR 0066).

## Checks

- **Checked against the accepted ADRs.** Touches:
  - **0033:** one ruling superseded, as above. Every other ruling stands,
    `TraceabilityRecordPerSession` and `ProseNamesNoProduct` included.
  - **0032:** the trunk is not rewritten.
  - **0066, 0068:** filed unaccepted; the ruling moves under its key.
- **Layer ownership.** None.
- **Analyzer rule.** None here. The gate is #169's, in the commit-message stage
  beside `eng/issue-refs.cs` and `eng/dco.cs`.
- **Open questions owned.** None.
