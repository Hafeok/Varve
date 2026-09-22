---
name: ADR proposal
about: Propose an architectural decision, or supersede one
title: ""
labels: adr
assignees: []
---

> [!NOTE]
> Anyone may propose an ADR. The maintainer accepts or rejects it, and a
> rejection says why in writing (`GOVERNANCE.md`).
>
> **An accepted ADR is never edited.** If this changes an accepted decision it
> is a *superseding* ADR, and the old one keeps its text so the reasoning that
> was wrong stays readable. If the decision still stands and only needs detail,
> it is a **dated amendment** inside the existing ADR instead — say which.

## Problem

> [!TIP]
> What decision needs making, and what is blocked or at risk until it is made?

## Proposed decision

> [!TIP]
> State it as the ADR would: plainly, and in enough detail that someone could
> implement it without asking you what you meant.

## Alternatives considered

> [!TIP]
> **Not a formality.** An ADR that lists no losing option has recorded an
> outcome rather than a decision, and the next person cannot tell whether the
> alternative was rejected or never seen. Give each one a reason.

- **Alternative 1** — rejected because …
- **Alternative 2** — rejected because …

## Consequences

> [!TIP]
> What becomes easier, what becomes harder, and what has to change: code, a
> specification, a rule page, a baseline, a gate. Name them.

## Checks

- [ ] Checked against the accepted ADRs in `docs/adr/`
- [ ] It supersedes ADR **____**, or it contradicts none
- [ ] It departs from `docs/brief.md` in this respect: **____**, or it does not
- [ ] It needs a new dependency (which needs a register entry, ADR 0009)
- [ ] It is accepted ahead of the evidence and therefore states a **revisit condition**
- [ ] It affects AOT, trimming or WASM
