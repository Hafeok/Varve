---
set: header-chain-and-divergence
namespace: varve
adr: 0014
decisions:
  - key: HeaderChainsToPrevious
    statement: "Every commit header carries prev, the hash of the previous commit's header, with a fixed value at position 1"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: HeaderCommitsToContent
    statement: "Every commit header carries content, the hash of (alloc, A, R), so the chain commits to every byte of the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DivergenceFoundByComparison
    statement: "Two logs with a common prefix and different continuations are divergent, and the branch point is the first position whose chain values differ"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: RefuseRatherThanGuess
    statement: "A store refuses to open a log whose chain does not verify, and refuses to continue from a head that is not its own"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ChainDetectsDoesNotAuthenticate
    statement: "The header chain detects accidental divergence; it does not prevent a fork and does not authenticate who wrote a commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: RefusalNamesPositionAndBranch
    statement: "A chain refusal says which position failed and where the branch point was"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DurableFormatVersioned
    statement: "The durable log format carries a version discriminator, so a change to what is hashed is a stated migration rather than a corruption"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0014](../adr/0014-header-chain-and-divergence.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
