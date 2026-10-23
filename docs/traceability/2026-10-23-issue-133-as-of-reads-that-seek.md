# As-of reads that seek — an as-of read inside an extent set costs its matches

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#133](https://github.com/Hafeok/Varve/issues/133), in 1.0 ([#15](https://github.com/Hafeok/Varve/issues/15), amendment of 2026-10-19) |
| **Date** | 2026-10-23 |
| **Tool** | Claude Code 2.1.309, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_019pHmYtvAaXkouBRwTGXwCp` |
| **Branch** | `claude/clever-keller-jpi0qq`, from `main` at 59b230d (after `v0.1.0-preview.12`) |
| **ADRs** | 0177 and 0178, filed unaccepted; 0049, 0072, 0138, 0139 and 0159 carry dated amendments |

## The prompt

> # Session: as-of reads inside an extent set cost O(matches), not O(set size) (#133)
>
> You are working in github.com/Hafeok/Varve, from current `main`. `AGENTS.md` applies in full. #133 is in 1.0 (#15, amendment of 2026-10-19).
>
> **Read before planning:**
> - #133 in full, including its "Where the cost comes from";
> - ADR 0138 (validity intervals; its "As-of reads" section names the cause: a set is **narrowed** to `(a, P]` by reclassifying all its rows before the read proceeds);
> - ADR 0139 (extents and the catalog; derived format 4);
> - ADRs 0158–0162 from Extent maintenance (#135): merges, the merge policy and `MaxSetBytes`, the reader rule, and the shared commit index (the ninth blob);
> - ADR 0072 and #129's corrected inventory: derived formats are **rebuilt, not read forever**;
> - `docs/spec/storage-format.md` §7;
> - #110's traceability record and `tests/Varve.Benchmarks/README.md`: REC M's as-of numbers (2,736 ms inside the largest merged set, against 12.1 µs for a head lookup at p50), and which generator version they used.
>
> Other sessions running in parallel (SHACL part 2b, Management plane part 2) don't touch the extent code. Replication (#97) starts after your plan settles the layout and will build against it, so say clearly in the plan what the layout is.
>
> ## The decision: seek, don't narrow
>
> A read at P inside a set's window looks its keys up in the set exactly as a head read does, then classifies only the matching rows' intervals against `(a, P]`. That needs a key's intervals to be reachable from the key. Measure at least two layouts before choosing:
> 1. **Intervals beside the key:** each entry in an order blob points into a position column aligned with the key column, with the positions kept out of the key bytes so head reads never touch them.
> 2. **An interval index per set** (an interval tree, or sorted endpoints with a skip index) for range scans as of P, so a scan skips keys whose intervals don't cover P.
> 3. **Position-sorted runs within a key range:** only if the measurements show keys with many intervals.
>
> **Constraints:**
> - **Head reads must not get slower.** Measure lookup and scans at p50 and p99 against #110's 100-checkpoint row.
> - **Merges (ADR 0158) write the new layout,** and a merge stays a pure function of its inputs.
> - **Write amplification and checkpoint time** are reported against #110's 1.77 and its checkpoint figures.
> - **On object storage, count the pages touched** per as-of read, not only the time: each page miss is a ranged GET.
> - **A new derived kind or version is allowed,** and is probably derived format 5. A format-4 set is read with the old narrowing path, or rebuilt, during an upgrade, never failed. Say which, and how a preview.12 follower behaves during a mixed upgrade.
> - The reader-announcement rule (ADR 0160) and the shared commit index (ADR 0161) are unchanged.
>
> ## Definition of done
>
> - Over generated histories with retractions, as-of reads equal the in-memory projection at every position, unchanged, across any merge schedule and cache eviction schedule.
> - At REC M, an as-of point lookup inside the largest set is within 2× of a head lookup, at p50 and p99.
> - An as-of pattern scan costs time and pages proportional to its matches, measured at two set sizes at least 4× apart.
> - Head read latency is unchanged within noise. Report write amplification and checkpoint time.
> - Benchmarks are on the file backend and on Azurite, with the slower rows first and the REC generator version stated.
> - The traceability record; `docs/spec/storage-format.md`; the operator guide's sizing advice for `MaxSetBytes`, which may now be raised; README Status, `docs/roadmap.md` and `eng/status.cs`.
>
> ## Plan questions
>
> 1. The layouts measured, with numbers, and the one chosen.
> 2. The format change: kind, version, and the upgrade path for format-4 sets and mixed-version followers.
> 3. What merges and checkpoints write, and the change in write amplification.
> 4. What changes for `MaxSetBytes` and its default.
> 5. ADR numbers from 0177, with amendments to 0138 (its "As-of reads" section) and 0139, and 0072 if the derived version changes.
>
> ## How this lands
>
> - **One PR,** red only on `CS0618`.
> - **The descriptor** is at the next free `0.1.0-preview.N` when it lands (ADR 0102). It carries `accepted-by: mailto:emil@okkels-klein.dk` and `Closes #133` as a trailer on the descriptor commit. Use bare #N elsewhere, and no closing keyword in the PR description (#108).
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`, the repository's rule regardless of any generic hook;
>   - reference #133, with the DCO sign-off;
>   - never name a model.
> - **A `docs/traceability/` record** (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml`.
> - **Test time:** size land-gate iteration counts, report each new test's duration per OS (#126), and put long runs behind environment variables that the nightly workflow sets.
> - **Land through `land/`.**

