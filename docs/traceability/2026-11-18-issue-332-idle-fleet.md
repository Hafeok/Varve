# An idle fleet's system dataset does not move, and the idle collection runs

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#332](https://github.com/Hafeok/Varve/issues/332); for [#227](https://github.com/Hafeok/Varve/issues/227) and [#264](https://github.com/Hafeok/Varve/issues/264) |
| **Date** | 2026-11-18 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, from the session's own metadata |
| **Session identifier** | `session_01NbWSYPUMnLqwEBi4fBQ1tR` |
| **Branch** | `claude/idle-fleet-332`, from `main` at bb1d142 |
| **ADRs** | none decided; the test holds [0244](../adr/0244-memory-bounds-for-writing-checkpointing-and-reopening.md) point 6 |

## The prompt

The session's prompt covers three pull requests; this record is step 3's.

> Session prompt: three small 1.0 items — reserve `proof/` (#340), absent secrets in repo-standard (#327), the idle-fleet test (#332)
> Paste into a fresh session on `Hafeok/varve`. Read `AGENTS.md` first and follow it in full, then #340, #327 and #332 with their comments.
> Three independent, small changes. One pull request each, in this order. None carries a release descriptor. They may land as one train.
>
> Step 1 — #327: a declared secret may be absent
> `main`'s weekly `repo-standard` job is red because the `auth-providers` environment declares five secrets that do not exist yet (three `VARVE_ENTRA_*`, two `VARVE_GOOGLE_*`). The maintainer has not provisioned those tenants, and ADR 0223 already says the provider leg skips when none of its identifiers are present.
> The maintainer's ruling of 2026-11-17: the five values may be declared as absent.
>
> * The tool's schema types an environment's secrets as a plain list of names, so a name cannot be marked absent today. Extend it so a secret can be declared with its name and that it may be absent.
> * A secret declared so and missing is reported as "declared, absent", and the run is not red for it. A secret declared without that marking and missing is still red.
> * repo-standard never reads or writes a secret's value. That does not change.
> * Mark the five `auth-providers` values, and nothing else.
> * Fixtures: declared and present; declared as may-be-absent and missing; declared plainly and missing (red); an undeclared secret that exists (as today).
> * If the schema change needs a decision, file it unaccepted as a dated amendment to the ADR that holds repo-standard's schema.
>
> Step 2 — #340: reserve `proof/` in storage format 1
> #292 (accepted as a proposal on 2026-11-18, for a minor after 1.0) will add a `proof/` directory beside `log/` and `derived/`. The format freezes at 1.0, so the name is reserved now.
>
> * Find out first, and post it on #340: what each reader of a dataset directory does today with an unknown sibling of `log/` and `derived/`, on the file backend and on an object store: open for read, open for write, a follower, backup and restore, `varve storage`, and the format inventory's gate. The shipper is being built on #338; say what you found there and tell that session on #166, and do not change its branch.
> * Reserve the name in `docs/spec/storage-format.md` and the format inventory (ADR 0216): 1.0 writes nothing there; a reader ignores it and never deletes it.
> * A read-forever fixture: a format 1 dataset with a `proof/` directory holding objects this build does not know. It opens for read and for write, follows and restores, and the objects are byte for byte the same afterwards.
> * A dated amendment to ADR 0216, filed unaccepted.
> * Build no tile, checkpoint, tree arithmetic or route. That is #292.
>
> Step 3 — #332: an idle fleet's system dataset does not move
> ADR 0244's idle collection runs only when the whole server is idle, and the system dataset counts toward that. The delegate read the code and found that nothing commits to the system dataset on a timer; nothing asserts it.
>
> * A test: a fleet of at least a writer and a follower, with no requests, over many `Fleet:FollowInterval` ticks under a controlled clock. The system dataset's head position does not move.
> * On the same fleet: after a checkpoint and the idle wait, the idle collection runs.
> * Sized for the land gate, with no real waiting. Report its duration per OS.
> * If the test fails, that is a finding: stop, post it on #332 and #227, and do not change product code to make it pass without the maintainer's answer.
>
> How this lands
>
> * Issues are the work log. Comment on each issue when you start it. Work you find and don't do becomes its own issue at once.
> * You are a cloud session, not the maintainer's delegate (ADR 0239). File every decision unaccepted. Never write `accepted-by` on a decision set, and never land.
> * Never write the approval line, for any hash, anywhere, including in a report. Report a head as "head `<sha>`", with its hosted result after reading every failed job.
> * Each description carries `Completes: #N` for its issue, and the completing commit `Closes #N`. No closing keyword in a description.
> * ADR status lines read "Accepted — filed unaccepted by …".
> * Commits: as `Claude (Emil) <claude+emil@okkels-klein.dk>`, signed with the agent key. Never `git commit -s`. The co-author trailer is exactly `Co-Authored-By: Claude <noreply@anthropic.com>`; no commit message names a model.
> * Branches: one per pull request, named `claude/<what>-<issue>`. If a push to a new branch is refused, say so on the issue with the exact refusal and do not try another route.
> * When `main` moves, merge it in. Never rebase a pushed branch, and never force-push.
> * No suppression or downgrade of DD or VARVE rules. Never weaken a gate to make a test pass.
>
> Close-out
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record per pull request: the pull requests, issues closed, what each reader does with an unknown directory, the test's duration per OS, and anything deferred as a linked issue.

## The report

- **What the server runs on a timer.** `ServerHost` starts one loop,
  `Fleet.RunAsync`: wait `Fleet:FollowInterval` on the server's clock, advance
  the system dataset and converge the datasets, then finish a move of event
  declarations that a request started. The rest of the server's waits are a
  lease's retries at open, the drain at shutdown, and the idle release's own
  five seconds. Store maintenance writes `derived/`, never a commit.
- **The fleet in one process.** The fleet tests run server processes on
  Azurite or RustFS, in services jobs no ruleset requires. The test is for the
  land gate, so it runs two of the server's own `Fleet` instances, writer `a`
  and follower `b`, over one in-memory object store, under a clock the test
  steps. That needed two seams in `Varve.Server`, neither changing behaviour:
  - `Fleet.StartAsync` has an overload that takes the `ObjectStores` instead
    of building it from the settings, and `ObjectStores.Over` wraps a given
    `IObjectStore`;
  - the heads the idle test compares are `Fleet.Heads()`, which `ServerHost`
    now passes to `IdleRelease.Over` in place of its inline lambda, so the
    test and the host compare the same list.
- **The test**, `IdleFleetTests`:
  - a dataset `d` placed on `a`, with one commit; both interval loops
    running; 600 ticks of one second each (ten minutes of fleet time) with no
    request. The system dataset's head is the same on both instances and its
    `log/` objects in the store are the same names and lengths, before and
    after; so are the heads of every open dataset.
  - on the same fleet: a checkpoint of `d`, then `IdleRelease.WhenIdleAsync`
    over `Fleet.Heads`, its five-second wait being five ticks of the same
    clock with the loops running. It reports idle and releases once. Only
    the wait and the collection are stood in for, as in
    `ObjectStorageServerTests`.
  - a control, `the_stepped_follower_takes_a_system_commit_on_the_next_tick`:
    a declaration committed on `a` reaches `b` on the next tick, so the loop
    the test watches is live.
- **It catches what it is for.** With the writer's loop made to commit a
  heartbeat to the system dataset each tick (tried locally, reverted), the
  test failed: the head was 601 where 1 was expected, which also shows the
  600 ticks ran the loop 600 times.
- **It passed, so there is no finding** for #332 or #227: the delegate's
  reading holds.
- **Duration,** by the TRX, on this session's Linux container: 0.37 s alone,
  0.3 s within the whole server suite run in parallel (257 tests, 197 passed,
  60 skipped for services, 12 s). `ci.yml`'s `build` job gains a "Server test
  durations" step, as the store suite has, so that each OS's log names it.
  Hosted, at head 8337e23e, inside the whole server suite: **0.6 s on
  `ubuntu-latest`** (11th slowest) and **0.6 s on `windows-latest`** (18th).
  The `build` matrix has no macOS leg.
