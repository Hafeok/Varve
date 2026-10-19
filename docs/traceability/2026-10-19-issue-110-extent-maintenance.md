# Extent maintenance — background merges and extent deletion

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#110](https://github.com/Hafeok/Varve/issues/110), milestone Extent maintenance |
| **Date** | 2026-10-19 |
| **Tool** | Claude Code 2.1.303, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_014BgePW3C3ECWERkiQMVdPb` |
| **Branch** | `claude/gracious-goldberg-ufudmi`, from `main` at ce6136b (`v0.1.0-preview.8`) |
| **ADRs** | 0158–0162, filed unaccepted; 0139 superseded in part by 0160; 0134, 0138 and 0139 carry dated amendments |

## The prompt

The maintainer's message, with the session prompt in a zip, which was not
copied into the repository:

> @"/root/.claude/uploads/8202f6b4-aa9a-5406-9603-ac378f889ee6/6c4a25b7-varve-extent-maintenance.zip" Unzip the attached varve-extent-maintenance.zip outside the repository and read varve-claude-code-extent-maintenance.md in full. It's the session prompt for milestone Extent maintenance (#110) in github.com/Hafeok/Varve. Work from current main. AGENTS.md applies in full. Read issue #110 and the ADRs the prompt names, then post the plan answering its seven plan questions and wait for approval before writing code. The Management plane (#105) runs in parallel; coordinate only through the reader contract the prompt describes.

The session prompt, `varve-claude-code-extent-maintenance.md`:

> # Session: Extent maintenance — background merges and extent deletion (#110)
>
> Object storage (#92, `v0.1.0-preview.8`) landed extents without merges, by the cut the maintainer accepted on 2026-10-15. Each checkpoint adds one extent set per permutation. Every head and as-of read merges across all of them, and on an object store no extent is ever deleted. This milestone bounds both, and it is in 1.0 (#15).
>
> **Starting point.** Current `main`. Two other sessions run in parallel:
> - **SHACL part 1 (#123)** touches nothing here.
> - **Management plane (#105)** builds a read-only tier-1 follower on the follower attach that #92 landed. That follower reads extents, so it is a reader under your deletion rule (point 4).
>
> Whichever of you lands second wires the follower into the reader contract. If you land first, define the contract so that #105's follower can use it unchanged.
>
> **Read before planning:**
> - issue #110;
> - ADRs 0134–0142, above all 0136 (one writer by an epoch and a log-slot fence), 0138 (validity intervals) and 0139 (extents and the projection catalog). Read 0139's "Deletion waits for Extent maintenance" and its shared commit index paragraph: both are this milestone's;
> - ADR 0071 and ADR 0072 with their dated amendments;
> - #92's traceability record and the operator guide's checkpoint budget;
> - `src/Varve.Store.ObjectStorage` (the catalog, the page cache, follower attach and the cold start);
> - #117 (object store verification), which may land before or after you.
>
> `AGENTS.md` applies in full. Plan first, and wait for approval. Land as one PR, red only on `CS0618`. Its descriptor is at the next free `0.1.0-preview.N` when it lands (ADR 0102), names #110, closes it with a `Closes #110` trailer on the descriptor commit, and carries `accepted-by: mailto:emil@okkels-klein.dk` (the descriptor's line, `docs/releases.md` point 6). Use bare #N everywhere else, and put no closing keyword in the PR description (#108). Your ADRs are 0158–0162: SHACL holds 0148–0157 and the Management plane holds 0163 onward. Land through `land/`.
>
> ## A. Decisions (Accepted unless stated; each becomes an ADR or a dated amendment)
>
> 1. **Only the writer merges.**
>    - A merge writes its new extents with create-if-absent, then swaps the catalog once with replace-if-unchanged, under the epoch fence of ADR 0136.
>    - A superseded writer's swap is refused, and so are its extents: they are never named.
>    - A merge never blocks the commit path, and never blocks a checkpoint.
>    - A checkpoint that lands while a merge is running wins. The merge's swap then rebases: it replaces its input sets only if every one of them is still in the catalog, keeps every set added since, and otherwise abandons the merge (its extents become unnamed and are deleted under point 4).
>    - There is no separate merge worker in this milestone. If the plan finds the writer must offload merges, stop and say why before designing one.
>
> 2. **The merge policy.**
>    - The policy is stated per permutation, with the bound it maintains on the number of extent sets. Size-tiered with a stated fan-out is the default proposal. Argue for another policy only with numbers.
>    - Merges are triggered by events, the catalog changing at a checkpoint, never by elapsed time.
>    - Measure write amplification: bytes rewritten per byte checkpointed, over the REC M history.
>    - The policy is configurable within stated limits. The default holds the read latency gate.
>
> 3. **A merge never changes a result.**
>    - A merge is a pure function of its input sets. It preserves every validity interval exactly (ADR 0138), so a read at any position over the merged catalog equals the same read over the unmerged one.
>    - A merge has no other effect, either: it neither coalesces history nor drops retracted intervals. Compaction that drops history is a different decision, and it is not this milestone's.
>
> 4. **The deletion rule: readers announce, and no timer.**
>    - **Who announces what.** Every instance that reads extents from the store announces the oldest catalog version any of its in-flight reads holds. That covers the writer, followers, and CLI reads that attach. An as-of read does not need an old catalog version: merged extents carry the same intervals, so it reads through the newest catalog. Only reads that already hold a catalog version count.
>    - **The reader record.** Propose its object, location, identity and update rules. The identity must survive a restart of the same instance, for example by being persisted in its cache directory or its configuration.
>    - **What the writer may delete:** an extent named by no catalog version at or after the oldest version any present reader record announces.
>    - **A dead reader's record holds deletion back until an event removes it.** The events are:
>      - the same instance restarting and replacing its record;
>      - an operator command, `varve storage readers` with `list` and `forget`;
>      - later, fleet membership from the Management plane (#105). Record this as a consequence of the ADR, not as work here.
>    - Never a timer.
>    - **What a forgotten reader sees.** A reader whose announced version was forgotten while it was still alive must fail the affected reads with a new problem type (propose its name) and re-attach. It must never return a wrong result.
>    - **Retention is visible.** The bytes retained because of each reader record are reported on readiness and in `varve storage readers list`.
>    - **On the file backend** the process holding the OS lock is the only reader, so a retired set is deleted at once, as today.
>
> 5. **The shared commit index (ADR 0139).**
>    - At each checkpoint, the writer stores the commit index below the catalog in the object store, beside the extents. A cold start or a follower then never reads the log's headers below the catalog.
>    - The plan says whether this needs a new derived kind. If so, it is derived format 5, and format 4 stays readable under read-forever, with a fixture written by the current build (#129's inventory will list it).
>    - If 0139's "no format change" holds after all, say why.
>
> 6. **Operations.**
>    - The operator guide's checkpoint budget from #92 is removed. In its place, the guide documents the merge policy, write amplification, retained bytes and the readers command.
>    - `varve storage` reports extent sets per permutation and retained bytes.
>    - If #117 has landed, `varve storage verify` gains two checks:
>      - every extent a catalog names exists;
>      - no reader record names a catalog version that no longer exists.
>
> ## B. Definition of done
>
> - Over generated histories with retractions, a merged projection equals the in-memory one at every position. This holds across any merge schedule, any interleaving of checkpoints and merges, and any cache eviction schedule.
> - Failure injection at every extent write, catalog swap, reader-record write and deletion. No reader ever sees a catalog naming a missing extent, except a forgotten reader, which gets the problem of point 4 and never a wrong result.
> - Over generated schedules of readers attaching, pinning, announcing, crashing, restarting and being forgotten, no extent that a live announced catalog version names is ever deleted. Every unnamed extent is deleted once no record holds it.
> - A superseded writer's merge swap is refused, and its extents are never named.
> - A follower attached to the writer's catalog keeps serving across merges and deletions on Azurite and on RustFS.
> - Read latency at REC size M after 10, 50 and 100 checkpoints with merges on, against #92's table without them, with the slower rows shown first. Also report write amplification and the cold start with the shared commit index, against #92's.
> - Every milestone 4 and 6a property and the storage contract suite unchanged.
> - The traceability record. README Status, `docs/roadmap.md`, AGENTS.md's State and `eng/status.cs` are updated for the milestone.
>
> ## CI and test time
>
> - Use only the images listed in `.github/ci-images.yaml` (#118). If #118's PR 2 has landed, use the mirror references. Never add an image without a row.
> - Size property-test iteration counts for the land gate. State each count, and report each new test project's duration per OS. The Windows store step has grown from 8 to 53 minutes (#126), so don't add to that without saying so.
> - A long run, such as many generated schedules or many checkpoints, belongs in a separately named job or test category that #129 can move to the release gate. Say which runs those are.
>
> ## Plan questions
>
> 1. The merge policy, its bound on sets per permutation, and the expected write amplification.
> 2. The rebase rule when a checkpoint lands during a merge, and what is abandoned when.
> 3. The reader record: object, identity, update rule, and the contract the Management plane's follower implements. Name the package and type, and check them against the layer rules.
> 4. Dead readers: the events that remove a record, the `forget` command's authorisation, and the problem type a forgotten reader gets.
> 5. The shared commit index, and whether it is derived format 5.
> 6. Which existing tests and documents change, including the operator guide's checkpoint budget.
> 7. ADR numbers within 0158–0162, and the amendments (0139 certainly, 0072 if the format changes).

## The plan, and the maintainer's rulings

The session read issue #110, ADRs 0134–0142, 0071 and 0072 with their
amendments, #92's record and benchmarks, the operator guide's checkpoint
budget and `Varve.Store.ObjectStorage`, and posted a plan answering the seven
questions. It proposed size-tiered merges with fan-out 4 and a cap; a rebase
that keeps sets added since and abandons on a missing input; the catalog
version as the set-sequence counter, advanced at every swap; a reader record
`readers/<id>` with announce-then-confirm; `varve storage readers forget`
guarded by what list showed; `reader-forgotten`; the shared commit index as
an optional ninth blob in format 4. It raised four findings: a superseded
writer's create could take the name a new writer's set needs; a follower
would now write, against ADR 0134; ADR 0072's "derived is not read forever"
against the prompt's read-forever for format 4; and the backend buffered a
whole blob in memory before uploading it. The maintainer's answer:

> Approved, with one correctness change and these rulings.
>
> **Correctness: the record announces an interval, not the oldest version.**
> - A reader holding v₁ and v₃ that announces only v₁ lets the writer delete a set named in (v₁, v₄) that the v₃ view still reads.
> - So the record carries `catalogOldest` and `catalogNewest`, the oldest and newest versions it holds. The writer deletes set s (removed at r) only when no record's [oldest, newest] overlaps (s, r).
> - State the writer's order in ADR 0160: swap, then read the records, then delete. Announce-then-confirm is safe only with that order. Prove it in the reader-schedule property, including readers that hold several versions at once.
> - The contract becomes `AnnounceAsync(CatalogVersion oldest, CatalogVersion newest)`.
>
> **Rulings:**
> 1. **Format:** the optional ninth blob in derived format 4. No format 5.
> 2. **The writer writes a record too.** The refcount covers the writer's own views only while it remains the writer. After supersession, the stepped-down instance's in-flight reads would be invisible to the new writer, which could merge and delete under them. With a record, every role is uniform, as Replication's handover needs. The record is replaced only when the held interval changes.
> 3. **Announcing is required by default.** A `403` on the record fails the open and names the role. `varve` read commands accept an explicit `--unannounced` for read-only identities. Such a read may find an extent gone. It then fails with its own problem type (propose a name, e.g. `read-unannounced-extent-gone`), never the damage `IOException`, and never returns a wrong result. No silent fallback.
> 4. **The latency gate is relative, within the same run** on the same runner:
>    - head reads with merges on at 100 checkpoints stay within 1.5× of that run's 10-checkpoint row, at p50 and p99;
>    - an as-of read inside the largest set stays within 2× of that run's 10-checkpoint as-of row, at the default `MaxSetBytes`.
>
>    `MaxSetBytes` is in scope. State its default from the M measurement. Report #92's absolute numbers alongside for reference, not as the gate.
> 5. **Derived formats are rebuilt, not read forever** (ADR 0072, 0139). #129 has been corrected to say so.
>    - The reader record v1 and the writer record are coordination state and are read forever.
>    - A record of an unknown version is treated as holding every catalog version, which blocks deletion. It is never ignored.
>    - Add the reader record to the spec as its own format with a version.
>
> **Findings:**
> 1. Fix it as proposed: a taken name means taking the next sequence. Add a property test: a superseded writer's creates, checkpoints and merges never make the new writer's checkpoint or merge fail, and none of them is ever named.
> 2. Amend 0134. The follower's write access is scoped to the `readers/` prefix: Azure with an ABAC condition on the blob path, and S3 with a prefix-scoped policy. Document both in the operator guide. Workload identity stays the only credential.
> 3. Settled by ruling 5.
> 4. Stage uploads through a file in the cache. State the single-PUT size limit per provider (Azure and S3), and refuse a merge whose output would exceed it, rather than failing midway. Multipart is a later decision.
>
> **Also:**
> - Readiness reports retained bytes but never turns unready because of them.
> - A maintenance round may poll, since an interval only affects how soon something is deleted, never whether it's safe to (handover ADR point 7). Say so in 0160.
> - Using the set-sequence counter as the catalog version is accepted.

## The report

REPORT-PLACEHOLDER
