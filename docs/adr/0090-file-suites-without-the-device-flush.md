# 0090 — The file suites run without the device flush; Defender skips the build's files

## Status

**Proposed — filed unaccepted by the Windows test-time slice of #10,
2026-10-07** (ADR 0066). Acceptance is the maintainer's act on the pull
request.

Decided by the maintainer after #67: "a test-only IFileSystem wrapper over the
real file system whose Flush is a no-op, used by the file property tests,
bulk-load, derived and FileStorage suites; the fault-injection suite and the
durability job keep real flushes and the ADR lists them by name", and "the
Defender exclusion step for the workspace and temp folder on windows-latest,
measured on its own". It changes what a test on the file backend proves, which
is why it is an ADR.

**Revisit condition:** a defect that only a flushed run on the real device
would have shown; or a Windows runner whose flush costs what Linux's does.

## Context

`Test Varve.Store` took **9 min 37 s to 11 min 59 s on `windows-latest`**
against **2 to 4 minutes on `ubuntu-latest`** (table below); every other step
of the build job is within seconds across the two. The file backend flushes on
every commit, as ADR 0073 requires, and the durability job measures that flush
on the Windows runner at **3.4 ms at the median, 17 ms at the 99th
percentile** (`FlushFileBuffers`, `eng/durability.cs`, `main` at `9f06f2c`).
The store's file suites drive many thousands of commits through real
directories: the model properties re-run on files, the storage contract on
files, and the derived index on disk.

Those suites are on real files for what a simulated file system cannot show:
NTFS's sharing modes, renames over open files, locks, the read-only attribute
of a sealed segment, `CRLF` (ADR 0036). None of them is about what a flush
makes durable. That is proven where it is the subject, and stays there.

## Decision

### A test-only file system whose flush does nothing

`tests/Varve.Store.Tests/UnflushedFileSystem.cs` is an `IFileSystem` over the
machine's (`DiskFileSystem`) whose handles' `Flush` is a no-op; every other
call reaches the disk unchanged. `TemporaryDirectory(flushes: false)` opens
its storages on it. It is internal to the tests: `FileStorage.OpenAsync`, the
public entry, always flushes, and nothing shipped can reach the wrapper.

**On the wrapper:**

- `FilePropertyTests` — milestone 4's properties re-run on files, the derived
  rebuild, concurrent readers beside a writer, copies taken while written.
- `FileStorageContractTests` — the storage contract on files.
- `DerivedTests` — the two that run on disk: a pin across a merge on files,
  and a missing `derived/` rebuilt from the log.

**Keeping real flushes, by name:**

- **`FaultInjectionTests`** — every write path crashed at every operation
  under process crash, power loss and lost directory entries. Its
  `SimulatedFileSystem` loses what was not flushed, so its flushes are the
  subject.
- **`BulkLoadTests`** — the bulk loader crashed before each of its operations,
  on the same `SimulatedFileSystem` (testing.md §6). It has no storage on the
  real disk, so the wrapper has nothing to replace there; its flushes stay
  the simulated ones the crash models need.
- **The durability job** (`eng/durability.cs`, `durability (…)` on Ubuntu,
  Windows and macOS) — appends and flushes to the device and reports the cost.
- **`FileStorageTests`** — the layout on disk, the lease, the key store and
  unpublished writes, through the public `FileStorage.OpenAsync`, so the
  public entry is exercised as shipped. Ten to thirty commits each.
- **`ReplicaTests`, `MaintenanceTests`, `AllocationTests`** — one directory
  each, not in this decision's scope; unchanged.

### Defender skips the build job's own files on Windows

The build job's first step on `windows-latest` adds the workspace, the
runner's temporary folder and the user's temporary folder (where every test
dataset directory is created) to Defender's exclusions. Its own commit, so its
effect is measured apart from the wrapper's.

## Measured

`Test Varve.Store`, `build (windows-latest)`, from the step's start and end in
the job's record; `ubuntu-latest` beside it.

| Run | Commit | Windows | Ubuntu |
|---|---|---:|---:|
| Before: `main`, #66 merged | `777fb22` | 9 min 37 s | 3 min 22 s |
| Before: `main`, #68 merged | `9f06f2c` | 11 min 26 s | 2 min 7 s |
| Before: #67 | `b8696a6` | 11 min 59 s | 3 min 48 s |
| Before: #67, its last head | `85d6df9` | 10 min 52 s | 3 min 6 s |
| **Before: `main`, #67 merged** | `50045b2` | **11 min 42 s** | 3 min 45 s |
| **After the wrapper** | `8c210e0` | **5 min 31 s** | 3 min 39 s |
| **After the wrapper and Defender** | `db4edaf` | **6 min 1 s** | 3 min 37 s |

The whole `build (windows-latest)` job: 15 min 13 s on `main` after #67,
11 min 7 s with the wrapper, 11 min 24 s with both; the Defender step itself
takes 7 s.

**The wrapper halves the Windows store tests** (11 min 42 s to 5 min 31 s,
against a before spread of 9 min 37 s to 11 min 59 s over five runs), and
Ubuntu's does not move (3 min 37–45 s, against 2 min 7 s to 3 min 48 s
before), as expected where a flush is cheap.
**Defender's exclusion shows no gain** on its own run: 6 min 1 s against the
wrapper's 5 min 31 s, inside the runner's spread. One run each; the maintainer
may keep it or drop it at acceptance, and this ADR's third ruling goes with it.
What remains of the gap to Ubuntu, about 2.5 minutes, was not measured by
suite; the candidates are the suites still on the real, flushed disk
(`FileStorageTests`, `ReplicaTests`, `MaintenanceTests`, `AllocationTests`)
and the runner itself.

## Alternatives considered

- **Move the store tests to a job of their own.** The build job would finish
  sooner and the pull request would wait as long.
- **Fewer cases on Windows.** Cheaper, and it weakens what Windows is in the
  matrix for.
- **A public option to skip the flush.** A durability switch on a shipped type
  for the tests' sake; the seam already exists, internal (`IFileSystem`).
- **Unflushed everywhere, the fault-injection suites included.** Their crash
  models are defined by what was flushed; without it they prove nothing.

## Consequences

- The file property tests, the storage contract on files and the derived
  index on disk no longer prove that a commit was flushed; the fault-injection
  suites and the durability job do, as before.
- `Test Varve.Store` on Windows takes about half what it did; the build job
  about four minutes less.
- The Defender exclusion applies to the build job's runner only, which is
  discarded with it.

## Checks

- **Checked against the accepted ADRs** (0001–0089) and specification 1.5.
  Touches **0036** (Windows in the matrix, for the platform's file behaviour,
  which the wrapper keeps), **0073** (durability per host; the file backend
  still flushes), **0025** (the properties run as before, at the same
  iterations). No conflict; nothing shipped changes.
- **Layer ownership.** Tests of `Varve.Store`, layer 4, and the CI workflow.
- **Analyzer rule.** None.
- **Open questions owned.** None.
