# 0246 — Client idempotency keys, scoped to the agent: a retried write commits once

## Status

**Accepted — filed unaccepted by the session for #132, 2026-11-16** (ADR 0066),
and accepted by the maintainer on 2026-11-18, written by his delegate
(ADR 0239). The plan was posted on
[#132](https://github.com/Hafeok/Varve/issues/132). The maintainer's review of
2026-11-16 made four changes, which are folded in below:
- the commit records a hash, not the key;
- a retry past the window is refused;
- the client sends a key by default;
- the index's rebuild is measured, which brought #310 into this change.

It is numbered 0246, because 0244 and 0245 are taken on open branches.

**Amends** [0094](0094-a-write-over-http-is-one-commit.md) (a lost response's
commit is found by the client's key, never by a client-chosen cause) and
[0168](0168-write-routing-by-forwarding.md) (the key's headers are forwarded),
each by a dated amendment in its own file. It supersedes nothing. It records
the decision set
[`client-idempotency-keys`](../decisions/client-idempotency-keys.md).

## Context

When a write's response is lost, the client can't tell whether the write
committed. ADR 0094 makes the cause a request id that the server mints, and
its amendment of 2026-10-09 refuses a client-chosen one, because such an id
can be forged and can collide. A client whose response never arrived
therefore holds no id to look up in the commits resource. ADR 0168 adds a hop
where a response can be lost, and leaves client idempotency keys to this
issue. The maintainer decided on #105 that keys are scoped to the
authenticated agent.

`draft-ietf-httpapi-idempotency-key-header` names the header and its
answers:
- `Idempotency-Key`, a Structured Field string;
- `422` for a key reused with a different payload;
- `409` while a request with the key is still being processed.

## Decision

1. **The header.** A write may carry `Idempotency-Key`: one Structured Field
   string (RFC 9651 §3.3.3) of 1 to 255 printable ASCII characters. A key
   that is malformed, empty or longer, or more than one key, is `400` and
   commits nothing.

   The key is honoured on:
   - SPARQL Update;
   - Graph Store `PUT`, `POST` and `DELETE`;
   - the settings resource;
   - every management write that is one commit.

   The event-schema move (ADR 0167) is several commits and is idempotent by
   its own rule, so it records no key. Reads ignore the header.
2. **Scoped to the agent.** A key is the pair (agent, key), where the agent is
   ADR 0094's `<issuer>#<subject>` IRI. One agent's key never matches
   another's, so the key can't be used to forge or collide with another
   caller's write. The cause stays the request id the server mints. In
   anonymous mode the agent is none, and all anonymous callers share that one
   scope. Anonymous mode is refused in production.
3. **A hash rides in the commit, never the key.**
   - A keyed commit carries a host attachment (ADR 0165), so no byte of
     format v1 changes. The attachment is the literal
     `"…"^^<https://w3id.org/varve/ns#idempotencyKey>`.
   - Its lexical form is the canonical JSON (RFC 8785) of four members:
     - `keyHash`: SHA-256, in lowercase hex, over the agent's IRI, a NUL, and
       the key. The agent is empty in anonymous mode. NUL occurs in neither
       an IRI nor a key, so two pairs collide only if SHA-256 does.
     - `fingerprint`: SHA-256 over the method, the path and query, the
       `Content-Type` and the body bytes;
     - `status`: what the write answers when it commits, `201` or `204`;
     - `location`: its `Location`, when it has one.
   - The key itself never enters the log.
   - The fingerprint covers what a forward leaves unchanged (ADR 0168 point 3).
   - **The attachment is never served.** It belongs to the writer. The
     change feed and the commits resource leave it out of a commit's
     `attachment` lines, for every caller, as they already leave the shapes
     reports out for a row-restricted one (ADR 0237). The CloudEvents
     rendering (ADR 0143) renders no attachment at all.
4. **The writer's index is read from the log.**
   - **What it holds.** For each open `Dataset`, the system dataset included,
     the writer keeps the first sixteen bytes of each key hash in the window,
     with its commit's position. A hit reads that commit's header again, and
     checks it against the whole hash and the fingerprint. The status,
     `Location` and request id come from that header too.
   - **When it reads the log.** Before each keyed check, it reads the commits
     it hasn't read yet, up to the head and within the window, four at a
     time. It reads their headers alone, through
     `Dataset.ReadMetadataAsync` (#310). That read takes only the header at
     the tail of the commit's closing record, checks it against the commit
     index's hash, and never decodes the delta.
   - **So keys survive a writer change.** A predecessor's keyed commits are
     known after a restart, a handover (abdication, takeover, claim in place)
     or a reopen, because they are in the same log.
   - **Read ahead, in the background.** The server calls
     `IdempotencyKeys.PrepareAsync` whenever it becomes a dataset's writer:
     on opening it as the writer, on a claim in place, and for the system
     dataset.
     - The read runs on the thread pool, and the server does not wait for
       it, so neither the open nor a takeover is made longer.
     - A keyed write that arrives before the read finishes waits for it, then
       reads only what it didn't reach.
     - If the read fails, it is logged, and the first keyed write reads the
       log itself.
     - Decided by the maintainer on #132, 2026-11-16.
   - **Its memory.** About 56 bytes per keyed commit in the window: 5.4 MiB
     for a full window of 100,000 keyed commits, measured at REC M.
     `docs/operator/small-machine.md` gives the figure per written dataset.
   - **Claims.** A request holds its key, as a claim, from the lookup until it
     is answered. On one writer, two requests with one key therefore never
     reach the sequencer together.
5. **A window of commits, never time.**
   - A key is honoured while its commit is among the dataset's last
     `Varve:Limits:IdempotencyWindow` commits, 100 000 by default.
   - The window is a function of the log alone, so every writer of the
     dataset agrees on it.
   - Keeping keys for ever needs an index in `derived/`. That is #309, and it
     stays out of 1.0.
6. **A retry past the window is refused.**
   - A write may carry **`Varve-Key-Position: <p>`**, the head position the
     client knew when it made the key.
   - If the key isn't found, and the head the index was read to is more than
     the window past `p`, the original may have committed at a position the
     window no longer covers. Whether it did can't be determined.
   - The write is then answered **`422 idempotency-key-expired`**, with
     members `keyPosition`, `headPosition` and `window`, and nothing is
     committed.
   - When the head is within the window of `p`, any commit the original made
     is at a position above `p`, so inside the window. A key not found there
     is a write that never committed, and it commits.
   - A key that is found is answered from its commit, whatever `p` says.
   - **What a client is promised.** Decided by the maintainer on #132,
     2026-11-16.
     - **With `Varve-Key-Position`: once, or refused.** A retry of a keyed
       write either commits it exactly once or is refused. It is never
       committed twice, however late the retry comes.
     - **Without it: once within the window.** A retry is committed at most
       once while the original's commit is among the window's commits. A
       retry later than that cannot be told from a new write, and may commit
       a second time.
     - `Varve.Protocol.Client` always sends the header on a retry, with the
       head it knew when it made the key, or 0 when it knew none, so its
       writes have the first guarantee. A client that sends no header has
       only the second.
7. **Checked before every precondition.** The key is looked up after
   authorisation and before `If-Match`, `Varve-If-Graphs-Match` or a graph's
   version. So a retry whose original moved the head is answered from its
   commit, not with `412`.
8. **The answers.**

   | Case | Answer |
   |---|---|
   | No commit holds the key, within the window of `Varve-Key-Position` or with none | Proceeds; if it commits, the key's hash is recorded with the commit |
   | Same agent, key and fingerprint as a commit in the window | That commit's status, `Location`, `Varve-Position`, `ETag` and `Varve-Request-Id`, and `Varve-Replayed: true`; nothing is committed again |
   | Same agent and key, another fingerprint | `422 idempotency-key-reused`, with `position`, the original's; nothing committed |
   | Not found, more than the window past `Varve-Key-Position` | `422 idempotency-key-expired`, with `keyPosition`, `headPosition` and `window`; nothing committed |
   | A request of the same agent holds the key | `409 idempotency-key-in-flight`, `Retry-After: 1`; nothing committed |

9. **Only a commit records a key.** `NoChange`, `Conflict`, `Rejected`, a
   failed precondition and every refusal record nothing, so a retry of any of
   them is evaluated afresh.
   - A write whose outcome is unknown (ADR 0214) releases its claim. Its
     retry reads the log: if the commit is there, the retry is answered from
     it, and otherwise the retry commits.
   - A management route's own checks (that a dataset exists, that it is
     closed) run before the key is looked up. None of them commits, so they
     can answer a retry differently from its original, but they never commit
     it twice.
10. **The client sends and retries by default.**
    - `ClientLimits.IdempotencyKeys` is the source of each logical write's
      key. By default it is 128 random bits from the operating system, in
      lowercase hex. It is asked once per logical write, and that key is
      reused on every retry of the write. A host may inject its own source;
      null sends no key, and then no write is retried.
    - Every response's `Varve-Position` tells the client the newest head it
      has seen. When it makes a key, it keeps that head, or 0 if it has seen
      none, and every retry sends it as `Varve-Key-Position`.
    - A first attempt sends no `Varve-Key-Position`, since it is not a retry.
      So an idle client's first write is never refused for being stale. A
      head of 0 only ever refuses more.
    - With a key, the client sends the write again with the same key and
      position, up to `ClientLimits.WriteRetries` times (3 by default). It
      does so only after:
      - a lost response, where no answer arrived;
      - a refusal the server states as retryable, by problem type:
        - `503 writer-moved`, `writer-stepped-down` and `writer-superseded`:
          the writer changed, nothing was committed, and the next attempt is
          routed anew;
        - `503 shutting-down`: the server drains;
        - `503 management-behind`: the write's grants were behind its
          dataset's (ADR 0165);
        - `503 commit-outcome-unknown`: the key settles it (ADR 0214);
        - `503 unavailable`, but only with the reason `no-writer` or
          `writer-unreachable`, where nothing reached a writer;
        - `409 idempotency-key-in-flight`.
    - **Every other answer is final, and every other `503` is not retried.**
      That covers:
      - `unavailable` with any other reason, or none: a dataset that failed,
        or a sequencer broken by an append or by running out of memory;
      - `read-limit-exceeded`, `not-ready`, `load-spill-damaged`, and a `503`
        with no Varve problem;
      - a `503` with `Retry-After`. That header times a retry; it never
        decides one, because a broken sequencer's `unavailable` carries it
        too.
    - **A memory limit is never retried:** `memory-limit-exceeded` is a
      `422`, and a sequencer out of memory answers `unavailable` with no
      routing reason. Decided by the maintainer on #320, 2026-11-18.
    - Between attempts it waits for the server's `Retry-After` (at most five
      seconds), or else 100 ms, doubled each time.
    - **Its guarantee** is point 6's first: once, or refused.
    - **Why ADR 0011 is satisfied.** ADR 0011 bans ambient randomness so that
      §10's determinism property holds: two stores fed the same requests
      write `log/` byte for byte alike, and the store draws no randomness of
      its own. The key is the client's input to a write, chosen outside any
      store, as its body is. The store records a hash of it, derived from the
      request, and replaying the same requests (keys included) replays the
      same bytes. No store, and no answer a comparison checks, depends on the
      client's draw. The client package is under the ban list
      (`VarveDeterministic`), so its one draw, `ClientLimits`' default
      source, is the one suppression of `RS0030`, citing this ADR.

## Alternatives considered

- **A client-chosen cause.** Refused by ADR 0094's amendment: a cause is
  something the server vouches for.
- **The key itself in the commit.** Anyone who reads the log or the feed
  would then hold it. The hash is all a writer needs to find it again.
- **A key global to the dataset.** One agent could then collide with
  another's key and be answered with that write's outcome, which is the
  forgery ADR 0094 refused. Scoping by agent costs nothing, because the hash
  includes the agent.
- **Keys kept for a duration.** #132 rules out elapsed time. Time also isn't
  a function of the log, so two writers could disagree about a key.
- **Keys kept for ever, in `derived/`.** Correct, and #309, but it is a
  derived format with its own rebuild and verification (ADR 0213), and it
  stays out of 1.0. The refusal past the window makes the window safe in the
  meantime: a client is told, never committed twice.
- **Checking the key in a validator under the sequencer.** It would see
  every commit in order. But a validator can't learn whether the batch it
  accepted became durable, so a failed batch would leave a key held with no
  commit behind it. The claim lives outside the sequencer and is settled
  with the write's outcome.
- **The host supplies every key source.** That was the first draft. It left
  `AddVarveClient` and the web UI unkeyed by default, and #132 asks for keys
  by default.

## Consequences

- **Problem types:** `idempotency-key-reused` (`422`),
  `idempotency-key-expired` (`422`) and `idempotency-key-in-flight` (`409`)
  are new.
- **Headers:** `Idempotency-Key`, `Varve-Key-Position` and the response's
  `Varve-Replayed`.
- **`Varve.Store`** gains `Dataset.ReadMetadataAsync` and
  `CommitHeaderTerms` (#310). The management order's seed and the event
  catalogue's recorded position use them too.
- **`Varve.Protocol`** gains `IdempotencyKeys` (with `PrepareAsync`),
  `CommitWindow` and `ProtocolOptions.Idempotency`.
- **`Varve.Protocol.Client`** gains `ClientLimits.IdempotencyKeys`, keying by
  default, and `WriteRetries`.
- **The server** gains `Varve:Limits:IdempotencyWindow`.
- **The change feed's `attachment` lines** leave the idempotency attachment
  out. Change feed specification §2 says so.
- **Measured** at REC M over the full window (#132): the rebuild after a
  restart and after a takeover, its time and the heap it holds.
  - It takes 311 to 468 ms and holds 2.6 to 5.4 MiB.
  - It runs in the background, after the open or the claim.
- **Replication part 2 (#166)** must carry each commit's agent and
  attachments unchanged, and must pass both headers through any new hop, as
  the comment on #132 sets out.

## Checks

- **Checked against the accepted ADRs** (0001–0243). It touches:
  - **0011** (the client's key draw, satisfied as set out in point 10);
  - **0037** and **0094** (the agent; the cause untouched; a dated amendment);
  - **0165** (a host attachment, no format change; this one not served);
  - **0168** (the headers forwarded; a dated amendment);
  - **0214** (an unknown outcome settled by the key);
  - **0237** (attachments left out of the feed);
  - **0115** (one known setting).

  No conflict.
- **Layer ownership.**
  - `Varve.Store` (4): the header-only read.
  - `Varve.Protocol` (5): the headers, the index and the answers.
  - `Varve.Protocol.Client` (5): sending and retrying.
  - `Varve.Server` (6): the setting.
- **Analyzer rule.** None.
- **Open questions owned.** Keys kept for ever in `derived/` are #309,
  after 1.0.
