# 0013 — Records versus commits, and bulk load as a multi-record commit

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§1 (*Record*), and the durability step of T1.

## Context

`docs/brief.md`: "Bulk load: a multi-billion-quad import cannot be one in-memory
transaction, yet must be one logical commit or a well-defined sequence."

The two halves of that sentence pull against each other only if the unit of
*append* and the unit of *meaning* are the same thing. They need not be. A log
can append in pieces and mean in wholes, provided the wholeness is recorded in
the log itself rather than in something beside it.

This matters more here than in most stores because of §2: the dataset directory
is copied, backed up, synchronised and checked into version control by tools
that know nothing about Varve. Any scheme where "is this commit finished" lives
outside the log — a lock file, a pending marker, a database of in-flight
transactions — produces a copy that is silently wrong.

## Decision

**A record is the physical unit of append. A commit is one or more records, and
the last one carries a closing flag.**

- The **readable head** is the position of the last *closed* commit.
- Records of an unclosed commit are invisible to every read, every subscription
  and every projection. Not filtered out at the edges — invisible, because the
  readable head has not moved.
- On recovery, an unclosed tail is **discarded**. It is not replayed, not
  repaired and not reported as data.

Atomicity therefore comes from the closing flag and not from buffering. A commit
of any size uses bounded memory in the writer, and a bulk load is one logical
commit made of as many records as it needs.

**The closing flag is in the log.** A copy of `log/` made at any moment — mid-load
included — is a valid log whose readable head is the last closed commit. Nothing
outside the directory is needed to interpret it, and no copy is silently wrong.

**Durability is ordered.** Every record of a commit is durable before the closing
record is durable. The closing record's durability *is* the commit point; before
it, a crash loses the whole commit, and after it, the commit is complete.

## Alternatives considered

- **One commit, one record — buffer the whole delta and append it atomically.**
  Simplest possible rule, and it is what most of the specification's other
  invariants would prefer. Rejected by the brief's own constraint: a
  multi-billion-quad import does not fit in memory, and an import that cannot be
  one commit is an import that shows up as millions of positions.
- **Bulk load as a sequence of ordinary small commits.** No new concept at all,
  and resumable for free. Rejected on two grounds. A reader at any moment during
  the load sees a *partially loaded dataset* and cannot tell that from a finished
  one — there is no position at which the load is a thing that happened. And it
  spends a position per batch, so `Diff` and as-of reads across a load become
  millions of steps for one logical act. If a caller genuinely wants a resumable
  sequence, it can still write one; what it cannot do is call that a commit.
- **A pending-transaction marker outside the log** — a lock file, a sidecar, a
  manifest. Standard, and it makes recovery easy. Rejected by §2 outright: a copy
  of the directory that includes the data and not the marker, or the marker and
  not the data, is a dataset that reads as complete and is not. The flag has to
  be in the bytes that get copied, in order, or it does not hold.
- **Stage into a temporary area and rename on completion.** Gives atomicity from
  the filesystem. Rejected: rename is not atomic on every backend we have to
  support — the browser is the obvious one — and it doubles the write volume for
  a load that is already the largest thing the store does.
- **Let a projection see uncommitted records and roll back.** Would make a bulk
  load visible as it progresses, which some users want. Rejected: it makes every
  projection implement compensation, and I8's rebuild equivalence would then have
  to hold across rollbacks too. The cost lands on every projection for the
  benefit of one workload.

## Consequences

**A crash during a bulk load loses the load.** The tail is discarded and the
dataset is exactly as it was. That is the correct behaviour for atomicity and it
is genuinely expensive for a multi-hour import, which is the substance of **Q2**
and **Q3** below. Resumption, if it is ever offered, is a way of *rebuilding* the
same commit cheaply, not a way of leaving it half-open.

**Records must be independently discardable.** A record cannot depend on
something outside the log having been updated when it was written — no
"allocate then reference" across the record boundary that survives the discard.
This constrains the storage abstraction (ADR 0018) and is one of §2's
requirements on it.

**The distinction leaks into the wire.** A subscriber receives *commits*, never
records, and a replica ships records but advances its readable head only on a
close. Anything that speaks the log has to know both words, which is why
`CLAUDE.md` now lists commit-versus-record as vocabulary that must not drift.

**Positions stay meaningful.** One import is one position. This is what keeps
as-of reads and diff useful across a load, and it is the main thing the
alternative of many small commits would have cost.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0012). Touches
  **0010** (I4's non-emptiness is per commit, not per record), **0011** (the
  sequencer holds the commit open across its records, so a bulk load occupies
  the sequencer for its duration — a real cost of the single-sequencer choice),
  and **0016** (a projection receives closed commits only). No conflict with any.
- **Layer ownership.** The commit-versus-record distinction and the closing
  protocol belong to `Varve.Store` at **layer 4**. The *append* and *durability*
  primitives they rest on belong to the storage abstraction, ADR 0018, which is
  also layer 4 as a contract with implementations at layer 5 and below the
  store's own file backend.
- **Analyzer rule.** None.
- **Open questions owned.**
  - **Q2 — bulk load and I2.** Normalising a multi-billion-quad commit needs an
    index lookup per quad against the pinned state (ADR 0010). Into an empty
    dataset this is trivial; into a populated one it needs a stated strategy.
    **Due by milestone 6**, with the bulk loader.
  - **Q3 — bulk load and validators.** The overlay of a multi-record commit does
    not fit in memory, so either validators are disabled for bulk commits or the
    overlay spills to disk. Cross-references **ADR 0017**, which owns the
    validator contract and the overlay. **Due by milestone 6.**
