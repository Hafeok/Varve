# The simulated file system is safe for concurrent writers

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#214](https://github.com/Hafeok/Varve/issues/214) |
| **Date** | 2026-11-19 |
| **Tool** | Claude Code, a cloud session (not the maintainer's delegate, ADR 0239) |
| **Model** | `claude-opus-5-5`, as configured for the session |
| **Session identifier** | `session_01NbWSYQdXxwCu9uLmSmMtCZ` |
| **Branch** | `claude/simulated-file-system-214`, from `main` at c754bcb |

## The prompt

Step 1 of the session prompt "the release gates hold up — the flaky gate job
(#214), a landed descriptor is closed (#358), stale digests caught locally
(#333)". Its step 1, verbatim:

> Step 1 — #214: `SimulatedFileSystem` is safe for concurrent use
> The issue states the fix and the gate; follow it.
>
> * One lock over the double's inode and path tables. Keep its fault and
>   power-cut semantics exact under concurrency, and say on the pull request
>   how the lock sits with the "drop unsynced pages" model.
> * Look for other shared mutable state in `tests/Varve.Store.Tests/Faults/`
>   and treat it the same way.
> * A test that drives the double from parallel writers, as
>   `ExtentSet.WriteAsync` does, and fails on the old code.
> * The contract test class of the issue, 200 runs under contention, with no
>   failure. #190's fault sweeps pass unchanged at default depth.
> * This is a test double. Change no product code. If the race turns out to
>   be in the store, stop and say so on #214.

The session prompt's whole text is in the session's close-out under
`docs/sessions/`, filed with the last of its three pull requests.

## The report

- **The race.** `SimulatedFileSystem` already had one lock, `_gate`, over its
  tables. `Handle.WriteAsync` and `Handle.Flush` passed `owner.PathOf(inode)`
  as an argument, so it ran **before** `Write` and `Flush` took the lock, and
  enumerated `_files` while a parallel create, rename or delete changed it.
  It is in the double, not the store.
- **The fix.** `Write` and `Flush` resolve the path inside the lock, with the
  operation. `Operations`, `Allocations`, `Crashed` and `Writes` are read
  under the lock; `Writes` returns a copy. The injector and refusal callbacks
  were already invoked under the lock, so they stay serialised.
- **Other shared state in `Faults/`.** `TeeFileSystem`'s `Handle` and `Both`
  kept a plain `bool` "disposed", and a power cut disposes what is open while
  the store may be disposing the same handle; each is now disposed once by
  `Interlocked.Exchange`. `KillingFileSystem` already counts flushes with
  `Interlocked`. `FaultInjectionTests`' dictionaries are local to one run.
- **The lock and the "drop unsynced pages" model.** Every operation, the
  flush included, is atomic under the one lock, so a run with parallel writers
  is the same as some serial order of its operations, and the model is applied
  to that order. A flush moves its own file's pending writes to its durable
  bytes and marks the metadata journal durable up to that point; another
  file's writes, before or after it in that order, stay pending in that file,
  as on a journaling file system where an `fsync` commits the whole journal
  but only its file's data.
- **The tests**, `Faults/SimulatedFileSystemConcurrencyTests.cs`:
  - `a_write_racing_a_rename_names_the_file_as_it_is_when_written`: the race
    made deterministic. A rename waits, under the lock, until a write has
    started. On the old double the write is counted against the old name
    (`racing.tmp`), in every run, under load too.
  - three tests with eight writers on their own threads, 256 files each,
    created, written in chunks, flushed and renamed: every file's bytes; one
    crash stops every writer at one operation; a power loss keeps every
    flushed file. On the old double all three fail with #214's
    `Collection was modified` when the class runs alone; under the sweeps'
    load they did not, which is why the deterministic test exists.
- **Gate results.**
  - The new tests fail before the fix and pass after it (20 runs of the class
    alone, two of the namespace).
  - `FileStorageWindowsSharingContractTests`, **200 runs, 8 processes at once
    on 4 cores with a busy loop on every core: 200 passed, 0 failed.**
  - #190's sweeps, the `Varve.Store.Tests.Faults` namespace at default depth:
    35 tests, 33 passed, 2 skipped by design (`DeepExploration`,
    `KillHost`), the same as on `main` with the new tests excluded.
    `BulkLoadTests`: 9 passed. The whole of `Varve.Store.Tests`: 0 failed.
- **Found, not done.** A rename is two journal entries, so a power-loss cut
  can fall between them and lose a file under both names: harsher than the
  ext4 model of ADR 0073. Filed as
  [#370](https://github.com/Hafeok/Varve/issues/370).