The session was not asked to stop for approval, and did not: it measured,
chose and built, and the plan's answers are below and in the pull request,
for the maintainer to rule on there.

## The plan's answers

**1. The layouts measured, and the one chosen: intervals beside the key.**
REC M, generator output version 2, on files, a checkpoint every hundredth of
the history and maintenance after each, the largest set 211 MiB:

| | Format 4: narrow | Layout 1: seek (format 5) |
|---|---:|---:|
| Opening a view inside the largest set | 2,950 / 3,246 ms (two runs) | 0.01 ms |
| A quad's as-of lookup inside it, p50 / p99 | 3.3 / 10.3 µs, after the narrowing | 4.4 / 15.0 µs |
| Its pages, p50 / max | 0 / 3, on the narrowed run | 2 / 5 |
| A head lookup, same run, p50 / p99 | 12.5 / 40.6 µs | 15.2 / 52.1 µs |

- **Layout 1** is built: the gate holds at 0.29× a head lookup at p50 and
  p99, from the first read.
- **Layout 2** (a skip index) was measured by its ceiling on the largest
  set (`ExtentLayoutStatistics`): it could pass 75%, 50% and 25% of the rows,
  in whole blocks, a quarter, half and three quarters into the window. A
  lookup or a subject's quads read one or two blocks either way; a broad
  scan early in a window is where it would save, up to that share. Not
  built; ADR 0177 records it as an addition a later format can make beside
  the columns.
- **Layout 3** has nothing to order: every key of the largest set has
  exactly one interval.

**2. The format change.** Derived **version 5 of kind 6** (an extent) only;
every other kind, the catalog included, is still written as version 4. A
format-4 set is **read with the old narrowing path, never failed**, and
maintenance **rewrites** it alone as format 5 when no merge is due. A
preview.12 follower behind a writer of this release finds the format-5
extents cache misses, its chain ends before them, and it reads the log above:
correct at every position, at the log's cost, until upgraded. Followers
first, then the writer. Checked with `main`'s store (preview.12's extent
code) over this build's sets, on a directory and on Azurite: the same answers
at every position sampled (below).

