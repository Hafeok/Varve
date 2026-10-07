# The file suites without the device flush (ADR 0090)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#10](https://github.com/Hafeok/Varve/issues/10) |
| **Date** | 2026-10-07 |
| **Tool** | Claude Code 2.1.292, a cloud session started from the Android app |
| **Model** | as configured and served for the session below, from its own metadata (`get_session`) |
| **Session identifier** | `session_01JZjxTGanMjuGR2KWbPjWRi` |
| **Branch** | `ccr-73c21edc-57a4zy`, from `main` at 50045b2 (#67 merged) |

## The prompt

> After #67 merges, own PR: a test-only IFileSystem wrapper over the real file system whose Flush is a no-op, used by the file property tests, bulk-load, derived and FileStorage suites; the fault-injection suite and the durability job keep real flushes and the ADR lists them by name. Add the Defender exclusion step for the workspace and temp folder on windows-latest, measured on its own. Measure Test Varve.Store on the Windows runner before, after option 1, and after both; file the ADR unaccepted with the numbers.

## The report

**Why.** On `windows-latest`, `Test Varve.Store` took 9 min 37 s to 11 min
59 s against 2 to 4 minutes on Ubuntu; every other step of the build job was
within seconds. The durability job measures a flush on that runner at 3.4 ms
at the median, and the file backend flushes once a commit (ADR 0073).

**What changed.**

- `tests/Varve.Store.Tests/UnflushedFileSystem.cs`: `DiskFileSystem` with a
  no-op `Flush`, internal to the tests. `TemporaryDirectory(flushes: false)`
  opens its storages on it, and `OpenForCallerAsync` gives one the caller
  disposes. On it: `FilePropertyTests` (eleven directories),
  `FileStorageContractTests`, and the two `DerivedTests` on disk.
- `build (windows-latest)`: a first step adding the workspace, `RUNNER_TEMP`
  and the user's temporary folder to Defender's exclusions.
- ADR 0090 and its decision set, filed unaccepted; `docs/testing.md` §6.

**Measured** (step times from each job's record; one run each):

| | Commit | Windows | Ubuntu |
|---|---|---:|---:|
| Before, `main` after #67 | `50045b2` | 11 min 42 s | 3 min 45 s |
| After the wrapper | `8c210e0` | 5 min 31 s | 3 min 39 s |
| After the wrapper and Defender | `db4edaf` | 6 min 1 s | 3 min 37 s |

Earlier before-runs, for the spread: 9 min 37 s, 11 min 26 s, 11 min 59 s,
10 min 52 s.

**What was found.**

1. **`BulkLoadTests` has no storage on the real disk.** It runs on
   `MemoryStorage` and `SimulatedFileSystem`, whose flushes the crash models
   rest on; the wrapper has nothing to replace there, so the brief's "bulk-load"
   is met by leaving it, and the ADR lists it among the suites that keep their
   flushes.
2. **`FileStorageTests` stays flushed**: it opens through the public
   `FileStorage.OpenAsync`, the entry a host uses, with ten to thirty commits
   a test.
3. **Defender's exclusion shows no gain** on its own run (6 min 1 s against
   5 min 31 s), inside the runner's spread. Kept as the brief asked, with the
   result stated for the maintainer's call at acceptance.

**Tests.** Store suite 156 passed, 1 skipped, locally; `eng/ci.cs` 31 of 31
jobs on the PR's first head; CI green on every head apart from `agent review`,
which waits on the responsible human's approval.

**Left.** Acceptance of ADR 0090, and keeping or dropping its Defender ruling.
