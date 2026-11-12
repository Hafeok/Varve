# Session, 2026-11-11: chain builds, steps 0 and 1

Developed with AI assistance under human review. The full records, with the
prompt, are one per issue:
- [`2026-11-11-issue-230-chain-builds-ahead-of-main.md`](../traceability/2026-11-11-issue-230-chain-builds-ahead-of-main.md);
- [`2026-11-11-issue-277-landing-trains.md`](../traceability/2026-11-11-issue-277-landing-trains.md).

A cloud session, not the maintainer's delegate (ADR 0239 point 1). It filed
its decisions unaccepted, posted each head, and asked. It approved, landed,
tagged and accepted nothing.

## Pull requests

| PR | Step | Closes |
|---|---|---|
| [#280](https://github.com/Hafeok/Varve/pull/280), stacked on #261 | 0: ADR 0243, chain builds ahead of main, and landing trains; notes on 0088 and 0102 | #230 |
| stacked on #280 | 1: `land.cs --train`; `agent review` admits a train | #277 |

They land after #259, #260 and #261, in that order. Once `agent review`'s
change is on `main`, they could themselves land as a train.

## ADRs

- **0243**, filed unaccepted: thirteen keys. It is #230's text with the review
  of 2026-11-11 folded in, each settled point marked, and a table of every
  difference from #230.
- **0088** and **0102**, amended 2026-11-11, add-only and unaccepted.

## Issues

- Opened: #277 (step 1); #278 (found: ADR 0241 is taken by both #259 and
  #264).
- Closed by these pull requests: #230, #277.
- Steps 2 to 6 get their issues when each starts.

## Test counts

`land-fixtures` went from 12 cases to 22. `agent-review-tamper` gained 4
cases. No test project changed.

## Questions for the maintainer

1. Should #230 close when 0243 lands with its rulings still unaccepted, or
   should acceptance come first?
2. Do you want to overrule the `chain#` namespace before step 2?
3. Keep or strike the two things this session added to 0243: GitHub's merge
   queue as a rejected alternative, and the revisit condition on trains.
