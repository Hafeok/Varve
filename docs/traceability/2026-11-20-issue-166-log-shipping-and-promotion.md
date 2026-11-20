# Log shipping and promotion: tier 3 (pull request 2 of Replication part 2)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim
> below. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#166](https://github.com/Hafeok/Varve/issues/166), not completed by this; [#97](https://github.com/Hafeok/Varve/issues/97). Opened by this session while doing it: [#384](https://github.com/Hafeok/Varve/issues/384), [#391](https://github.com/Hafeok/Varve/issues/391), [#392](https://github.com/Hafeok/Varve/issues/392) |
| **Date** | 2026-11-18 to 2026-11-20 |
| **Tool** | Claude Code 2.1.325, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYSB3pCh3SLBkJVgjCv` |
| **Branch** | `claude/replication-tier3-166`, stacked on `claude/amazing-johnson-1iatse` (pull request 1, #322) |
| **ADRs** | [0184](../adr/0184-log-shipping.md) and [0185](../adr/0185-promotion-across-copies-and-the-lineage-record.md) filed unaccepted, with sets `log-shipping` and `promotion-across-copies`; 0113, 0119, 0135, 0136, 0160, 0163, 0164, 0165, 0166, 0167, 0168, 0183, 0213 and 0216 amended, unaccepted |

## The prompts

The session's prompt, and the maintainer's answers D1 to D11 to its delta
plan, are verbatim in the record of pull request 1,
[2026-11-16-issue-117-object-store-verification.md](2026-11-16-issue-117-object-store-verification.md).
This is the same session. What the maintainer wrote after it, that this
pull request acts on, follows.

**Starting it,** among other points:

> 4. Start pull request 2 now, on a new branch claude/replication-tier3-166, stacked on #322. Try the push once; if it is refused, say so on #166 with the exact refusal and do not try another route.
> 5. Keep the commit identity as it is. The local demotion of CS0618 is fine as a diagnostic and never committed.

**On three findings of the first draft:**

> 3. The three findings: the first two are accepted as corrections to the plan. The third restates #166's byte-identity gate, and is accepted on one condition: ADR 0184 says what the target holds when the source consolidates a segment after its chunks were shipped, and the tier 3 gate shows a follower of the target reading the same log in that case. Record the restated gate on #166 as a change to its definition of done.
> 4. The D2 addition (the promote request's position is checked) is accepted.
> 5. Say on #166 when ADRs 0184 and 0185 are stable enough to review.

**The rulings of 2026-11-20:**

> Rulings on your decisions of 2026-11-20. ADRs 0184 and 0185 stay
> unaccepted; the architect reviews them in full when you call them
> stable, and decisions 5 and 6 are ruled then.
>
> 1. `Dataset.HeaderHash(Position)`: accepted in principle. On #166, say
>    what it returns past the tip and below retained history, whether it
>    does I/O (then it is async), and whether `varve storage verify`
>    should use it. Put the baseline diff in the description.
> 2. The three readiness states: agreed. Make sure each has a page and
>    shows in `varve status` with its detail.
> 3. `store-unverified` for Azure Blob: agreed. On #166, list every
>    provider's state on the compatibility page today, and say which were
>    verified against a real service and which against an emulator.
> 4. Declaration order: not an ordering in the guide. Build the
>    declaration you offered: a dataset declared on the target fleet as a
>    copy of a source. Its member never creates a dataset under it, and
>    readiness reports that it waits for the first shipping round.
>    Declaring both sides in one apply must work. Add the test.
> 5. `epoch-moved`, `promotion-incomplete` and the standby rule: agreed.
>    Write each into ADR 0185 as a decision.
>
> #384 comes before #338. A shipping target is in that state at every
> segment roll, so the warm standby meets it routinely.

**The answers to two open points of the review, the same day:**

> 1. The compatibility page is not a refusal.
>    - `varve apply` accepts a target whose provider the page does not
>      list as verified, and its answer says in-band that the project has
>      not tested that provider and that shipping starts once the target
>      store holds a verification record.
>    - The run-time gate is unchanged: no verification record of an
>      accepted suite at the store, nothing is written, and readiness
>      says `store-unverified` with what to run.
>    - `store-unverified` is then a readiness state only, unless an apply
>      can still answer it; say which.
>    - Keep the compiled page states for the notice. Change ADR 0184
>      decision 9 and `ATargetIsAVerifiedStoreOrADirectory`, and file a
>      dated amendment to ADR 0183, unaccepted. Say on the pull request
>      that this narrows a refusal and names what replaces it.
>
> 2. Directory targets leave this work.
>    - Remove them from ADR 0184 (decisions 1, 9 and the tests), from ADR
>      0185 decision 2, from the decision sets, and `varve:directory` from
>      the vocabulary. Remove the `directory-not-shipped` state.
>    - A `varve:ShippingTarget` is a store and a prefix. Anything else is
>      refused at apply.
>    - Open a post-1.0 issue for directory targets with what the ADR said,
>      and link it from #166.
>    - `shipping (services)` runs between Azurite and RustFS only.

## The report

**What it does.** A dataset in an object store is kept as a byte-identical
copy in a second store, and an operator can make that copy the dataset.

**Shipping, ADR 0184.**
- `LogShipper` in `Varve.Store.ObjectStorage`. A round ships the log, then
  the extent sets the source's catalog names, then the catalog last. Every
  write on the target is a create-if-absent of the source's bytes, or a
  conditional replace of the catalog.
- It holds the source's sets by a reader record of its own, and removes a
  retired set from the target only once the source has deleted it and no
  reader of the target holds it.
- It resumes from the target's tail and keeps no state.
- It refuses a target whose store holds no verification record.
- `varve:shipsTo` and `varve:ShippingTarget` declare a target: a store and
  a prefix, with the store's provider.
- The server runs one shipper per declared target, off the sequencer, with
  a back-off of its own, and reports each on readiness.
- The source fleet's system dataset is shipped to each target store ahead
  of the dataset commits that refer to it.

**Promotion, ADR 0185.**
- The lineage record says a copy is a replica or promoted.
- `varve:copyOf` declares a dataset a copy in the fleet that serves it.
  That fleet never creates it, and waits for the first shipping round.
- A replica is followed as a warm standby and never claimed. A write to
  one is `409 dataset-is-replica`.
- An operator promotes by request to an instance of the target fleet, or
  with `varve promote --embedded` where no fleet serves the copy. The
  source and the position they state are checked.
- A promotion that fails part way is completed only by promoting again.
- A promoted copy's write watermark ignores the source fleet's management
  positions.

**The gates, over in-memory stores.**
- **Failure injection.** One history of a source with every kind of change
  a round meets. The shipper makes 213 writes and deletes on the target and
  is stopped at each, before the store sees the request and after the store
  applied it. Whatever it left is a prefix of the source's log byte for
  byte, with a catalog that names only what is there, and a shipper started
  again makes the copy whole.
- **The promotion rehearsal,** two fleets in one process, through to the
  old source's shipper stopping.
- **Both sides in one apply,** one fleet shipping to another prefix of its
  own store.

**What the work found.**
- **#384.** A follower could not open a dataset whose newest segment held
  only its opening chunk, or its trailer and no seal chunk. Found by the
  failure-injection gate. Its fix is its own pull request, #393.
- **A waiting copy made its instance not ready,** being reported as
  failed. It has its own state, `awaiting-shipping`.
- **After a promotion every write would have waited** for a management
  position of the source fleet, since the watermark was seeded from the
  head commit whatever fleet wrote it. The test's assertion reads 500 with
  the fix turned off.
- **Two shippers could lose a set between them** under the first rule for
  removing retired sets. The rule now needs the source to have deleted the
  set.
- **The ADRs as first filed gave three shipper states an HTTP status** that
  no request could receive, and showed a target as a blank node, which a
  management graph does not hold.
- **#392,** in passing: a property test of query rewriting drew a seed
  with a real counterexample. Not this work's.

**What changed by ruling.**
- The compatibility page is a notice on an apply, not a refusal.
  `store-unverified` is a readiness state only.
- Directory targets left this work, to #391.
- The header hash accessor is asynchronous.

**Not built by this pull request, and said in its ADRs.**
- Events and audit of a commit another fleet wrote, answered from the
  shipped system dataset.
- The `shipping (services)` job between Azurite and RustFS. While the two
  ADRs are unaccepted every hosted build stops on `CS0618`, and this
  session has no container runtime, so the job could not have been run
  anywhere before review.
- ADR 0184 decision 5's check of a set blob before it is copied. The code
  copies bytes; the question is with the architect.

**How it was verified.** On the session's Linux container, with `CS0618`
not an error on that machine only and never committed: the whole pipeline,
80 of 80 jobs, on heads `a00c2cf3` and `88d5bb02`. The hosted runs of this
branch are red on `CS0618` by ADR 0066.

Developed with AI assistance under human review.
