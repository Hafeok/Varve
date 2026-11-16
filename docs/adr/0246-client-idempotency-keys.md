# 0246 — Client idempotency keys, scoped to the agent: a retried write commits once

## Status

**Proposed — filed unaccepted by the session for #132, 2026-11-16** (ADR 0066).
Acceptance is the maintainer's act, or his delegate's for a decision he has
read (ADR 0239). The plan is posted on
[#132](https://github.com/Hafeok/Varve/issues/132). Numbered 0246, because 0244
and 0245 are taken on open branches.

**Amends** [0094](0094-a-write-over-http-is-one-commit.md) (a lost response's
commit is found by the client's key, never by a client-chosen cause) and
[0168](0168-write-routing-by-forwarding.md) (`Idempotency-Key` is forwarded),
each by a dated amendment in its own file. It supersedes nothing. It records
the decision set
[`client-idempotency-keys`](../decisions/client-idempotency-keys.md).

## Context

When a write's response is lost, the client can't tell whether the write
committed. ADR 0094 makes the cause a request id that the server mints, and
its amendment of 2026-10-09 refuses a client-chosen one, because such an id
can be forged and can collide. A client whose response never arrived
therefore holds no id to look up in the commits resource. ADR 0168 adds a
hop where a response can be lost, and leaves client idempotency keys to this
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
3. **The key rides in the commit.**
   - A keyed commit carries a host attachment (ADR 0165), so no byte of
     format v1 changes. The attachment is the literal
     `"…"^^<https://w3id.org/varve/ns#idempotencyKey>`.
   - Its lexical form is the canonical JSON (RFC 8785) of four members:
     - `key`;
     - `fingerprint`: SHA-256, in lowercase hex, over the method, the path
       and query, the `Content-Type` and the body bytes;
     - `status`: what the write answers when it commits, `201` or `204`;
     - `location`: its `Location`, when it has one.
   - The commit's own agent is the key's agent, so the attachment doesn't
     repeat it.
   - The fingerprint covers what a forward leaves unchanged (ADR 0168 point 3).
4. **The writer's index is read from the log.**
   - For each open `Dataset`, the system dataset included, the writer keeps
     (agent, key) → (position, fingerprint, status, location, request id).
   - Before each keyed check it reads the commits it hasn't read yet, up to
     the head and within the window. So a predecessor's keyed commits are
     known after a restart, a handover (abdication, takeover, claim in place)
     or a reopen, because they are in the same log.
   - A request holds its key, as a claim, from the lookup until it is
     answered. On one writer, two requests with one key therefore never
     reach the sequencer together.
5. **A window of commits, never time.**
   - A key is honoured while its commit is among the dataset's last
     `Varve:Limits:IdempotencyWindow` commits, 100 000 by default. Once its
     commit is older, the key is forgotten, and a retry with it commits
     again.
   - The window is a function of the log alone, so every writer of the
     dataset agrees on it.
   - Keeping keys for ever needs an index in `derived/`, which is a format
     question. It is a follow-up and is not built here.
6. **Checked before every precondition.** The key is looked up after
   authorisation and before `If-Match`, `Varve-If-Graphs-Match` or a graph's
   version. So a retry whose original moved the head is answered from its
   commit, not with `412`.
7. **The answers.**

   | Case | Answer |
   |---|---|
   | No commit holds the key | Proceeds; if it commits, the key is recorded with the commit |
   | Same agent, key and fingerprint as a commit in the window | That commit's status, `Location`, `Varve-Position`, `ETag` and `Varve-Request-Id`, and `Varve-Replayed: true`; nothing is committed again |
   | Same agent and key, another fingerprint | `422 idempotency-key-reused`, with `position`, the original's; nothing committed |
   | A request of the same agent holds the key | `409 idempotency-key-in-flight`, `Retry-After: 1`; nothing committed |

8. **Only a commit records a key.** `NoChange`, `Conflict`, `Rejected`, a
   failed precondition and every refusal record nothing, so a retry of any of
   them is evaluated afresh.
   - A write whose outcome is unknown (ADR 0214) releases its claim. Its
     retry reads the log: if the commit is there, the retry is answered from
     it, and otherwise the retry commits.
   - A management route's own checks (that a dataset exists, that it is
     closed) run before the key is looked up. None of them commits, so they
     can answer a retry differently from its original, but they never commit
     it twice.
9. **The client sends and retries.**
   - `ClientLimits.IdempotencyKeys` is the source of each write's key, and
     the host supplies it. The key is recorded in the log, so the client
     draws no ambient randomness for it (ADR 0011; the ban in
     `eng/BannedSymbols.Deterministic.txt`). This follows the follow's
     jitter.
   - The `varve` command line supplies the operating system's randomness at
     its composition root. A client with no source sends no key and retries
     no write.
   - With a key, the client sends the write again with the same key, up to
     `ClientLimits.WriteRetries` times (3 by default). It does so after:
     - a lost response;
     - any `503`;
     - `409 idempotency-key-in-flight`.
   - Between attempts it waits for the server's `Retry-After` (at most five
     seconds), or else 100 ms, doubled each time.

