# Cached derived data is verified before use

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#146](https://github.com/Hafeok/Varve/issues/146), with [#127](https://github.com/Hafeok/Varve/issues/127)'s guard test; [#129](https://github.com/Hafeok/Varve/issues/129) and [#15](https://github.com/Hafeok/Varve/issues/15) referenced |
| **Date** | 2026-10-27 |
| **Tool** | Claude Code 2.1.311, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01NbWSYRzUotGnNtdpHy2K62` |
| **Branch** | `claude/adoring-keller-42dw5z`, from `main` at 3a8f636, with `main` at 705b268 merged |
| **ADR** | [0213](../adr/0213-cached-derived-data-is-verified-before-use.md) |

## The prompt

The session was given `varve-claude-code-readiness-store.md`, the brief for
1.0 readiness A (#127, #146, #129's failure injection and format versioning,
#132 and #61), with this message:

> Read varve-claude-code-readiness-store.md in the attached zip (folder varve-readiness-a). It is your session prompt for 1.0 readiness A in Hafeok/Varve: #127, #146, #129's failure injection and format versioning, #132 and #61. Work on current main and follow AGENTS.md in full. Plan first. Post the plan on #129, answering every plan question, and wait for approval before writing any code. Your ADRs are 0213–0222.

The brief's step 2, verbatim:

> **2. #146: cached derived data is verified in full on read.**
> - Every kind in its narrowed scope gets a check over its whole content, verified before first use: the commit index, the dictionary, the graph-version index, runs and spills, and `derived/validation/`.
> - **A failed check is a cache miss, rebuilt from the log, never an error and never a wrong result.**
> - Choose the mechanism per kind in an ADR: a whole-blob hash, or per-block checks as format 5 already has (ADR 0177).
> - State what the check costs at open and on first read, measured at REC M.
> - **Inference's `derived/inference/` will adopt this mechanism (#171).** File the ADR first and post it on #171.
> - **The gate:** a byte zeroed, flipped or cut at any position of every kind gives a miss and the correct answer. The pull request runs a strided share; the nightly runs every position. #126's finding, a wrong history in 985 of 1,920 cases, must be reproduced against the old code and pass against the new.

The plan was posted on #129. The maintainer approved it with these answers,
verbatim:

> Approved, with these answers. 1. #127: close it on your CI finding. I'll take it out of #15. Keep step 1 as the slow-sender guard test, shown to catch the defect against a deliberately broken reader, plus the container ratchet reading the existing exemptions. Ship it as part of step 2's PR or alone, whichever is smaller. 2. Spills stay in scope on your terms. A damaged spill fails the load with nothing committed, under its own problem type (propose its name), so a caller knows a resubmit is right. 3. Mid-read damage: quarantine, re-run and resume, as proposed. Add a property that injects damage at every block of a scan, on files and on objects. The streamed result must equal the undamaged one byte for byte, with no duplicate and no cut. A second damage during the re-run degrades the same way, down to the log. 4. Soak: Linux in the release gate; Windows weekly in the nightly. The release gate also refuses when the latest Windows soak is red or older than 8 days. 5. #132: yes to the empty delta, for keyed writes only, amending ADR 0057. Yes to 100,000 commits as the default of `Varve:Protocol:IdempotencyWindow`. The guide states that a retry past the window commits again, and that the client's retry budget is far inside it. 6. Saved plans aren't read forever. They're versioned and positional. An unknown version is refused with a problem telling the operator to re-plan. The inventory lists them under that rule. One change to step 2: keep the flush where a reader can't verify. Drop the flush-then-rename for a derived file only when it is written in a format every announced reader verifies (format 6 under `maxDerived`). A format-4 file written while a preview.15 follower is announced keeps the flush, because that reader trusts what it reads. Add that to the ADR, with a test in which an older-format reader is announced during a torn write. Replication's PR is #175, as you noted. Start with step 2's ADR and post it on #171.

This record covers step 2 and step 1's guard test, which ship in one pull
request, as the answer to question 1 allowed. Steps 3 to 5 have records of
their own.

## The report

### #127

The truncated Graph Store `POST` did not reproduce over a real socket
against `main`. The container conformance run that reported it had failed
pulling its CI image, before any test ran: the job's step data shows it,
and every container run on `main` since #118 is green. #127 was closed on
that finding, and the maintainer took it out of #15.

What step 1 keeps is the guard. `SlowSenderTests` sends bodies over a raw
socket in many small segments with delays between them, with
`Expect: 100-continue`, and chunked, to every route that reads a body:
SPARQL update, form and query, Graph Store `PUT`, `POST` and multipart, a
managed dataset's `PUT`, the settings and state `PUT`s, and
`/manage/apply`, and checks the 16 KiB limit for a declared length and a
chunked body. To show it catches the defect class, the body reader was
deliberately broken to stop at `read.IsCompleted || buffer.Length > 0`:
all four cases failed (400s, and a chunked body over the limit answered
204). The container job's ratchet now reads the server's exemptions.

### #126's finding, reproduced

Against `main` at 3a8f636, the commit index sweep was extended to every
byte zeroed, flipped and cut. A format-4 blob served a wrong history in
**985 of 1,920** zeroed entries and **1,219 of 1,920** flipped ones, which
is #126's finding and more. On this branch, version 6: **0 of 6,324**
cases, the entries' 5,760 and the directory's and header's 564, are
anything but a miss and the right answer.

### What was built

ADR 0213 holds the decisions; in short:

- **One mechanism**, format 5's 64-bit fold per block, held in the hashed
  directory, verified before a block's first use. Derived version 6 for
  runs, checkpoints, the commit index, the dictionary extent and a new
  checked envelope (kind 9). Kinds that are read whole and are small (the
  state, the catalog, the graph-version index) verify their directory's
  SHA-256 at every read.
- **Damage found mid-read** is quarantined, the window re-run from the log
  into the same place, and the scan resumed; a sought set falls back to
  narrowing; the writer writes a damaged set again on its next round.
- **Spills** are checked against checks the load keeps in memory; damage
  fails the load uncommitted, as `load-spill-damaged`.
- **Projections outside the store** use `Dataset.WriteCheckedAsync` and
  `TryReadCheckedAsync`. The validation projection moved to it, and #171
  was told the API.
- **Shared blobs** are written in version 4 while a reader of 5 or older
  is announced, and rewritten by maintenance once none is; readiness
  reports `uncheckedSets` meanwhile.
- **The flush**, per the maintainer's change: a derived file is published
  without a flush only when its own header says version 6 of a checked
  kind.

### What the work found on the way

- `DerivedFormat.RenameToAsync`, which rebinds a bulk delta run's header
  when validators attach terms, wrote version 4 over a version-6 layout.
  It now keeps the version. No store test covers a bulk load whose
  validator attaches terms, which is how it went unseen.
- A run's damage could not be healed before the bulk delta's commit, since
  the log does not hold the window yet; the delta is put under healing
  after the state is published.
- `MemoryWriterFenceTests`' concurrent-claims case failed once in three
  full store runs, with a claimant holding an epoch the record's history
  lacks. The code is untouched by this branch; it was offered to the
  maintainer as a separate task.

### The gates

On this session's container (Intel Xeon, 4 cores at 2.1 GHz, 15 GiB,
Ubuntu 24.04, .NET SDK 10.0.403), Linux only:

| Sweep | Pull request | Nightly |
|---|---:|---:|
| Every other kind, every byte (`DerivedDamageTests`) | 1,894 of 30,302, 9 s | 30,302, 2 min |
| Commit index, every byte zeroed, flipped, cut | 791 of 6,324, 12 s | 6,324, 65 s |
| Order extents, every byte | 962 of 15,384, 9 s | 15,384, 62 s; 0 failed reads (305 before) |
| Every block a read reads, damaged, files | 45 of 177, 1 s | 177, 2 s; all answered from the log |
| The same on objects | 59 of 234, 3 s | 234, 9 s |
| Checked envelope | 1,154, 1 s | 4,621, 3 s |
| Spills, each of the first 600 bytes | 150 loads, 7 s | 600, 24 s; all failed uncommitted |

`FlushRuleTests`: under a reader of 5, 21 derived files were flushed and 5
were not, and the set's shared blobs were among the flushed; under a
reader of 6, 17 and 9. Every unflushed file was torn by a simulated power
cut, and the answers after equal the answers before.

The Windows and macOS figures come from the pull request's CI.

Each new sweep was shown to fail. With the term-page checks switched off,
`DerivedDamageTests` at its pull-request depth found 107 different answers
and 50 failed reads. With `RenameToAsync`'s fix undone,
`BulkLoadTests.a_validator_that_attaches_a_term_commits_the_load_and_it_reopens`
fails with "A bulk load's delta run does not read back".

### The cost at REC M

`Varve.Benchmarks --asof-seek file M`, on `main` at 3a8f636 and on this
change with the same benchmark, alternately, four and three runs; medians:

| | `main` | This change |
|---|---:|---:|
| The open from a fresh cache | 496 ms | 291 ms |
| The head's reads, first pass | 79.5 ms | 67.8 ms |
| Head: lookup / a subject's quads, p50 | 12.65 / 9.85 µs | 12.5 / 10.0 µs |
| Inside the largest set: lookup / a subject's quads, p50 | 3.5 / 7.6 µs | 3.6 / 7.9 µs |
| Load with 100 checkpoints and maintenance | 182.7 s | 189.2 s |
| `fsync` calls in the run | 28,222 | 27,938 |

The subject's quads inside the largest set are 4% slower, 0.3 µs, which is
over #146's 3% budget but less than the spread between `main`'s own runs
(7.5 to 10.1 µs). The other read figures are within it. ADR 0213's
amendment of today holds the table and what it says.

## Commits

Listed by the pull request.
