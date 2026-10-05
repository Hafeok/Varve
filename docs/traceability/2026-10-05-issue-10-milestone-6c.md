# Milestone 6c — the dictionary on disk, the bulk loader, the browser backend, the soak's causes

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is verbatim.
> The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#10](https://github.com/Hafeok/Varve/issues/10), [#61](https://github.com/Hafeok/Varve/issues/61) |
| **Date** | 2026-10-05 |
| **Tool** | Claude Code 2.1.289, a cloud session started from the Android app; one subagent, in a git worktree, built the browser backend ([its record](2026-10-05-issue-10-milestone-6c-browser-backend.md)) |
| **Model** | `claude-opus-5-5` (Claude Opus 5.5), configured and served — confirmed from the session's own metadata (`get_session`: `session_context.model` and `last_served_model`), not from memory |
| **Session identifier** | `session_01JZjxTGanMjuGR2KWbPjWRi` |
| **Branch** | `ccr-73c21edc-57a4zy`, landing on `main` as one pull request |

## The prompt

> Milestone 6c (Refs #10, #61): bulk loader, checkpoint policy, on-disk dictionary, browser backend, and the soak. 6a is merged (format version 1 frozen, ADRs 0070–0077). Read the 6a traceability record, storage-format.md, spec 1.5 and ADRs 0070–0077 before planning. AGENTS.md applies; plan first; one PR, red only on CS0618.
>
> 1. #61 first. Separate the causes of the soak's working-set growth (168 MB median to 730 MB, peaks 2.8 GB). Checkpoints built in memory are the known one: write checkpoints as a streaming merge of the runs and the dictionary section directly to the derived blob, bounded by one block per input run plus the output buffer; state the bound. Find and fix whatever remains; report each cause with its measurement. Re-run the one-hour soak and report working set, file handles and derived/ counts; the 1.0 soak gate is a flat working set within a stated band.
> 2. On-disk dictionary (6a's deferral): the term dictionary as derived state in the same run and checkpoint machinery, so open is O(tail since the last checkpoint) in time and bounded in memory, not O(terms). Lookup by term and by id through the synchronous blob read; the hot-path allocation tests extend to it. ADR.
> 3. Bulk loader per ADRs 0076 and 0077: streaming input from any Varve parser, external sort into runs spilling to derived/, merge-join against the pinned runs for the effective delta, one multi-record commit, validators over the on-disk overlay. Gate: 100 million generated N-Quads into an empty dataset and into a populated one, bounded memory (state the bound and assert it), a crash injected at every record boundary during the load leaving the dataset at the previous head with no partial commit visible, result equal to the reference model, throughput against pyoxigraph's bulk loader.
> 4. Checkpoint policy: background work under the explicit maintenance option from the 0042 note (every N commits or M bytes of log, configurable), checkpoints as 6a defined them, the as-of latency property measured (cost proportional to log distance), deferred deletion of runs a pin still holds proven by a test that holds a pin across a merge.
> 5. Browser backend: OPFS with synchronous access handles in a worker (decision 0071's synchronous read on the browser); IndexedDB fallback declaring TransactionCommitted durability where OPFS sync handles are unavailable. Contract tests in headless Chromium; the determinism test across desktop and browser (same script, byte-identical log/). If OPFS sync handles cannot be reached from .NET WASM without JS interop that breaks AOT, say so with evidence and bring the alternative; maintenance stays off by default in the browser.
> 6. Replica bootstrap as a file-level operation: ship a checkpoint plus the log tail into a second dataset directory and open it; a property that the replica equals the source at the shipped position. No protocol.
>
> Report as 6a did: ADRs, defects found with seeds, iteration counts, the soak with causes separated, benchmarks with hardware, proposed spec changes, and what milestone 7 needs from the maintainer.

The plan was posted in the session before any code and the work went ahead
on it; the prompt did not ask to wait for approval, and every decision is
filed unaccepted, so the maintainer's review is on the pull request.

## The report

[REPORT]
