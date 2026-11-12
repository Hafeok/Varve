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
| [#281](https://github.com/Hafeok/Varve/pull/281), stacked on #280 | 1: `land.cs --train`; `agent review` admits a train; the merge pinned | #277 |

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

## The maintainer's decisions, 2026-11-11

1. The rulings are accepted on #280 before it lands, and #230 closes with
   it. Done: accepted on #280 by his delegate at his word.
2. The `chain#` namespace stays.
3. Both additions stay: GitHub's merge queue as a rejected alternative, and
   the revisit condition on trains.
4. A one-member train stays refused. A failed train whose shorter form has
   one member prints the plain landing command.

Two additions to #281 followed:
- the pinned merge settings, in `land.cs`, in `agent-review.cs`, and in ADR
  0243 point 10;
- two more tamper cases: an extra commit between the merges, and a `main`
  that has moved.

## `eng/ci.cs` before landing

The whole pipeline, 66 jobs, passed on both heads:
- #280 at `d6ffddca8f50`, in 18 min 6 s;
- #281 at `d1c6d9911eb1`, in 17 min 6 s.

The final counts: `land-fixtures` 12 → 25 cases; `agent-review-tamper`, six
train cases. No test project changed.

## Landed

#280 and #281 landed on 2026-11-12, and #230 and #277 are closed. The first
train ran the same day: four members went red on #282's new gate, and the
shorter train of three landed. #278 closes when #264, renumbered to 0244,
lands.
