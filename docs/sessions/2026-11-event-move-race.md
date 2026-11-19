# Session, 2026-11-19: a cancelled event move stays cancelled (#254)

Developed with AI assistance under human review, by a cloud session that is
not the maintainer's delegate (ADR 0239). The prompt and the maintainer's
answer are in [the record](../traceability/2026-11-19-issue-254-event-move-race.md).

## Pull request

| PR | Head | Completes | Hosted result |
|---|---|---|---|
| [#374](https://github.com/Hafeok/Varve/pull/374) | `1cd717d` tested; then the citations and this close-out | #254 | at `1cd717d`: every job green but `native aot (macos-15)`, which no runner took ("not acquired by Runner of type hosted", macOS arm64 capacity), rerun; `agent review` green. The head after it is red on `CS0618` alone, for the three unaccepted keys (ADR 0066) |

## The cause

`EventMoveTests.A_catalogue_that_does_not_translate_one_to_one_cancels_the_move_and_writes_reopen`
failed now and then at line 412 (`409 event-catalogue-moved` after the
cancel) or at line 408 (the state read `moving`). The server did not answer
the cancel before it was in force. The fleet's interval loop undid it after
the answer:

- `Fleet.ResumeMovesAsync` picked every dataset that was `Moving` in the
  grants it had applied, then called the host's resume, unserialised with a
  move request for the same dataset.
- The resume ran the move's whole driver, `EventMoves.RunAsync`, with no
  caller. Each round the driver re-read the state, and it took
  `varve:InDataset` as a move to start: it froze the catalogue again.
- So a cancel committed between the loop's pick and its run, by the move's
  own request or by an operator's `apply`, was undone. With a catalogue that
  translates, the run went on to `Moved`, which is final.
- A request's run whose cancel went stale did the same.

A trace from a failing run, with a diagnostic print in a throwaway build,
showed the loop's second freeze: the request froze at 2 and cancelled at 3;
the loop's cancel went stale, it found `InDataset` at 3, froze again at 4 and
cancelled at 5. The test's write landed between 4 and 5.

#217 had proposed another cause, the grants snapshot lagging the system head.
That gap does not occur on this path, because a local write's advance
recompiles the snapshot before it returns. #217 is closed as a duplicate of
#254.

## Reproduction, before and after

| | Linux | Windows |
|---|---|---|
| Before: the flaky test, loaded, at `c754bcb` | 11 of 300 (3.7%) in this container: 9 at line 412, 2 at line 408 | seen in CI three times (2026-11-02 on #217, 2026-11-16, 2026-11-19); not looped |
| After the first fixes, 500 runs under load, `repeat` job at `627ca92` | 0 of 500 ([run 44560515570](https://github.com/Hafeok/Varve/actions/runs/44560515570)) | **1 of 500** ([run 44560510759](https://github.com/Hafeok/Varve/actions/runs/44560510759)): `409` for `422`, the race of the one-run gate |
| After the first fixes, 300 runs under load, `eng/repeat.cs --load`, this container, at `a247813` | **1 of 300**: the same `409` for `422` | — |
| **After every fix, 500 runs under load, `repeat` job at `1cd717d` (the gate)** | **0 of 500**, 25 min ([run 44570301523](https://github.com/Hafeok/Varve/actions/runs/44570301523)) | **0 of 500**, 29 min ([run 44570295242](https://github.com/Hafeok/Varve/actions/runs/44570295242)) |
| After every fix, 300 runs under load, this container, at `1cd717d` | 0 of 300, 15 min | — |
| The new tests at the red head `a3af2c8` (hosted) | `build (ubuntu-latest)` and `pipeline (devcontainer)`: exactly the 2 new tests failed | `build (windows-latest)`: exactly the 2 new tests failed |

## What changed

1. **(a) A run of the move never undoes a cancel.**
   - A run with no caller never freezes.
   - A run that has seen the move in progress, by its own committed freeze or
     by reading `Moving`, commits nothing more when it then finds
     `varve:InDataset`.
   - A request's run answers `409 event-move-cancelled`, a new problem type
     with a page and "What to do". The client does not retry it.
2. **(b) A cancel whose write came back with a problem** answers that
   problem, not "cancelled".
3. **(c) A write to a dataset that is `Moving` here** advances the system
   follower once before its grants are pinned (`MovingDatasetWrites`).
   - **What it adds to such a write:** one read of the store, then the
     datasets converge to the declarations under the advance's lock, so
     writes to a moving dataset take that lock one at a time. The read is:
     - on a follower of the system dataset, a probe of its next log slot;
     - on its writer on an object store, the writer record;
     - on a fleet of one, nothing.
   - **When the plane cannot be read:** a catalogue write to a dataset that
     was `Moving` is `503 management-behind`, which the client already sends
     again (ADR 0246 point 10). Every other write is decided as before.
   - **`EventCatalogueValidator`** takes the catalogue's state, `Open`,
     `Frozen` or `Unconfirmed`, in place of a bool. This is unshipped API in
     `Varve.Protocol`, not the client.
4. **A management write answered `NoChange`** is applied before its answer
   (`ManagementAnswers`). The model-based test found this one; the plan did
   not list it.
5. **One run of a dataset's move at a time** (`IManagementPlane.EnterMoveAsync`
   and `TryEnterMove`, default members). A move request waits for a run in
   progress; the interval loop's resume does not, and leaves the move to the
   run that holds it. `eng/repeat.cs --load` found this one after the first
   fixes, and so did the hosted 500 on Windows. The interval loop resumed a
   move a request was running and cancelled it first, so the request
   answered `409 event-move-cancelled` where the reason was its own
   untranslatable catalogue. Without the gate, a test of its own fails, and
   the model's fourth invariant fails in 6 runs of 6.
   - `EventCatalogueValidator` keeps its shipped constructor beside the new
     one. `api freeze` had refused its removal (CP0002, shipped in
     `v0.1.0-preview.22`).
6. **ADR 0167 amended 2026-11-19, filed unaccepted.** Three keys, none
   accepted: `ResumeOnlyFinishesAMove`, `CancelIsAnsweredOnlyWhenInForce`
   and `MovingWriteAdvancesFirst`. The last commit cites them, so the head is
   red on `CS0618` alone (ADR 0066); the head before it is the tested one.
7. **The `repeat` job** in `nightly.yml`, dispatch only (`eng/repeat.cs`,
   docs/testing.md §11).
   - The required-checks gate refuses `repeat (ubuntu-latest)` as a required
     name, which was proven with a declaration that requires it.

## The move's transitions

| # | Transition | Path | Could the answer run ahead? | Now |
|---|---|---|---|---|
| 1a | start (freeze, m₁) | the move request, local | no | unchanged |
| 1b | start (freeze, m₁) | `apply` of `Moving`, the dataset's writer another instance | yes, harmless to the move: the barrier authorises at ≥ m₁ | unchanged |
| 2a | complete (switch, m₂) | local | no | unchanged |
| 2b | complete (switch, m₂) | forwarded to a remote system writer | for this instance's state read only | [#367](https://github.com/Hafeok/Varve/issues/367) |
| 3a | the move's own cancel | local | no, but the loop undid it | (a) |
| 3b | the move's own cancel | forwarded | yes: a catalogue write here refused 409 until the next interval | (c) |
| 3c | the move's own cancel, write failed | any | yes, with no effect at all | (b) |
| 4a | cancel by `apply` | on the system writer | no, but the loop undid it, to `Moved` when the catalogue translates | (a) |
| 4b | cancel by `apply` | the dataset's writer another instance | yes, until its next interval | (c) |
| 4c | cancel by `apply` answered `NoChange` | any | yes: answered before this instance applied the head | `NoChange` advance |
| 5 | a request's run racing another run | any | not an answer, but it froze again after a cancel | (a) |

## The model-based test

`EventMoveModelTests` uses one server per schedule. One round of the interval
loop (`Fleet.TickAsync`) and one move request are each held between the
move's steps by a signal (`Fleet.Advancing`). Between their steps a schedule
does any of these:

- a freeze by apply;
- a cancel by apply;
- a catalogue write;
- the system dataset's refusal of writes on or off, so the move's own writes
  fail.

The catalogue translates, or it does not.

**The invariants**, checked after every action:

- no `moved` after an acknowledged cancel, unless a move was started since;
- with no move in progress, `in-dataset`, and a catalogue write is accepted;
- an answer that says cancelled holds when it is given;
- and it gives the true reason: `409 event-move-cancelled` only after a
  cancel by apply during the request, and `422` "cancelled" only for a
  catalogue that does not translate.

**Runs:** 40 schedules on the land gate; `VARVE_EVENT_MOVE_SCHEDULES` raises
it. On the fixed code, 1,000 ran clean in about 35 s.

**On the old code** it fails in 12 runs of 12, with one part of the fix
removed at a time:

| Removed | Found | Invariant |
|---|---|---|
| (a)+(b) | the loop re-freezes after an applied cancel, shortest `Freeze StartTick StepTick Cancel`; the move's own 422 "cancelled" while `moving`, the path #254's test takes | 2, 3 |
| the row-5 rule of (a) | a request cancelled under it freezes again and moves after the acknowledged cancel | 1 |
| (b) | a 422 "cancelled" whose cancel write was refused | 3 |
| the `NoChange` advance | a cancel by `apply` answered 204 while the state reads `moving` | 3 |
| the one-run gate | a request answers `409 event-move-cancelled` for a cancel the loop made under it | 4 |

**Beyond the two interleavings held before the fix** (the loop's resume after
an applied cancel, and after the move's own cancel), it found three:

- the request that freezes again after a cancel under it;
- the cancel answered with no effect;
- the `NoChange` answer.

The plan listed the first two. It did not list the third, which is now fixed.
A fourth, the loop and a request running one move together, was found by
repeating the flaky test under load. The model did not find it until the
fourth invariant was added, because with only the first three invariants the
answer's reason was never checked.

**The model was corrected three times during development; no correction was
in the code.**

- A move request in flight at a cancel was first counted as started before
  it. A request that has not yet read the state is concurrent with the
  cancel, and may move after it.
- That relaxation then excused a request that had read the state. It is now
  counted concurrent only before its first read.
- With the gate, a request can be held while waiting for a run before it
  reads anything, so the seam now says where it holds a flow.

## Durations

| Suite | OS | Before, `main` at `c754bcb` | After, `a247813` |
|---|---|---|---|
| Server, first start to last end (`build`) | Linux | 9.9 s (266 results) | 9.9 s (275 results) |
| Server, first start to last end (`build`) | Windows | 14.2 s (266 results) | 12.8 s (275 results) |
| Fleet (services), Azurite and RustFS | Linux | 1 min 38 s (38 results) | 1 min 59 s (38 results) |
| Handover (services) | Linux | 34 s (14 results) | 36 s (14 results) |

One run each. The fleet suite's 21 s more is not attributed: its tests do
not move event declarations, so the advance-first step does not run in them,
and one run is too few to tell a change from the runner. The model test adds
about 2 s at 40 schedules, within the server suite's time above.

`IdleFleetTests` ran unchanged and passed. The fix adds no commit on a timer:
the loop's resume now commits less than before, never more.

## Issues

- **Closed by trailer, once landed:** #254 (`Closes #254` on the close-out
  commit).
- **Closed as a duplicate:** #217, with the reason.
- **Opened:** [#367](https://github.com/Hafeok/Varve/issues/367), the
  silent give-up of `AppliedAsync` on the forwarded path. It stays its own
  issue.

## Left for the maintainer

- Accepting the three keys of ADR 0167's amendment, which turns the last
  commit green.
- `IManagementPlane` gains two default members (`EnterMoveAsync`,
  `TryEnterMove`) and `EventCatalogueValidator` a constructor: unshipped
  surface in `Varve.Protocol`, grown, not broken. `Varve.Protocol.Client` is
  unchanged.
- Whether the `NoChange` fix should have been its own issue. It is recorded
  on #254, and is in this pull request as a commit of its own.