## Alternatives considered

- **A client-chosen cause.** Refused by ADR 0094's amendment: a cause is
  something the server vouches for.
- **A key global to the dataset.** One agent could then collide with
  another's key and be answered with that write's outcome, which is the
  forgery ADR 0094 refused. Scoping by agent costs nothing, because the
  commit already records the agent.
- **Keys kept for a duration.** #132 rules out elapsed time. Time also isn't
  a function of the log, so two writers could disagree about a key.
- **Keys kept for ever, in `derived/`.** Correct, and the eventual answer,
  but it is a derived format, with its own rebuild and verification
  (ADR 0213). The window gives the guarantee a client retrying within
  minutes needs.
- **Checking the key in a validator under the sequencer.** It would see
  every commit in order. But a validator can't learn whether the batch it
  accepted became durable, so a failed batch would leave a key held with no
  commit behind it. The claim lives outside the sequencer and is settled
  with the write's outcome.
- **A default key drawn by the client library.** Banned by ADR 0011: the key
  is in the log.

## Consequences

- **Problem types:** `idempotency-key-reused` (`422`) and
  `idempotency-key-in-flight` (`409`) are new.
- **`Varve.Protocol`** gains `IdempotencyKeys`, `CommitWindow` and
  `ProtocolOptions.Idempotency`.
- **`Varve.Protocol.Client`** gains `ClientLimits.IdempotencyKeys` and
  `WriteRetries`.
- **The server** gains `Varve:Limits:IdempotencyWindow`.
- **Commits carry a new attachment datatype.** A reader of an older version
  reads it as an opaque term, as it reads any (ADR 0165).
- **The first keyed write after a writer opens a dataset** reads up to the
  window's commits from the log. Reading headers alone, without deltas, is a
  follow-up.
- **Replication part 2 (#166)** must carry each commit's agent and
  attachments unchanged, and must pass the header through any new hop, as
  the comment on #132 sets out.

## Checks

- **Checked against the accepted ADRs** (0001–0243). It touches:
  - **0011** (no ambient randomness: the key's source is the host's);
  - **0037** and **0094** (the agent; the cause untouched; a dated amendment);
  - **0165** (a host attachment, no format change);
  - **0168** (the header forwarded; a dated amendment);
  - **0214** (an unknown outcome settled by the key);
  - **0115** (one known setting).

  No conflict.
- **Layer ownership.**
  - `Varve.Protocol` (5): the header, the index and the answers.
  - `Varve.Protocol.Client` (5): sending and retrying.
  - `Varve.Server` (6): the setting and the CLI's key source.
- **Analyzer rule.** None.
- **Open questions owned.** Keys kept for ever in `derived/`, and the
  header-only commit read, are filed as follow-ups of #132.
