---
set: client-idempotency-keys
namespace: varve
adr: 0246
decisions:
  - key: KeyIsScopedToTheAgent
    statement: "A write may carry Idempotency-Key, one Structured Field string of 1 to 255 printable ASCII characters, and the key is the pair of the commit's agent and the key, so one agent's key never matches another's and the cause stays server-minted"
  - key: KeyRidesInTheCommit
    statement: "A keyed commit carries the host attachment varve:idempotencyKey, the canonical JSON of the key, the request's fingerprint, the status it answers and its Location, with no byte of format v1 changed"
  - key: KeysKeptForAWindowOfCommits
    statement: "The writer honours a key while its commit is among the dataset's last Window commits, 100000 by default, read from the log before its first keyed check and kept under the request's claim; never bounded by time"
  - key: KeyCheckedBeforePreconditions
    statement: "The key is looked up after authorisation and before any precondition, so a retry whose original moved the head is answered from its commit and not 412"
  - key: ReplayAnswersTheOriginalOutcome
    statement: "A request with the same agent, key and fingerprint as a commit in the window is answered with that commit's status, Location, position and request id, and Varve-Replayed, without committing again"
  - key: ReusedKeyWithAnotherRequestIs422
    statement: "The same agent's key with another fingerprint is 422 idempotency-key-reused with the original's position, and commits nothing"
  - key: KeyInFlightIs409
    statement: "A key a request of the same agent holds while it is answered is 409 idempotency-key-in-flight with Retry-After 1"
  - key: OnlyACommitRecordsAKey
    statement: "NoChange, Conflict, Rejected, a precondition's failure and every refusal record no key, so a retry of one is evaluated afresh"
  - key: ClientSendsAndRetriesWithAKey
    statement: "Varve.Protocol.Client sends a fresh key on every write by default and retries it with the same key, bounded, on a lost response, 503 writer-moved, writer-stepped-down, writer-superseded, unavailable and 409 idempotency-key-in-flight"
---

The rulings of [ADR 0246](../adr/0246-client-idempotency-keys.md), filed unaccepted for #132
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
