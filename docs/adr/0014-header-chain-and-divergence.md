# 0014 — Header chain and divergence detection

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
I6 and the `prev`/`content` fields of a commit header (§1).

## Context

§2 assumes the dataset directory will be copied, synchronised and checked into
version control by people and tools the store knows nothing about. That
assumption is what makes this ADR necessary rather than a nicety.

Once a log can be copied, two copies can be continued independently. Position 7
in one copy and position 7 in another are then different commits with the same
name, and every artefact that refers to a position — an as-of read, a
subscriber's resume point, a projection's stored position, a checkpoint, an
acceptance in some future ledger — silently means something different depending
on which copy it is read against. Nothing in the positional model detects that,
because positions are exactly the thing that agree.

A second and more mundane failure has the same shape: a log that is corrupted
in transit, or partially restored from a backup, produces commits that are
individually well-formed and collectively not the history anything claims.

## Decision

**Every commit header carries `prev`, the hash of the previous commit's header,
with a fixed value at position 1.** The headers form a chain, so a commit's
identity depends on the entire history before it.

**Every commit header carries `content`, the hash of `(alloc, A, R)`** — so the
header commits to the body, and the chain therefore commits to every byte of
the log.

**Two logs with a common prefix and different continuations are detectably
divergent.** The first position at which the chain values differ is the
branch point, found by comparison rather than by inference.

**A store refuses rather than guesses.** It refuses to open a log whose chain
does not verify, and it refuses to continue from a head that is not its own.

### What this is and is not

It is **detection**, not prevention and not authentication. The chain proves
that two logs are not the same history, and localises where they parted. It
does not prevent a fork, does not resolve one, and — because nothing signs the
chain — does not prove who wrote a commit or that a whole log was not rebuilt
from scratch by someone with write access to the directory. An attacker who can
rewrite every header can produce a self-consistent log.

Stating that plainly matters because a hash chain looks like tamper-proofing and
is not. What it defends against is *accident*: divergent copies, partial
restores, truncated transfers, a well-meaning sync tool merging two directories.
Those are the failures §2 makes likely.

## Alternatives considered

- **Nothing — rely on positions and timestamps.** What most single-writer logs
  do, and adequate when the log is never copied. Rejected by §2: the copying is
  not hypothetical, it is assumed, and the failure it produces is silent.
- **A dataset UUID and a generation counter**, compared on open. Catches
  "different dataset" and "stale replica" cheaply, with no hashing. Rejected as
  insufficient rather than wrong: two continuations of one copy share the UUID
  and can share the generation, which is exactly the case that matters. Worth
  having *as well* — it makes the common mismatch a fast rejection — but it is
  not a substitute.
- **Hash the body only, no chain.** Detects corruption of a commit, which is the
  cheaper half. Rejected: it says nothing about order or about history, so two
  logs whose commits are each individually intact but differently ordered both
  verify.
- **A Merkle tree over commits** rather than a linear chain. Allows proving
  membership of one commit without the whole history, which matters for a
  replica that wants to verify a range. Rejected for now as solving a problem we
  do not have: there is one writer and replication is log shipping, so a replica
  has the prefix. Revisit if partial or out-of-order replication is ever
  proposed — it would be a superseding ADR, and it would change the header
  format, so the earlier the better if it is coming.
- **Sign the chain.** Would turn detection into authentication. Deliberately out
  of scope: it needs a key, key custody, rotation and revocation, and §9 already
  establishes that the one place keys must not live is the dataset directory.
  A signing scheme is its own decision and should not ride in on this one.

## Consequences

**The header format is fixed early and is expensive to change.** `prev` and
`content` are inputs to every subsequent header, so changing what is hashed, or
how, invalidates every chain ever written. This is a format decision in the
same class as the id scheme (ADR 0012): it should be made once, and the
milestone 6 storage format must carry a version discriminator so that a future
change is a stated migration rather than a corruption.

**Hashing is on the write path.** One hash of the header and one of the body per
commit — negligible for an ordinary commit, and for a bulk load, proportional to
the data, which it already is.

**A refusal is a real operational state.** "This log does not verify" and "this
is not my head" are conditions an operator will meet, most often after restoring
a backup or copying a directory while a write was in flight. They need to be
diagnosable: the error has to say which position failed and what the branch
point was, or it will be met with a shrug and a `rm -rf derived/`.

**It gives divergence a vocabulary before branching exists.** The brief asks that
milestone 2 not block branching and merging of datasets. The chain does not
implement it, but it means a future merge tool can *find* the common ancestor
rather than being told it. ADR 0012 notes that the id scheme is what decides
whether such a merge is a join or a re-derivation; this decision is what decides
whether it can locate the fork at all.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0013). Touches
  **0011** (the sequencer computes `prev` and `content` inside the serialised
  region, since both depend on the head it is extending) and **0013** (the
  header belongs to the commit, and is durable as part of the closing record, so
  a discarded tail leaves the chain intact). No conflict with any.
- **Layer ownership.** The header, the chain and the verification rule are owned
  by `Varve.Store` at **layer 4**. The hash primitive is BCL
  (`System.Security.Cryptography`, SHA-2 family), which is available on every
  host including browser WASM — verified in ADR 0020's research, where the
  browser column for SHA-2-256/384/512 is supported.
- **Analyzer rule.** None.
- **Open questions owned.** None. The specification's §10 already fixes the two
  tests this decision has to pass: any single-byte change to a header breaks
  verification, and two continuations of one prefix are reported as divergent.
