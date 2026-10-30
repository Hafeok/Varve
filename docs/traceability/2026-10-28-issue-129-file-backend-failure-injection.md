# File-backend failure injection, a full disk, and the soak gate judged

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#129](https://github.com/Hafeok/Varve/issues/129) (its first deliverable), taking over [#61](https://github.com/Hafeok/Varve/issues/61); [#165](https://github.com/Hafeok/Varve/issues/165) and [#175](https://github.com/Hafeok/Varve/issues/175) referenced |
| **Date** | 2026-10-28 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z-step3`, from `main` at b8ac54b |
| **ADRs** | [0214](../adr/0214-file-backend-failure-injection.md), [0215](../adr/0215-the-soak-gate-run-and-judged.md) |

## The prompt

The session's prompt and the plan's approval are quoted in
[step 2's record](2026-10-27-issue-146-verified-derived-data.md). The
brief's step 3, verbatim:

> **3. File backend failure injection and soak** (#129's first deliverable, taking over #61).
> - **The faults:**
>   - a torn write at every write the backend issues;
>   - a full disk at every allocation;
>   - a process killed at every fsync boundary;
>   - a power cut, simulated by dropping unsynced pages through the file-system seam (`DiskFileSystem`).
> - **After each fault,** the store opens, every committed position reads back exactly, and no uncommitted position is visible.
> - **#61's working-set growth:** report its cause, separating what grows with the dataset from what doesn't, as ADR 0082 asks. Fix what isn't the dataset's own.
> - **Run the soak under the shipped configuration,** long enough to count as evidence. State the duration and why it's enough.
> - These runs go in the release gate and the nightly, not the land gate.

The maintainer's rulings during the step, verbatim:

> 3. Full disk (ADR 0214): the failed commit isn't acknowledged and reads go on, as you have it. Then the next write request after a failed append attempts the reopen itself: if space is back, the write proceeds; if not, it's refused with `507` and a new problem type (propose its name). No timer and no operator restart. Readiness reports writes refused for lack of space without going unready. Add a test that fills the disk, frees it, and shows the next write succeeding with no reopen by hand.
> 4. `release-gate.yml` as its own workflow: fine.

> The full-disk design is approved, and so is superseding `AcknowledgedOnlyWhenTheBatchIsDurable` in part. I'll review ADR 0214 on the PR. One precondition to state in the ruling and to test: "committed" means closed and flushed. On a full disk the flush can fail too. So each member of a failed batch gets one of three answers:
> * closed and flushed: committed;
> * not closed (truncated by recovery): `507 storage-full`;
> * closed, flush not confirmed: outcome unknown, `503` with the request id, resolved through the commits resource, as ADR 0136 does for a lost answer. Never `507`, since the record may survive.
>
> Add properties over generated batches with a full disk at every write and every flush:
> * the committed members are a prefix of the batch, in order;
> * no member answered `507` is found in the log after a reopen;
> * every member answered committed is found there;
> * every outcome-unknown member is resolved by the commits resource, either way.

> Approved as written. The failed-flush rule (no later flush trusted for earlier bytes) is the right call; say in ADR 0214 that it's the fsyncgate behaviour it guards against.
> Two additions:
> 1. One outcome-unknown type. `commit-outcome-unknown` is also the answer for a forwarded write whose outcome is unknown (ADR 0168's lost-answer case). Make its problem page cover both causes. I'll tell #175 to use it instead of a separate `503 unavailable`.
> 2. Retry guidance: the problem page gives the conditional resend on the previous position now, and, once #132 lands, a resend with the same `Idempotency-Key` as the first option. Add a note in #132's ADR to update the page.

> The dictionary-cache bug is a good catch. Add one more property to step 3: after any failed append (full disk or not, followed by recovery or a reopen), every term committed before the failure and every term committed after it reads back as itself. Run it over the batch sweeps' schedules. Post a note on #165 too: a claim in place keeps the standby's dictionary caches, so the same reasoning applies there.

> All good. Before you push step 3, run git submodule update --init --recursive and run Varve.Conformance.Tests and Varve.Sparql.Results.Tests locally. Step 3 touches the commit path that the update suites exercise. Then carry on with your list.

## The report

### What was built

- **The faults at every point** (ADR 0214), each with a seeded share on a
  pull request and all of it at stride 1 in the release gate and the
  nightly (docs/testing.md section 8):
  - torn writes, every write of the backend cut at every byte: 58,168 cuts;
  - every sector cut of every unflushed write, enumerated: 25,376 images;
  - a full disk at every allocation (1,557 points), during recovery (111),
    and inside generated batches at every write and every flush, with an I/O
    error too, followed by the next write's recovery or a reopen (2,369);
  - a real process, the test assembly as its own host, killed before and
    after every flush: 194 kills;
  - a power cut on real files, the image a mirrored simulated file system
    gives under ADR 0073's models: 2,682 images.
- **A full disk recovers by the next write.** The writer tracks what it
  closed and what a returned flush covered. Each member of a failed batch is
  answered by the log: committed (closed and flushed), outcome unknown
  (closed, flush not confirmed: `503 commit-outcome-unknown`, with the
  position and the request id) or refused (never closed: `507
  storage-full`). The next write opens the writer on a scan stopped at the
  head and fences the rest off with an abandoned trailer. Readiness reports
  `writesRefused` and stays ready.
- **The soak gate** (ADR 0215): a bounded vocabulary, the working set by
  cause, `--soak-judge` and its fixtures, four hours on Linux in
  `release-gate.yml`, which `release.yml` requires, and a weekly Windows
  soak that a release requires green and at most eight days old.

### What the work found on the way

- **The term dictionary's caches outlived a failed batch.** Staging gave
  ids to new terms; the commits never landed and the next batch gave the
  same ids to other terms, so a lookup read `p0` back as `s0`. The caches
  are forgotten after a failed append. Removing that line fails the four
  recover-mode sweeps. The same reasoning was posted on #165 for a claim in
  place.
- **A reopen took commits whose flush had failed.** A checkpoint then
  covered one of them, and after a power cut the checkpoint survived and the
  commit's records did not: the log refused to open (seed 6616, flush 48).
  An open now flushes the newest open segment before it builds on it, and
  the file backend's flush of a segment it has no handle on opens one.
- **Settling a failed batch must answer every member.** An exception while
  settling left the batch's members waiting for ever, which the
  object-storage fault sweep showed as a hang; a superseded writer's fence
  was settled against the log instead of failing the batch. Settling now
  never throws past the answers, and only an `IOException` is settled.
- **The kill test's child** needed the xunit runner's own `-method` filter.

### The gates

- `Varve.Store.Tests`: 401 tests, 315 passed, 86 skipped (service-backed),
  none failed, in 5 min 52 s; every new sweep at stride 1 green.
- `Varve.Conformance.Tests` with the W3C submodules: 40 of 11,097 failing,
  the same 40 as `main`'s base; the ratchet reports none regressed.
  `Varve.Sparql.Results.Tests`: 1,058 of 1,058.
- `eng/ci.cs`: register, licence headers, decision sets, banned symbols,
  issue references, DCO, the soak-freshness fixtures, required checks, CI
  images, release pending, changelog and status pass; the soak judge's
  fixtures pass. The build is red only on `CS0618`, the rulings of ADRs 0214
  and 0215 not yet accepted.

### The soak

The eight-hour Linux soak under the shipped configuration, `--soak 480
--policy --subjects 2000`, did not finish here: the container was restarted
at minute 17.5, the meter crashed at minute 45.5 on a file deleted between
its listing and its read (fixed: a vanished file counts as nothing), and the
container was restarted again at minute 111. The 111-minute run answered
#61: the working set does not grow. The samples outside the band were the
collector's committed but unused memory. A second run found retired extent
sets' empty directories accumulating, about 2.7 a minute, now removed with
their last file. ADR 0215's amendment of 2026-10-30 has the numbers.

The maintainer's rulings on it, verbatim:

> Good result: #61's question is answered.
> 1. The band: exclude the collector's committed-but-unused memory, and report it beside the band with its own ceiling in the judge (propose one, such as committed ≤ 2× the live heap after a full collection, plus a fixed margin). Drift catches growth only, above +10%. State the GC mode and settings the soak ran with, and make them the server image's.
> 2. The derived-files bound: write its formula from the configuration (checkpoints kept, files per checkpoint, the maximum number of runs, sets in the chain) into ADR 0215. Set the bound from it, and show 97 inside it. Same for handles, if a formula exists.
> 3. Long soaks: the 4-hour Linux soak in `release-gate.yml`. The 8-hour soak can't run on hosted runners (6-hour job limit), so amend ADR 0215: it runs in #164's Azure environment as a Container Apps job under the shipped configuration, before 1.0. I'll add it to #164.

> Stop the local 4-hour soak; this container can't hold one, and your 111-minute run already answered #61. Instead:
> 1. Push the held changes to #190 now: the meter fix (7f80222), the judge changes (committed memory under its own ceiling, growth-only drift, the derived-files and handle bounds from their formulas), and the ADR 0215 amendment for the 8-hour soak in #164's environment.
> 2. Dispatch `release-gate.yml` on #190's branch with the 4-hour Linux soak, and link the run and its `--judge` verdict in #190's body. That's #190's soak evidence before it lands.

What followed:
- the judge excludes the collector's committed but unused memory (committed
  less the heap) from the band and holds committed memory to twice the live
  heap after a full collection plus 128 MB; drift misses only growth over
  10%; it refuses a run under any collector but the shipped one;
- the bounds on `derived/` files and handles come from the configuration
  (`SoakBounds`): 97 of 182 files and 141 of 309 handles in the 111-minute
  run, which the amended judge holds;
- the shipped collector's settings moved into
  `src/Varve.Server/ShippedRuntime.props`, which the server and the soak
  both import;
- the eight-hour soak runs in #164's Azure environment before 1.0.
