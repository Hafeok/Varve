---
set: client-idempotency-keys
namespace: varve
adr: 0246
decisions:
  - key: KeyIsScopedToTheAgent
    statement: "A write may carry Idempotency-Key, one Structured Field string of 1 to 255 printable ASCII characters, and the key is the pair of the commit's agent and the key, so one agent's key never matches another's and the cause stays server-minted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: KeyRidesInTheCommit
    statement: "A keyed commit carries the host attachment varve:idempotencyKey, the canonical JSON of the SHA-256 of the agent and the key, the request's fingerprint, the status it answers and its Location; the key itself never enters the log, and no byte of format v1 changes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: KeysKeptForAWindowOfCommits
    statement: "The writer honours a key while its commit is among the dataset's last Window commits, 100000 by default, holding the key's hash read from the log before its first keyed check and kept under the request's claim; never bounded by time"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: RetryPastTheWindowIsRefused
    statement: "A write may carry Varve-Key-Position, the head the client knew when it made the key; a key not found whose position is more than the window below the head is 422 idempotency-key-expired, because whether its original committed cannot be determined, and commits nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: OnceOrRefusedWithTheKeyPosition
    statement: "A keyed retry with Varve-Key-Position commits its write once or is refused, never twice; without it, a retry commits at most once while the original's commit is in the window"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: KeysReadAheadInTheBackground
    statement: "The server starts reading a dataset's keys from its log whenever it becomes the dataset's writer, without waiting for it, so the open and a takeover are no longer; a keyed write that arrives first waits for that reading"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: KeyCheckedBeforePreconditions
    statement: "The key is looked up after authorisation and before any precondition, so a retry whose original moved the head is answered from its commit and not 412"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: ReplayAnswersTheOriginalOutcome
    statement: "A request with the same agent, key and fingerprint as a commit in the window is answered with that commit's status, Location, position and request id, and Varve-Replayed, without committing again"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: ReusedKeyWithAnotherRequestIs422
    statement: "The same agent's key with another fingerprint is 422 idempotency-key-reused with the original's position, and commits nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: KeyInFlightIs409
    statement: "A key a request of the same agent holds while it is answered is 409 idempotency-key-in-flight with Retry-After 1"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: OnlyACommitRecordsAKey
    statement: "NoChange, Conflict, Rejected, a precondition's failure and every refusal record no key, so a retry of one is evaluated afresh"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
  - key: ClientSendsAndRetriesWithAKey
    statement: "Varve.Protocol.Client sends a key of 128 random bits on every write by default, made once per logical write from an injectable source, with the head it knew then as Varve-Key-Position, and retries with both, bounded, only on a lost response and the refusals the server states as retryable: 503 writer-moved, writer-stepped-down, writer-superseded, shutting-down, management-behind, commit-outcome-unknown, unavailable with reason no-writer or writer-unreachable, and 409 idempotency-key-in-flight; every other 503 is final, Retry-After or not, and a memory limit is never retried"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-18T00:00:00Z
---

The rulings of [ADR 0246](../adr/0246-client-idempotency-keys.md), filed unaccepted for #132
(ADR 0066) and accepted by the maintainer on 2026-11-18, written by his delegate (ADR 0239).
