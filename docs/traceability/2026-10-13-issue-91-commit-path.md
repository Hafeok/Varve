# Commit path — group commit and per-graph concurrency

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#91](https://github.com/Hafeok/Varve/issues/91), milestone Commit path |
| **Date** | 2026-10-13 |
| **Tool** | Claude Code 2.1.296, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_019HhDaNHkyLi7YMs6ubZEv8` |
| **Branch** | `claude/milestone-commit-path-v0.1.0-w4ktdc`, from `main` at 2472e1a (`v0.1.0-preview.4`) |
| **ADRs** | 0124, 0125, 0126, filed unaccepted; 0011, 0057, 0072, 0094, 0096, 0114 and 0119 carry dated amendments |

## The prompt

The maintainer's message, with the session prompt in a zip beside four
attachments (the roadmap's milestones, the web UI milestone, replication on
Azure, and `docs/why.md`), copied into the repository only as the prompt's
item 0 says:

> @"/root/.claude/uploads/4b800d70-913e-55e5-83f2-a340256d2b04/fb863c3f-varve-commit-path.zip" Milestone Commit path (#91), `v0.1.0-preview.5`.
>
> The attached `varve-commit-path.zip` holds this session's prompt and its attachments. Extract it into an empty directory outside the repository, then:
>
> 1. Read `varve-commit-path/varve-claude-code-commit-path.md` in full. It is the session prompt and it governs this session; follow it exactly as if I had pasted it here.
> 2. The other four files in the zip are the attachments its item 0 names: `varve-roadmap-milestones.md`, `varve-roadmap-web-ui-milestone.md`, `varve-roadmap-replication-azure.md` and `varve-docs-why.md`. Copy their content into the repository only as item 0 says; never commit the zip or the extracted folder.
> 3. Before planning, confirm the starting point: `main` is at 2472e1a and tagged `v0.1.0-preview.4`, #91 is open, and the session's identity hook has run and signs as the agent. If any of these fails, stop and tell me which one rather than working around it.
> 4. Then plan as the prompt says, and wait for my approval before writing code.

The session prompt, `varve-claude-code-commit-path.md`:

> # Session: the commit path
>
> Two changes to the sequencer, made together because they touch the same code and the same invariants, and kept apart from anything that only adds surface. Group commit makes every backend cheaper per commit and is a precondition for the object-storage backend, where a write costs milliseconds. Per-graph optimistic concurrency makes graphs behave like independent streams for writers, so unrelated writes in one dataset stop conflicting. Start from `main` at 2472e1a (`v0.1.0-preview.4`). Read the specification (I1–I8 and the sequencer), ADRs 0010, 0011, 0013, 0016, 0057 and its amendments, 0073, 0092–0097, 0110–0123, the 7a and operability traceability records' write benchmarks, and `src/Varve.Store` around the sequencer before planning. `AGENTS.md` applies in full. Plan first, wait for approval; one PR, red only on `CS0618`; the descriptor `v0.1.0-preview.5` naming #91; land through `land/`. #35 (`INSERT DATA` throughput) rides along: it is the same write path.
>
> ## 0. Docs first, separate commit
>
> Replace the forward-looking part of `docs/roadmap.md` with the attached `varve-roadmap-milestones.md` (with the attached Web UI and Replication texts as their sections), and add `docs/why.md` from the attached `varve-docs-why.md`, linked from the README. Bump the README quick start's image tag to `v0.1.0-preview.4`, and extend `eng/status.cs` so that tag must equal the newest descriptor's version (with a fixture: a stale tag fails). The README's status and `eng/status.cs` must pass before you touch the sequencer.
>
> ## A. Decisions (ADRs, Accepted unless stated)
>
> 1. **Group commit, every backend, on by default.** Requests that arrive while a write is in flight are normalised in arrival order against the head as each would see it, and written as one batch when the write returns; each request keeps its own commit record, position and header-chain link, and the batch is one storage write and one flush. Nothing about a single commit changes: I1–I8 hold per commit, and the effective delta of each request is computed against the state including every earlier request in the batch. **A request is acknowledged only after the write holding its batch is durable; a failed batch write fails every request in it, and none is acknowledged** (Object storage and the tier-1 writer handover rely on this). Bounds: a maximum batch size in records and bytes, and no added latency when the sequencer is idle (a lone request is written immediately). The ADR states what a caller can observe (nothing but latency) and what it cannot (the batch boundary is not part of any contract).
> 2. **Per-graph optimistic concurrency.** The expected-position check becomes "no commit in (p, head] touched any graph in scope S"; today's check is S = all graphs. A dated amendment to ADR 0011 and 0057 states the generalised check and the property it buys: if no commit in (p, head] touched S, and the request read and wrote only graphs in S, its effective delta at head equals its effective delta at p (serialisability per scope). The store keeps a graph-version index (the position of the last commit that touched each graph), maintained by the default projection, persisted in `derived/`, rebuilt from the log like everything else, and consulted by the sequencer in the same step as today's check.
> 3. **Protocol.** A Graph Store `GET`/`HEAD` returns the graph's version as its `ETag`; dataset-level reads keep the head. `If-Match` on a Graph Store `PUT`/`POST`/`DELETE` checks the graph's version. A SPARQL Update may send `Varve-If-Graphs-Match: <iri>=<position>, …` (the default graph as `default`); without it, a request that evaluates a pattern takes as its scope the graphs its `WHERE` read and its delta wrote, falling back to all graphs when the pattern reads an unbound `GRAPH ?g` or the union default graph. Data-only requests stay unconditional (0057 as amended). A conflict is `409` under the existing `conflict` problem type with a new `graphs` member listing the conflicting commits' positions and the graphs they touched; the catalogue page and its schema test follow. The service description advertises the header.
> 4. **Client.** `Varve.Protocol.Client` exposes a graph's version and the conditional header, so the client milestone builds on it rather than adding it.
>
> ## B. Definition of done
>
> - Group commit: a property that for generated request streams the batched history equals the unbatched one commit for commit (positions, deltas, header chain, bytes); crash injection inside a batch at every byte offset recovers to the last closed commit, as milestone 4 and 6a require; no request is acknowledged before its batch is durable (checked under failure injection); the idle-latency bound measured.
> - Per-graph concurrency: the serialisability property over generated histories with concurrent writers on disjoint and overlapping graph sets: disjoint scopes never conflict, overlapping ones conflict exactly when an intervening commit touched the scope; the graph-version index rebuilt from the log equals the incrementally maintained one (projection rebuild equivalence).
> - Benchmarks, honest row first: the write benchmark on the file backend before and after group commit; the eight-writer benchmark with one graph per writer before and after per-graph concurrency (conflicts should fall to zero); single-writer latency unchanged within noise.
> - Every existing property, the W3C suites, the protocol suites and the auth matrix unchanged.
>
> ## Non-goals
>
> The object-storage backend, any new public endpoint beyond the header and the `ETag` change, per-resource concurrency (graphs are the unit; the ADR says why resource-level would need a resource-version index the size of the dataset and records it as rejected for now).
>
> ## Report
>
> The traceability record and PR body: the ADR numbers and amendments; both properties with iteration counts and any counterexample found; the benchmark tables; what the object-storage and client milestones can now rely on.

The session confirmed the starting point (`main` at 2472e1a, tagged
`v0.1.0-preview.4`; #91 open; the identity hook signing as the agent),
proposed a plan with questions on the as-of tag, what Settings and Erasure
touch, the `409`'s graphs and the conditional workload, and the maintainer
approved it:

> Approved, with these answers and conditions.
>
> ## Your four questions
>
> 1. **As-of ETag: the graph's version at that position.** An ETag identifies a representation, and the content of g as of q is fixed by the last commit ≤ q that touched g. Two as-of reads with the same content then share a tag, which is the correct caching behaviour. The cost model is the same as R2, as you say.
> 2. **Settings and Erasure touch every graph: yes.** Being conservative here is the only way "S = all graphs" stays exactly today's check.
> 3. **409 `graphs`: yes**, limited to the request's own scope and capped at 100 with a truncation flag. Also bound the scan in `ConflictsAsync` by ADR 0114's as-of distance limit, so a long-stale p cannot turn a conflict response into a log scan.
> 4. **The conditional eight-writer workload: yes.** Keep the `INSERT DATA` eight-writer row too, as the control.
>
> ## Conditions on ADR 0124 (group commit)
>
> - **Timestamps.** State that each member's timestamp is taken at its staging, and that timestamps are monotonic within a batch and across batches. The equivalence property runs under an injected `TimeProvider`. Without that, "log/ bytes identical" cannot hold between the On and Off runs.
> - **Projection failure at member k.** Say outright that members k+1… are not in the write. They are answered `Unavailable` because they never became durable, not because they did. Add an injected projection failure at a random k to the equivalence property, so this case is proven rather than just described.
> - **Cancellation.** A request cancelled while still queued, before a leader drains it, is withdrawn and never written. Once it is in a batch, it is not withdrawn (as you have it). Test both.
>
> ## Conditions on ADR 0125 (per-graph concurrency)
>
> - **Record what was requested, not what was returned.** The recording wrapper must log every graph a pattern asked for, even when the scan finds nothing. `FILTER NOT EXISTS { GRAPH <h> … }` against an empty or absent h is a read of h. If only graphs that returned rows are recorded, a concurrent insert into h breaks serialisability. Add this case to the property's generator explicitly.
> - **`GRAPH ?g` under `USING NAMED`.** The scope can be the declared named set instead of all graphs. Take it if it's cheap; otherwise fall back as planned and note it as a follow-up.
> - **Index size.** Graph-per-entity datasets are common, and an `ImmutableDictionary` with a million graphs is a real cost in memory and open time. Measure it at 10⁵ and 10⁶ graphs and state the numbers in the ADR. A different structure isn't needed now, but the limit has to be on record. Deriving the touched graphs from a commit's quads must not allocate per quad: mark it `[HotPath]`.
>
> ## Conditions on ADR 0126 (protocol)
>
> - **Empty graphs.** A graph with no quads has no current representation. `If-Match` against it is `412`, and `If-None-Match: *` on `PUT` succeeds (create-only). Its version still counts in scope checks, because emptying it was a commit that touched it.
> - **Strong tags across media types.** Follow whatever ADR 0094 settled for the head tag. If it uses one strong tag across serialisations, keep that for graph tags, and make the ADR say why it is acceptable here: the tag names the graph state, the server compares it only against that state, and responses carry `Vary: Accept`. Don't diverge from 0094 quietly.
> - **Server-side retry for implicit preconditions.** A request that carries no precondition from the client (GSP without `If-Match`, Update without `Varve-If-Graphs-Match`) can only conflict through the server's own optimistic check. The client has no basis to act on that 409. Re-pin and re-evaluate up to three times before answering `409`, unless 0057 says otherwise; if it does, say so and we'll decide. A request with a client precondition is never retried.
>
> ## Other notes
>
> - Amending ADR 0114 point 1 and taking the spec to 1.7 are both right. List them in the PR body under "what else changed".
> - On #15: leave it to me; I'll amend it from the architect chat.

## The report

**ADRs.** 0124 (group commit: requests queue in the sequencer, a lone one is
written at once, those that arrive during a flush are staged one by one and
written in one write and one flush, at most 1,024 records or 4 MiB; no answer
before the batch is durable; timestamps taken at staging; a projection
failure at member k answers k+1… `Unavailable`; cancellation withdraws only
while queued), 0125 (per-graph optimistic concurrency: the expected position
takes a scope; `Settings` and `Erasure` touch every graph; the graph-version
index is part of the default projection, persisted, rebuilt from the log,
its size measured and stated), 0126 (graph versions over HTTP: a Graph Store
tag is the graph's version as of the read; `If-Match`, `If-None-Match` and
`Varve-If-Graphs-Match` per graph; a `409` names the graphs of the scope,
capped at 100, its scan bounded by ADR 0114's as-of distance). Three decision
sets, filed unaccepted; `CS0618` is the one red.

**What else changed**, by dated amendment (ADR 0068): **0011** (the expected
position has a graph scope), **0057** (a request's scope is what it asked
for, absent graphs included; an inferred conflict is retried by the server,
a client's precondition never), **0072** (a break within the 4 MiB write
window of the last good record reads as torn — the maintainer's answer to the
I6 question below), **0094** (a Graph Store tag is the graph's version; the
server retries only its own precondition), **0096** (an as-of read's tag is
the graph's version at that position), **0114** point 1 (writes queue in the
sequencer, so the concurrent-write limit is the queue), **0119** (the `409`
lists the graphs in scope). The specification goes to **1.7** (T1 step 1
scoped, I6's write window, §7's index). Not in the brief: `eng/lib/Status.cs`
checks that the README's image tags name the latest release, because the
quick start named `preview.3` after `preview.4` shipped.

**The questions put during the work.** Two, with the maintainer's answers:

1. *Group commit puts several commits in one flush window, and power loss
   there can tear an earlier member while keeping a later one, which I6
   (spec 1.5) reads as damage and refuses.* — **Torn window**: a break
   within a format-level window W = 4 MiB of later records reads as torn;
   older, or under a sealed trailer, still refuses. The cost, stated in ADR
   0072's amendment and tested: bit rot inside the last W bytes drops
   commits that were never acknowledged as durable rather than refusing.
2. *Server-side retry for implicit preconditions contradicts ADR 0057 and
   ADR 0094.* — **Amend and retry**: up to three times, only where the
   precondition is the server's own inferred scope.

**The properties**, with what they found:

- **Batched equals unbatched** (`GroupCommitTests`): 4 seeds × 40 histories
  of 40 generated requests, submitted in generated groups held so that each
  is led as one batch, against the same requests one at a time with group
  commit off, under a ticking injected `TimeProvider`: outcomes, positions,
  deltas, the header chain and the log's bytes equal. Two seeds inject a
  default projection failure at a random k. 1,246, 1,262, 622 and 614
  commits in 588, 484, 269 and 276 writes. Beside it: no answer before the
  flush gate opens, a failed flush answers none, a request cancelled while
  queued is never written, one cancelled after it was taken is.
- **A crash inside a batch** (`FaultInjectionTests`): seeds 6151 and 6152,
  32 steps queued four at a time, a process crash at every operation and at
  every byte of every write, and power loss with writes reordered to the
  barrier: 12,614 and 13,261 injection points, each recovering to a closed
  commit no earlier than the last acknowledged. **Counterexamples found**:
  power loss kept a later member of a batch and tore an earlier one, which
  the reader called damage (the I6 question above); and after the torn
  tail was cut, an abandoned segment's trailer was decoded past its head
  ("unexpected end"), fixed by reading an abandoned segment only to its
  trailer's head. The I6 tests run with the window at 0 so that the strict
  rule is still checked, and a test states the window's cost.
- **Serialisability per scope** (`PerGraphConcurrencyTests`): 3 seeds × 300
  rounds of concurrent writers over six graphs — 632, 629 and 647 conflicts,
  413, 417 and 411 commits beside another writer — each final state equal
  to a serial order of the commits; and the graph index's rebuild (I8):
  2 seeds, 6,720 graph versions each, equal after reopening, after deleting
  `index/graphs`, after deleting `derived/`, and after a bulk commit.
- **A request's scope** (`ScopeTests`): 3 seeds × 300 generated histories,
  `FILTER NOT EXISTS` over a graph that may not exist and `GRAPH ?g` under
  `USING NAMED` among the generated requests: a request conflicts whenever
  an intervening commit touched what it read, never when it touched neither
  that nor what it writes, and every commit leaves the state evaluating it
  at the later head would (87, 74, 72 conflicts; 213, 226, 228
  serialisable commits). **Counterexample found**: a pattern whose constant
  the store had never held was never matched, so the recorder saw no read;
  a commit adding that constant did not conflict. Fixed by reading the
  pattern's graphs from the pattern itself as well, and mutation-checked.
  The oracle's own two bugs (a template's graph counted as read; a
  `Settings` fill at head + 1) were fixed in the test.

**Benchmarks**, honest row first, in `tests/Varve.Benchmarks/README.md`:
the memory backend with eight `INSERT DATA` writers is about 10% slower
(10,231 against 11,333 a second; 11,824 with group commit off), because there
is nothing to flush and batches arrive in bursts. On files the same row goes
2,418 → 5,766, p99 6.92 → 3.57 ms. Eight writers of a read-modify-write, one
graph each: 87,904 conflicts on files and 151,920 in memory before, none
after; 1,211 → 5,638 and 5,812 → 8,910. `GET` then `PUT` under `If-Match`:
71,368 and 157,821 retries before, none after. A lone commit in process:
0.27 ms against 0.24 ms (inside both runs' error), 1.5 KB more allocated.
The graph index: 64 bytes a graph on the heap, 16 on disk, open 198 ms at
10⁵ graphs and 263 ms at 10⁶.

**#35** is not met and stays open: 486.7 ms for 100,000 quads against
pyoxigraph's 430.5 ms; the phases are measured in the benchmarks README. (The
addendum below measures it against the commit before, in one run.)

**What the next milestones can rely on.** *Object storage* (#92): a request
is answered only after its batch is durable, a batch is one write of at most
4 MiB and 1,024 records, and a reader treats a break within the write window
as torn — so one object-store put per batch is the unit, and a put's
milliseconds are shared by every request that queued behind it.
`Dataset.GraphVersion` and the persisted index are derived, and rebuild from
the log. *Client and integration* (#93): `SparqlHttpClient.VersionOf` and the
conditional `GetGraphAsync`, `PutGraphAsync`, `CreateGraphAsync`,
`PostGraphAsync`, `DeleteGraphAsync` and `UpdateAsync(…, ifGraphsMatch, …)`;
a `409`'s `graphs` member; `UpdatePreconditions` in `Varve.Protocol.Model`.

**For the maintainer.**

- Group commit on the memory backend costs about 10% with eight writers.
  The alternative, off by default for `Durability.None`, is one line; the
  brief said "on every backend by default", so it is on. (Decided otherwise
  in review; see the addendum.)
- The allocation test in `Varve.Protocol.Tests` read 57 against 56 once under
  load from a parallel benchmark, and passed three times alone.
- A crash between writing a checkpoint's `.graphs` blob and the checkpoint
  can leave the blob without its checkpoint; it is never read (it names a
  position no checkpoint holds) and maintenance does not yet delete it.
- `ISparqlUpdateExecutor` changed signature: breaking for an implementation
  outside this repository, stated in the release notes.
- #15 is left to the maintainer, as asked.

## Addendum, 2026-10-14: the review

The maintainer's review of the pull request, verbatim:

> Yes: watch #104, fix CI failures, and answer review comments from me or the gates App. Report anything from anyone else to me rather than acting on it. Don't spend reruns on the expected `CS0618`.
>
> ## Decisions
>
> 1. **Group commit is off by default for `Durability.None`.** Batching buys a write one flush, and the in-memory backend has no flush, so on that backend it's pure leader overhead. That's the 10% you measured. Make the default follow durability: off for `None`, on for every durable backend, the browser's included. ADR 0124 is unaccepted, so edit it in place and say why. The equivalence property already sets `GroupCommit` explicitly on both sides, so the mechanism stays proven on every backend. Check that it does, and that no property relies on the default. Re-run the eight-writer memory row to confirm it's back at baseline.
>
> 2. **The flaky allocation test gets fixed in this PR.** A gate that reads 57 against 56 depending on what else is running is measuring the process, not the code. If it uses `GC.GetTotalAllocatedBytes`, switch to `GC.GetAllocatedBytesForCurrentThread` around a synchronous section. If the code under test hops threads, move the test into a non-parallel collection. Say in the record which it was.
>
> 3. **Orphaned graph-index files: fix in this PR.**
>    - **Write order:** write the checkpoint first and its graph-index file after. A checkpoint whose index file is missing rebuilds the index from the log, as a missing `index/graphs` does today. The orphan case then can't arise.
>    - **Sweep:** opening a dataset deletes any unreferenced graph-index file in `derived/`. That's safe because `derived/` is droppable by definition.
>    - **Proof:** add the crash point to the fault suite.
>
> ## #35: one question
>
> The 7a and operability records had `INSERT DATA` at 1.04–1.08× pyoxigraph. 486.7 against 430.5 ms is 1.13×. Is that the same workload and machine, and was the row measured before your change in this run? If group commit or the graph-version index made the fast path slower, that's a regression, not "#35 stays open". Put the before and after for this row, same run, in the benchmarks README. If the graph-version update on the fast path is the cost, it needs to be per commit, not per quad.

**1. Group commit follows durability** (`7139315`). Unset,
`DatasetOptions.GroupCommit` is `GroupCommitOptions.For(durability)`: off on
`Durability.None`, on for files (`Synchronised`) and the browser's OPFS and
IndexedDB (`Committed`); set, it applies on every backend. The server passes
its bounds to durable datasets only. ADR 0124 edited in place. The
equivalence property did set `GroupCommit` on both sides; four other tests
relied on the default for their batching — the flush gate, both
cancellation tests and the concurrent writers — and the serialisability
property for its batched half; all now set it, and a new test checks the
default on both kinds of backend. The memory row, re-measured with the
before binary in the same run: 10,158 a second before the milestone, 13,887
after.

**2. The allocation flake** (`93e1ce9`). It was neither of the two cases the
review named. The request crosses Kestrel's threads, so a per-thread counter
cannot see it, and the class already ran in a non-parallel collection. The
figure was the socket's: on four busy cores it read 57 on every case, on
`v0.1.0-preview.4` as on this branch, 56 quiet, and 55 once on a Windows
runner. A flush that waits for a slow reader boxes its awaits (Varve's
`ResponseOutput.FlushAsync` among them, and Kestrel's), and a woken pipe
reader is a thread-pool work item, so the bytes grow with how far behind the
reader is — at 66,000 rows by up to 4 bytes a solution even when quiet. Two
probes that did not cure it are recorded so they are not repeated: a
transport pool that never evicts, and no response-buffer limit. The cure is
an in-memory Kestrel transport in the test host whose pipes never make a
writer wait, run continuations inline, and keep every block: 56 in six runs
of all four cases on four busy cores. Not pursued, and offered: the
per-flush boxing in `ResponseOutput` could use a pooling builder, which
would cut what a real slow client costs the server. A size effect seen on
the way, not timing: at 66,000 rows the distinct-subject shapes read 58.

**3. The orphaned graph index** (`81f8f89`). The write order was already
checkpoint first, index after; the orphan came from the drop, which deleted
the checkpoint first. The drop now deletes the index first, and opening
deletes any `<P>.graphs` whose checkpoint is not one of this log's. A crash
at every one of 211 operations of a workload that writes and drops
checkpoints opens with no orphan and every graph version right (199 images
had a log to open); with the old order the test fails at operation 113.

**#35.** Same workload as Operability's, one interleaved run: before
(`2472e1a`) 427.8 and 472.2 ms at 100,000 quads, after 447.6 and 461.2 ms,
allocation 258.88 MB on both sides, pyoxigraph 393.2 ms. No regression; the
graph-version update is per commit, nothing per quad. The 1.13× of the first
report was this branch alone on a slower run; today both sides are at
1.09–1.20×.

**CI on the way.** `container (linux-x64)` and `auth (zitadel)` failed
pulling images (`toomanyrequests` from the registry), not this pull
request's; said once on the pull request. `pipeline (devcontainer)` failed
restoring: the audit data of 2026-10-14 flags
`System.Security.Cryptography.Pkcs` through 9.0.20 and 10.0.12, which the
analyzer harness reaches through `NuGet.Packaging`; pinned to 10.0.13 under
ADR 0004 (`312a3f1`), as `AngleSharp` is pinned; `main`, with the same graph, would fail the same
way.

