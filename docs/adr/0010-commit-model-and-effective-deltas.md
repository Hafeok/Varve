# 0010 — Commit model and effective deltas

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
I2, I4, and T1 steps 3 and 4.

> **A note on format, for this ADR set only.** ADR 0001 fixes five sections.
> Milestone 2 requires four further statements from every ADR in the set —
> what it was checked against, which layer owns each contract, whether it
> implies an analyzer rule, and which of the specification's open questions it
> owns. They are none of the five, so they go in a **Checks** section at the
> end. Uniform across 0010–0020.

## Context

`docs/brief.md` lists as a tension to be resolved by ADR rather than by
accident: "Whether retraction of a non-existent quad is an event, a no-op, or an
error; same for re-assertion."

It is a smaller question than it looks and a larger one than it sounds. Smaller,
because SPARQL 1.1 Update already answers it for the request: §3.1.1 and §3.1.2
say that inserting a triple already present and deleting one not present are
both *without effect*. Larger, because in an event-sourced store the request is
not the only thing that could be recorded — the log could record what was asked
for, or what actually changed, and those are different logs with different
properties.

## Decision

**The log records the effective delta against the pinned state, not the
request.** Formally, for every commit at position `P`:

```
A_P ∩ G_{P-1} = ∅        nothing asserted that was already there
R_P ⊆ G_{P-1}            nothing retracted that was not there
A_P ∩ R_P = ∅            no quad both asserted and retracted
```

Asserting a present quad, retracting an absent quad, and asserting then
retracting the same absent quad within one request all contribute nothing. The
sequencer applies the request's operations in order to an overlay on the pinned
state and takes the **net** result.

**An empty effective delta produces no commit.** The request returns
`NoChange(head)`, provisional term ids are discarded, and nothing reaches the
log or the dictionary. A `Data` commit therefore always has a non-empty delta
(I4). An `Erasure` commit is the one exemption, because what it carries is in
its metadata.

**A rejected or empty request leaves no trace.** Not a commit, not a dictionary
allocation, not a gap in the position sequence.

### Why this is not merely tidiness

The effective-delta invariant is what makes `Overlay(B, (A, R)) = (B \ R) ∪ A`
*exact* rather than approximate, and three things depend on that exactness: as-of
reads (R2), diff (R3), and the overlay a pre-commit validator sees (T1 step 5).
Without I2 the delta monoid still composes, but the overlay would have to answer
"was this quad already there" at scan time, which is the cost the invariant
exists to pay once, at write time, where the answer is known.

## Alternatives considered

- **Record the request verbatim — an intent log.** Genuinely attractive: it
  preserves who asked for what, including the no-ops, which is real audit
  information. Rejected because every reader then has to normalise. `Diff`
  stops being computable from the log alone, the overlay stops being exact,
  and every projection has to re-derive whether a delivered assertion actually
  changed anything. It also makes the log's meaning depend on the state it was
  applied to, which is exactly what an event-sourced store is supposed to avoid.
  If the intent is ever wanted, it is an audit concern in a layer above the
  store, recorded beside the commit rather than inside it.
- **Error on retracting an absent quad, or on re-asserting a present one.**
  Matches the intuition of a typed API and catches some caller bugs. Rejected:
  it contradicts SPARQL 1.1 Update §3.1.1–§3.1.2, so the store and SPARQL Update
  would disagree about the same operation; and it makes idempotent scripts
  impossible to write, which is the normal way people load data twice by
  accident and expect to get away with it.
- **Record no-ops as commits with an empty delta.** Would give a heartbeat and a
  place to hang metadata for "someone tried this". Rejected by I4: a log whose
  positions are dense and whose commits all changed something is a log where a
  position means something. If a marker is ever needed, it is a commit *kind*,
  the way `Erasure` is, not an empty `Data` commit.
- **Normalise lazily, at read time.** Keeps writes cheap. Rejected: it moves the
  cost from once per commit to every read of every position forever, and it
  makes `I2` an aspiration that nothing checks.

## Consequences

**Normalisation costs an index lookup per quad against the pinned state.** For an
ordinary commit this is nothing. For a bulk load into a populated dataset it is
the dominant cost, and it is the direct source of **Q2**, which ADR 0013 owns.
Loading into an empty dataset is trivial because the pinned state is empty.

**The store cannot tell you what was asked for.** "I ran this update and nothing
happened" is answerable — `NoChange` says so to the caller — but it leaves no
record. Anyone who needs the attempt recorded has to record it themselves,
above the store.

**A position always means a change.** This is what lets `Diff(P₁, P₂)` be the
composition of the deltas between them with no state lookup, and what lets a
subscriber treat every delivered commit as worth looking at.

**Two commits that ask for the same thing may produce different deltas**,
because the pinned state differs. That is correct and is the point, but it means
a commit's delta is not a function of the request alone, and anything that
replays requests rather than commits will diverge. Replay is over commits.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0009). Touches
  **0005**: the transaction contract described here is the write half of what
  0005 says `Varve.Store` exposes, and nothing in it names SPARQL — the
  reference to SPARQL 1.1 Update above is to its *semantics* for agreement, not
  a dependency. No conflict with any.
- **Layer ownership.** The transaction contract — an ordered list of assert and
  retract operations over terms, metadata, an optional expected position — is
  owned by `Varve.Store` at **layer 4**, per 0003 and 0005. It is expressed over
  `Varve.Rdf` terms (layer 1) and nothing higher.
- **Analyzer rule.** None. The invariants here are runtime properties of a
  computation, not statically checkable shapes; the specification's property
  tests for I2 and I3 are where they are enforced.
- **Open questions owned.** None directly. Q2 arises from this decision but is
  owned by 0013, which decides bulk load.