**The layout, for Replication (#97).** A set is still nine blobs under
`extents/<from>-<to>.<n>/`. Each `order<k>` is a run whose two key sections
for order `k` are the open and closing keys, exactly as format 4; then a
thirteenth key section, the rows within's keys; the term section and filter;
then three columns — open positions, closing positions, the rows within's
pairs — of fixed-width little-endian distances after `from`, `w` bytes each
(the directory's seventeenth word); then the directory and header. A
replica copies the nine blobs as they are.

**3. What merges and checkpoints write.** Both write format 5 from the same
rows; a merge stays a pure function of its inputs' rows. Write amplification
1.86 against 1.78: merges rewrote 0.4% more bytes, for 3.5% fewer
checkpointed. The 100 checkpoints took 24.2 s against 24.7 s; maintenance
91.8 s against 80.7 s, a merge reading its inputs once more for the rows
within's keys.

**4. `MaxSetBytes`.** It no longer bounds an as-of read. Its default stays
256 MiB: at 1 GiB REC M's chain held 7 sets against 5, and head lookups were
slower (21.4 against 15.2 µs at p50). The operator guide says to raise it
on datasets many times the cap.

**5. ADRs.** 0177 (seek, don't narrow; the layout; format 5) and 0178 (the
upgrade, mixed versions, the cap). Amended: 0138 ("As-of reads"), 0139
(format 5 for extents), 0072 (derived version 5), 0049 (an as-of estimate
inside a set may be estimated), 0159 (the cap bounds merges, not reads).

## The report

**What was built.**
- `src/Varve.Store`: the format-5 writer and loader
  (`DerivedFormat.Extents.cs`, `DerivedFormat.cs`); `ExtentSeek.cs`, the
  lookup, the delta stream and the scan that overlays it; `IndexVersion`
  takes the sought set as its newest member; `Dataset` seeks a format-5 set
  and narrows a format-4 one; maintenance rewrites format-4 sets.
- `tests/Varve.Store.Tests`: `ExtentSeekTests` (four tests) and
  `ExtentLayoutStatistics` (env-gated); `DictionaryTests` now expects
  version 5 for order extents and 4 for every other blob.
- `tests/Varve.Benchmarks`: `--asof-seek <file|azurite> M [maxSetMiB]`.

**The gates.**
- **Equal to the model at every position**: `ExtentSeekTests` over 40
  schedules of checkpoints, drops and merges with retractions, by scan, by
  lookup of every quad any view held, and by each subject's estimate; no
  read writes a narrowed run. The existing properties —
  `ExtentTests`, `ExtentMergeTests`, `EstimateTests`, and
  `ObjectExtentTests.eviction_never_changes_a_result` with caches of 1 byte,
  1 MiB and 8 GiB — now run over format-5 sets, unchanged.
- **Within 2× of a head lookup**: 0.29× at p50 and p99 on files (4.4 / 15.0
  against 15.2 / 52.1 µs), and on Azurite 4.2 / 15.6 µs against 14.9 µs /
  7.4 ms.
- **Scans by their matches**: a subject's quads cost 0.567 and 0.562 pages
  a match inside sets of 211 and 7.5 MiB, 28× apart; a point lookup inside a
  set of 16,000 quads reads no more bytes than inside one of 2,000 (a test).
- **Head reads unchanged**: across four benchmark runs the head row moved
  by run (12.3–15.3 µs at p50) in both formats, so the same lookups and
  subject scans were timed over one kept dataset of each format, three
  alternating rounds: p50 lookup 21.9–24.7 µs for `main` on format 4,
  23.6–26.1 µs for this build on format 4, 22.2–23.1 µs on format 5.
- **#110's table on this build**: every head gate within; the as-of read
  inside the largest set under 0.1 ms in every row, from 3,390 ms on `main`
  in the same container.
- **On Azurite**, from a fresh cache: 200 as-of lookups made 25 ranged
  reads, at most 5 for one, against format 4's narrowing, which reads every
  page of the set before the first.

**The mixed-version check.** A small program, built once against `main`'s
store and once against this branch's, wrote a history of 400 commits with
retractions and checkpoints, and hashed every seventh position's quads:
- on a directory: this build's sets read by `main` — the chain empty, the
  log read — and `main`'s format-4 sets read by this build gave the same hash
  as each build's own; `main`, as a writer, deleted the format-5 sets at open;
- on Azurite, with this build writing and merging: a follower of `main` and a
  follower of this build gave the writer's hash, `main`'s with an empty chain.

**Test durations on Linux** (this container), each new test alone,
including the test host's start: the 40-schedule property about 2.3 s, the
format-4 upgrade 2.4 s, the bytes test 2.7 s, the estimate test 1.8 s.
Windows and macOS are CI's to report on the pull request (#126). The
40-schedule property shares `VARVE_EXTENT_SCHEDULES` with
`ExtentMergeTests` for the release gate. **No nightly workflow exists** in
`.github/workflows/`: the variable is set by whoever runs the release gate
(`docs/testing.md` §7, #129), as for #110's properties.

**The store suite** (`Varve.Store.Tests`, Linux, this container): 338 tests, 282 passed, 56 skipped (env-gated: the object-store services, the real Azure account and the deep explorations), none failed, 14 min 34 s.

**Found and fixed on the way:**
- `DictionaryTests` asserted every extent is version 4; it now asserts the
  split ADR 0177 rules.
- The benchmark's first object-store "no page cached" figure was wrong: the
  page cache keeps the most recently read extent, so the as-of lookups, which
  read one extent, never refetched. It now counts each view's lookups from a
  fresh cache of its own.

**Not done here:**
- A skip index (layout 2): measured, recorded, not built.
- The narrowing path stays for format-4 sets; it goes when no supported
  build writes format 4 (ADR 0178).
