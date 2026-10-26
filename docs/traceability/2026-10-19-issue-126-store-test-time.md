# Why `Test Varve.Store` was slow: measured first, then cut

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#126](https://github.com/Hafeok/Varve/issues/126) |
| **Pull request** | [#136](https://github.com/Hafeok/Varve/pull/136) |
| **Dates** | 2026-10-19 to 2026-10-23 |
| **Tool** | Claude Code 2.1.303, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01YWZaiBxSK7r6aizeG5YWo3` |
| **Branch** | `claude/varve-store-perf-measure-hcl8ga`, from `main` at 9d48ccc; `main` merged in at 8faf8dc and 10c11df |
| **ADRs** | 0007 amended (`Microsoft.Testing.Extensions.HangDump`, ruling `HangDumpForTheStoreTests`, filed unaccepted) |

## The prompts

> # Session: why `Test Varve.Store` is slow, measure first (#126)
>
> You are working in github.com/Hafeok/Varve, from current `main`. `AGENTS.md` applies in full.
>
> **Read first:** issue #126 and its comment of 2026-10-19, which has the step durations before and after #116 and the scheduling gaps in the logs. Also read `.github/workflows/ci.yml` (every `dotnet test` step, and also `azure-objects.yml` and `publish.yml`, which run the same project), `tests/Varve.Store.Tests/` and ADR 0007 (the dependency register).
>
> **The facts so far:**
> - The `Test Varve.Store` step took 8–10 min on Windows and 5–7 on Ubuntu before #116. Since then it takes 27–38 min on Windows (once 54) and 10–16 on Ubuntu, and every PR pays it.
> - The logs show a few long classes holding every parallel slot. On Windows, other classes weren't scheduled until 14 minutes in, followed by a 19-minute tail with almost nothing printed.
> - A local run found `a_crash_during_recovery_recovers` (file backend, a fault test from before #116) taking 12 minutes.
>
> Post a short plan, then do it. Don't wait for approval unless a step below changes what a test proves.
>
> ## Work, in this order
>
> 1. **Measure before changing anything.**
>    - Add `--report-trx` to every `dotnet test` step in `ci.yml`, `azure-objects.yml` and `publish.yml`. `Microsoft.Testing.Extensions.TrxReport` is already referenced.
>    - Upload the `.trx` files as artifacts on every run, pass or fail.
>    - Add a step that writes the 20 slowest tests per OS into the job summary. Use a small `eng/` script, with no new dependency.
>    - Push, let CI run on the PR, and post both tables in the PR. Every later step reports against them.
> 2. **Guards.**
>    - Add `timeout-minutes` on each step that runs the store tests, at about 2× the slowest green run: roughly 110 for the Windows build, 35 for Ubuntu, and 10 for `storage (services)`. Recompute these from your own measurements in step 1 and state the numbers.
>    - Add `--hangdump --hangdump-timeout 15m` from `Microsoft.Testing.Extensions.HangDump`, with its register entry under ADR 0007 (test-time only), and upload the dump as an artifact.
> 3. **Cheap Windows fixes,** one commit and one measured CI run each:
>    - point `TEMP` and `TMP` at `${{ runner.temp }}` (the D: drive) for the test steps;
>    - exclude the workspace and runner temp from Defender scanning with `Add-MpPreference -ExclusionPath`, in a Windows-only step.
>    - Report each one's effect on the Windows step, and keep only those that help.
> 4. **The slow tests themselves.** For every test over 60 s on either OS in step 1's tables, say where the time goes: iteration count, fsync, sleeps, retry delays, container round trips, or serialisation in a shared fixture. Then, per test, do one of two things:
>    - **fix it,** when the time is a defect: a sleep, a retry back-off, an unbounded loop, or a fixture rebuilt per test;
>    - **cut its land-gate iterations,** keeping the full count behind an environment variable (`VARVE_*_ITERATIONS` or similar, documented in `docs/testing.md`). A new nightly workflow on `main` sets those variables. That's the run #129 can later make a release gate.
>
>    **Never change what a test proves,** only how often it proves it on a PR. If you think a test's property is wrong or redundant, stop and say so.
> 5. **Parallelism.** If the TRX shows one class serialising the tail, check whether it's in a collection that doesn't need to be serial. Split it if its tests are independent. Say what you changed and why.
>
> ## Target and report
>
> - **Targets:** the store step under 12 min on Windows and under 6 on Ubuntu, on the PR.
> - **Report** in the PR and the traceability record:
>   - before and after per OS;
>   - the top-20 tables before and after;
>   - each change's measured effect;
>   - which tests moved iterations to the nightly run, with their PR and nightly counts.
> - **If the target isn't reachable** without changing what a test proves, report the remaining offenders and stop.
>
> ## Standing rules
>
> - **One PR, no release descriptor.** Commits reference #126, and the last carries `Closes #126` as a trailer. No closing keyword in the PR description (#108).
> - **Commits** are signed with the agent SSH key, carry the DCO sign-off, and never name a model.
> - **A `docs/traceability/` record** (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml` (#118). The nightly workflow adds no new image.
> - **Land through `land/`.** If #135 (Extent maintenance) lands first, re-measure, since it adds store tests, and include its tests in step 4.

After the baseline, the flush trace and the queue were reported, the maintainer:

> Good progress. Two points before you push further.
>
> 1. **The 88,146 flushes in `ObjectFaultTests`.** Find which writes they are. If they're the object backend's local cache (pages, extent blobs, the commit index, the reader id file), the cache needs no durability: a torn or missing cache file is a cache miss (ADR 0072, 0139), and the durable copy is in the object store. In that case, flushing is a defect in `Varve.Store.ObjectStorage`, and it costs production too.
>    - Remove the flushes for cache writes, keeping any that a correctness argument needs, and state that argument.
>    - Add a test that a cache file truncated or zeroed at any byte is a miss, not an error or a wrong result.
>    - Report the flush count and the Windows time with this fix and with the `TEMP` change, separately.
>    - If the flushes turn out to be on a path that does need durability (the file backend's own log, a staged upload before its create), say which, and leave them.
> 2. **The stride for `a_crash_during_recovery_recovers`.** Don't use a fixed offset. On PRs, use stride 25 with the offset `seed mod 25`, the seed printed in the test output, and `VARVE_FAULT_RECOVERY_SEED` to replay it. PR runs then sweep every crash point over time, and the nightly run (stride 1) covers all of them every night.
>
> The traceability record names the tool and the model, as ADR 0033 requires. Commit messages still never name a model.
>
> Carry on with the push order as you have it, with point 1 inserted after the `TEMP` measurement.

On the `TEMP` run:

> Run 39412157646 on `8faf8dc` finished green: Windows store step 21.0 min, Ubuntu 10.6 min (baseline `1d543f2`: 32.7 and 15.9). Nothing has been pushed since.
>
> - **Post the test-by-test comparison** against the baseline from that run's TRX, top 20 per OS, in the PR. Ubuntu dropped too, so attribute how much of each OS's drop comes from `TEMP` and how much from the merged commits.
> - **Push the flush fix** on its own as the next measured run. Report the flush count before and after, and the step times.
> - **Then the rest** of the queue in your order, one measured run each.
> - The target is unchanged: under 12 min on Windows and under 6 on Ubuntu.

Later:

> any reason its in draft the PR ?

> keep it draft, carry on

> every time you push a new commit i have to approve - its getting to be an issue. The approval was supposed to be a gate for my approving into main not gating your progress

> carry on

## The report

### Before and after

`Test Varve.Store`, from the step's start and end in the job record, one run per
commit. Runner speed moved the same tests by up to a factor of two between
runs (below), so each change is also judged test by test from the TRX.

| # | Commit | Change | Windows | Ubuntu | storage (services) |
|---:|---|---|---:|---:|---:|
| 1 | `1d543f2` | TRX and the durations summary: **the baseline** | **32m 41s** | **15m 52s** | 4m 12s |
| 2 | `5db693c` | time limits and hang dumps | 26m 26s | 16m 48s | 4m 33s |
| 3 | `8faf8dc` | `TEMP`/`TMP` on `D:`, merged with `main` | 21m 00s | 10m 36s | 5m 04s |
| 4 | `2c2e49c` | the owner and ignore files without a flush | 25m 29s | 15m 07s | 3m 55s |
| 5 | `6a990df` | the object cache's damaged-blob test | 25m 23s | 16m 43s | 4m 31s |
| 6 | `982a1de` | one crash per operation builds every cut | 20m 35s | 15m 09s | 4m 25s |
| 7 | `00de133` | the crash during recovery at stride 25 | 11m 22s | 8m 19s | 2m 41s |
| 8 | `10c11df` | the fault suite split by class, merged with `main` (Extent maintenance) | 10m 09s | 7m 35s | 5m 18s |
| 9 | `b8931c0` | the recovery stride's seeded offset | 8m 24s | 7m 13s | 4m 21s |
| 10 | `fe5e643` | TRX for the steps `main` added | 10m 34s | 5m 07s | 5m 03s |
| 11 | `00a2a61` | the damaged-blob test at an eighth | 10m 27s | 7m 01s | 5m 46s |
| 12 | `5610816` | the process, batched and power-loss searches at half | 6m 40s | 3m 53s | 4m 28s |
| 13 | `b682a62` | the object fault search at half; the nightly depths | **8m 52s** | **5m 17s** | 4m 44s |

**Both targets are met**: under 12 minutes on Windows and under 6 on Ubuntu, on
both runs of the final tests (rows 12 and 13). `ac143f8` (the split) and
`cf70478` (the nightly workflow) have no row: `main` had moved and the pull
request could not merge, so CI did not run on them; row 8 measures the split.

### The top 20, baseline (row 1) and final (row 13)

The baseline's tables are on the pull request (the comment of 2026-10-19). The
final run against them, test by test; a class split by this work is matched
under its outer name:

#### windows-latest

First start to last end: 32m 34s → 8m 45s. Busy time of the 298 tests in both
runs: 88m 25s → 30m 15s; the 41 tests added since (Extent maintenance, this
work): 3m 02s. One baseline test was renamed by `main`.

| # | Test | Baseline | Final | Change |
|---:|---|---:|---:|---:|
| 1 | `ObjectFaultTests` · `after_a_fault_at_any_request_…(seed: 9201)` | 32m 30s | 7m 34s | −77% |
| 2 | `FaultInjectionTests` · `a_crash_during_recovery_recovers` | 11m 03s | 3m 58s | −64% |
| 3 | `ObjectReaderScheduleTests` · `a_fault_at_any_write_or_delete_…` | new | 2m 24s | — |
| 4 | `FaultInjectionTests` · `a_process_crash_anywhere_…(6102, 24)` | 2m 01s | 1m 36s | −20% |
| 5 | `FaultInjectionTests` · `a_power_loss_…(6201, 24, 6)` | 1m 11s | 1m 15s | +6% |
| 6 | `FaultInjectionTests` · `a_power_loss_…(6202, 24, 6)` | 2m 31s | 1m 14s | −51% |
| 7 | `ObjectExtentTests` · `a_fault_at_any_extent_write_or_catalog_swap_…` | 2m 23s | 1m 07s | −53% |
| 8 | `ObjectPropertyTests` · `…derived_runs_and_segments_consolidated` | 3m 16s | 1m 05s | −67% |
| 9 | `ObjectPropertyTests` · `a_log_in_objects_cut_…(64, 1024)` | 4m 12s | 45.9s | −82% |
| 10 | `ObjectPropertyTests` · `the_store_in_objects_agrees_with_the_reference_model` | 2m 29s | 41.3s | −72% |
| 11 | `FaultInjectionTests` · `a_crash_inside_a_batch_…(6151, 32)` | 2m 14s | 39.2s | −71% |
| 12 | `FaultInjectionTests` · `a_crash_inside_a_batch_…(6152, 32)` | 2m 06s | 36.5s | −71% |
| 13 | `ObjectPropertyTests` · `a_log_in_objects_cut_…(1048576, 67108864)` | 2m 56s | 36.2s | −79% |
| 14 | `LogPropertyTests` · `a_log_cut_at_any_byte_…(64, 1024)` | 26.8s | 32.3s | +21% |
| 15 | `FilePropertyTests` · `the_store_on_files_agrees_with_the_reference_model` | 2m 24s | 31.3s | −78% |
| 16 | `BulkLoadTests` · `a_bulk_load_equals_…` | 32.7s | 28.3s | −13% |
| 17 | `FilePropertyTests` · `a_log_on_files_cut_…(64, 1024)` | 1m 15s | 26.8s | −64% |
| 18 | `FilePropertyTests` · `…with_the_projection_in_derived_runs` | 1m 27s | 24.4s | −72% |
| 19 | `PerGraphConcurrencyTests` · `the_graph_version_index_rebuilt_…(9301)` | 1m 04s | 24.1s | −63% |
| 20 | `FaultInjectionTests` · `a_directory_copied_mid_write_opens_on_the_copy` | 10.2s | 23.7s | +132% |

#### ubuntu-latest

First start to last end: 15m 47s → 5m 13s. Busy time of the 298 tests in both
runs: 24m 36s → 19m 15s; the 41 added since: 58 s.

| # | Test | Baseline | Final | Change |
|---:|---|---:|---:|---:|
| 1 | `FaultInjectionTests` · `a_crash_during_recovery_recovers` | 6m 36s | 4m 55s | −25% |
| 2 | `FaultInjectionTests` · `a_crash_inside_a_batch_…(6151, 32)` | 2m 29s | 1m 55s | −22% |
| 3 | `FaultInjectionTests` · `a_crash_inside_a_batch_…(6152, 32)` | 1m 27s | 1m 17s | −11% |
| 4 | `ObjectFaultTests` · `after_a_fault_at_any_request_…(seed: 9201)` | 1m 29s | 1m 10s | −21% |
| 5 | `FaultInjectionTests` · `a_power_loss_…(6201, 24, 6)` | 44.8s | 54.7s | +22% |
| 6 | `LogPropertyTests` · `a_log_cut_at_any_byte_…(64, 1024)` | 59.5s | 43.5s | −27% |
| 7 | `FaultInjectionTests` · `a_power_loss_…(6202, 24, 6)` | 40.3s | 35.3s | −13% |
| 8 | `FaultInjectionTests` · `a_process_crash_anywhere_…(6102, 24)` | 1m 07s | 33.4s | −50% |
| 9 | `FaultInjectionTests` · `a_process_crash_anywhere_…(6101, 24)` | 54.6s | 29.2s | −46% |
| 10 | `ObjectPropertyTests` · `the_store_in_objects_agrees_with_the_reference_model` | 28.3s | 26.6s | −6% |
| 11 | `FaultInjectionTests` · `a_lost_directory_entry_is_a_segment_never_created` | 52.1s | 26.0s | −50% |
| 12 | `ObjectPropertyTests` · `a_log_in_objects_cut_…(64, 1024)` | 31.5s | 25.2s | −20% |
| 13 | `LogPropertyTests` · `a_log_cut_at_any_byte_…(1048576, 67108864)` | 21.9s | 23.9s | +9% |
| 14 | `ObjectReaderScheduleTests` · `a_fault_at_any_write_or_delete_…` | new | 23.4s | — |
| 15 | `FaultInjectionTests` · `a_directory_copied_mid_write_opens_on_the_copy` | 38.1s | 20.4s | −46% |
| 16 | `BulkLoadTests` · `a_crash_at_every_operation_of_a_load_…` | 16.8s | 18.2s | +8% |
| 17 | `FaultInjectionTests` · `a_crash_while_checkpoints_…_leaves_no_orphan` | 6.5s | 17.3s | +168% |
| 18 | `ObjectCacheTests` · `a_commit_index_blob_cut_short_…_is_a_miss` | new | 16.5s | — |
| 19 | `FaultInjectionTests` · `derived_data_deleted_or_stale_after_a_crash_is_rebuilt` | 6.4s | 16.1s | +149% |
| 20 | `LogPropertyTests` · `a_change_covered_by_a_later_commit_…_refuses` | 5.1s | 15.0s | +192% |

The tests that rose did not change. With the fault suite split into eight
classes, they run in parallel with the others instead of after them, and so
share the four cores: their durations include waiting for one. On Ubuntu the
step is now bound by the runner's cores, not by one class.

### Each change, and what it did

- **The measurement** (`1d543f2`). Every `dotnet test` step in the three
  workflows writes a TRX, and every job that tests uploads it, pass or fail.
  `eng/test-durations.cs` (BCL only) writes the 20 slowest tests, and the
  classes by busy time with the minutes into the run at which each started and
  ended, to the job summary. It is what found everything below.
- **Time limits and hang dumps** (`5db693c`, `e4b08c7`). First 65 and 35
  minutes, from the baseline; now 20 and 12, twice the slowest green run of
  the final tests, with 10-minute hang dumps in the build and publish steps,
  and 10 and 5 in the services steps. Checked with a test made to hang and a
  20 s limit: the log named it, a dump was written, and the run exited 7. No
  effect on time, as expected (row 2 is noise).
- **`TEMP` and `TMP` on `D:`** (`691322b`, measured at `8faf8dc`). The single
  largest change: **about 9.1 of the 11.7 minutes Windows dropped** in row 3.
  The tests on the real temporary folder fell 80–90%: `ObjectFaultTests`
  32m 30s → 4m 25s, `ObjectPropertyTests`, `FilePropertyTests`,
  `ObjectExtentTests`. Their caches and dataset directories were on `C:`,
  the system disk; per-flush cost there was not measured, only the drop. Kept.
- **The Defender exclusion** was already in the build job (ADR 0090) and was
  **not measured again**: since `TEMP` the test files are under `RUNNER_TEMP`,
  which it already excludes, and its removal would contradict ADR 0090. ADR
  0090's own run found no gain from it.
- **The lease's owner and the ignore file without a flush** (`2c2e49c`).
  `ObjectFaultTests`' flushes fell from 88,073 to 70,908 (strace, locally):
  8,478 each for `LOCK.owner` and `.gitignore`, at every open of the object
  backend's local cache. **Not resolvable in CI**: the run's runners were
  slower across the board, and the same tests varied more than the fix could
  move them. Locally, on one disk, `ObjectFaultTests` alone went from 2m 11s
  to 1m 50s. It also removes two device flushes from every open in
  production. The ignore file is now rewritten at open when it is not whole,
  so a power loss cannot leave `derived/` in version control (tested).
- **One crash per operation** (`982a1de`). A defect in the tests: the process
  and batched crash searches replayed the whole workload once per byte at
  which they cut a write. One replay per operation now builds every cut's
  image, byte for byte the one a crash at that cut leaves, which
  `an_image_cut_from_one_crash_is_the_image_of_a_crash_at_that_cut` holds
  (and fails when the cut is one byte short). Row 6: process crash −49% to
  −62%, batched −7% to −52%, the same points checked.
- **The class split** (`ac143f8`, measured at `10c11df`). `FaultInjectionTests`
  held 13 independent tests in one class, which xunit runs in series: it was
  the whole of the Ubuntu step (busy from minute 0.1 to 15.8 of 15.8). Its
  tests now sit in eight nested classes by kind of fault, under the same name,
  which ADR 0090 and `docs/testing.md` cite. With the recovery stride, row 7 to
  row 8 took Ubuntu from 8m 19s to 7m 35s; the step became bound by its cores.
- **Iterations moved to the nightly run** (`00de133`, `b8931c0`, `00a2a61`,
  `5610816`, `90b3448`), below. Rows 7 and 12 are where the targets fell.
- **The nightly run** (`cf70478`, `b8931c0`, `b682a62`): `.github/workflows/nightly.yml`,
  every night on `main`, Ubuntu and Windows, every depth variable set, no
  container, a 150-minute limit and a 100-minute hang dump (the crash during
  recovery runs alone for most of an hour at stride 1).

### Tests over 60 s, where the time went, and what was done

| Test | Over 60 s (baseline) | Where the time went | Done |
|---|---|---|---|
| `ObjectFaultTests` | 32m 30s Windows, 1m 29s Ubuntu | the local cache on `C:`; then 70,908 commit index publishes, each flushed | `TEMP`; the owner and ignore flushes removed; every second request on a PR |
| `a_crash_during_recovery_recovers` | 11m 03s, 6m 36s | 57,679 points, each a full recovery verified (profiled: replay 2.4 s, verification the rest) | stride 25 from a seeded offset on a PR |
| `a_crash_inside_a_batch_…` (2) | 2m 14s, 2m 29s | replaying the workload per cut (half), verification | replay fixed; every second operation on a PR |
| `a_process_crash_anywhere_…` (2) | 2m 01s, 1m 07s | replaying per cut (two thirds), verification | replay fixed; every second operation on a PR |
| `a_power_loss_…` (2) | 2m 31s, 44.8s | as-of reads in verification (63 of 118 s locally) | every second operation on a PR |
| `ObjectPropertyTests` (5), `FilePropertyTests` (3), `ObjectExtentTests`, `PerGraphConcurrencyTests` | Windows only, 1m 02s to 4m 12s | the temporary folder on `C:` | `TEMP`: all now under 70 s |
| `ObjectReaderScheduleTests` (Extent maintenance) | 2m 16s Windows at `10c11df` | the local cache on disk, as `ObjectFaultTests` | left as its milestone sized it; the nightly run sets its release-gate depths |
| `AzuriteFaultTests` (services) | 3m 27s | container round trips over HTTP; already sized by `VARVE_HTTP_FAULT_STEPS` | unchanged; outside the store step's target |
| `ObjectCacheTests` (this work) | 1m 59s, 2m 14s at `982a1de` | a cache copy and a reopen per damaged blob | an eighth of the cases from a seeded offset on a PR |

No sleep, retry back-off or unbounded loop was found in any of them.

### Iterations moved to the nightly run

Each moved search keeps its property; a pull request searches a share of the
same points, from where a seed says, so that pull requests between them search
all of them, and the seed starts the test's output and every failure's
message. `docs/testing.md` section 8 lists them.

| Test | Variables | Pull request | Nightly |
|---|---|---|---|
| `a_crash_during_recovery_recovers` | `VARVE_FAULT_RECOVERY_STRIDE`, `_SEED` | every 25th operation, about 11,500 points | every operation, about 288,000 (the gate's was every fifth, 57,679) |
| `a_process_crash_anywhere_…` | `VARVE_FAULT_PROCESS_STRIDE`, `_SEED` | every second operation, about 4,400 and 5,200 points | every one, about 8,800 and 10,500 |
| `a_crash_inside_a_batch_…` | `VARVE_FAULT_BATCH_STRIDE`, `_SEED` | every second, about 6,500 and 7,000 | every one, about 13,000 and 14,000 |
| `a_power_loss_…` | `VARVE_FAULT_POWER_STRIDE`, `_SEED` | every second, about 4,300 and 3,900 | every one, about 8,600 and 7,900 |
| `ObjectFaultTests` | `VARVE_OBJECT_FAULT_SWEEP_STRIDE`, `_SEED` | every second request, 532 of 1,068 points | all |
| `ObjectCacheTests` | `VARVE_CACHE_DAMAGE_STRIDE`, `_SEED` | 69 of 552 cases | all 552 |
| Extent maintenance (section 7) | `VARVE_EXTENT_SCHEDULES`, `VARVE_READER_SCHEDULES`, `VARVE_EXTENT_FAULTS_FULL` | 40, 60, every eighth point read | 400, 1,200, every point read |

### What was found

- **A torn commit index blob in the object backend's cache is not a miss.**
  Below the catalog, a cached blob is accepted when its last entry matches the
  catalog's; the 23 entries before it are not checked against the log, by ADR
  0139's design. Zeroing one byte in them gave a different history by time
  (the timestamp, the canonical counter) or a `LogVerificationException`
  (the header hash, the segment and offset) in 985 of 1,920 cases. A crash
  cannot produce such a blob, because the blob is published by a temporary
  file flushed before its rename, so **that flush stays**: it is the
  correctness argument for 70,908 of `ObjectFaultTests`' flushes. Bit rot, or a
  file system that does not honour the flush, could still produce one.
  Verifying the entries (a body hash, which is a derived format change, or a
  check against the log, which ADR 0139 avoids) is a decision for an ADR,
  not for this work. `ObjectCacheTests` holds the cases that are a miss: a cut
  at any byte, and a zeroed byte of the last entry, the directory or the
  derived header.
- **The runners vary by up to a factor of two.** The same tests ran 30% faster
  on Ubuntu in row 3 than in row 1, and 20–55% slower in row 4 than in row 3,
  with no code change between them that they measure. A Windows runner's disk
  and CPU vary separately. One run cannot resolve a change of 20%; the per-test
  TRX, normalised by in-memory tests on the same runner, or a local run on one
  machine, can.
- **CI does not run on a pull request that cannot merge.** Twice `main` moved
  under this branch and two pushes went unmeasured until `main` was merged in.

### What is left

- `ObjectFaultTests` is still the Windows step's tail, between 4 and 10
  minutes; it is disk-bound in the commit index publishes that stay.
- The commit index's unverified entries (above), for an ADR.
- The ADR 0007 amendment's ruling, `HangDumpForTheStoreTests`, is filed
  unaccepted (ADR 0066).
- The nightly run has not run yet: it runs on `main` once this lands.
