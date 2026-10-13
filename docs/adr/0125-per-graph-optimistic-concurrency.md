# 0125 — Per-graph optimistic concurrency and the graph-version index

## Status

**Accepted — filed unaccepted by milestone Commit path of #91, 2026-10-13**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the Commit path plan: Settings and Erasure commits touch
every graph; what a request asked for is recorded, not what it found; the
index's size is measured and stated here.

**Amends** ADR [0011](0011-concurrency-single-sequencer.md) (the expected
position gains a scope) and ADR
[0057](0057-sparql-update-one-request-one-commit.md) (a request's scope is
what it read and wrote), each by a dated block of its own (ADR 0068).

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
version 1.7: T1 step 1, §7 (the index is part of the default projection).

## Context

ADR 0011's expected position is a statement about the whole dataset: "I read
at `p`, and nothing has changed since." Any commit after `p` makes it false,
whatever that commit touched. A dataset with one graph per entity, per
tenant or per document has writers that never read each other's data and
still conflict on every concurrent write. ADR 0057's amendment removed the
check where a request read nothing; a request that reads one graph and
writes it still conflicts with every writer of every other graph.

The check can be narrower without being weaker. Normalisation (T1 step 3)
reads only the quads a request's operations name, and a pattern's evaluation
reads only the graphs it matches. If the commits after `p` touched none of
those graphs, the request's reads and its normalisation see what they saw
at `p`.

## Decision

1. **The check, generalised.** A request may carry, with its expected
   position `p`, a **scope** `S`: a set of graphs. T1 step 1 becomes: *if
   some commit in `(p, head]` touched a graph in `S`, reject with
   `Conflict(head)`.* Without a scope, `S` is every graph, and the check is
   exactly ADR 0011's: `p ≠ head` conflicts. A `p` above the head conflicts,
   as it does today.
2. **What "touched" means.** A `Data` commit touches the graph of every quad
   in its effective delta, asserted or retracted. **A `Settings` or `Erasure`
   commit touches every graph**: settings change what every later write
   means, and an erasure retracts across graphs by key; it is also what keeps
   "`S` = every graph" exactly today's check, since every commit then touches
   some graph in it (a `Data` commit is non-empty, I4).
3. **The property it buys** (serialisability per scope). If no commit in
   `(p, head]` touched `S`, and the request read and wrote only graphs in
   `S`, its effective delta at `head` equals its effective delta at `p`: the
   quads it reads and the quads its normalisation consults are the same in
   `G_p` and `G_head`. A request whose reads are wider than its declared
   scope gets no such guarantee, which is why the scope is the caller's
   statement or is inferred from what it read (ADR 0057, amended).
4. **The graph-version index.** The version of a graph `g` at position `P` is
   the position of the last commit at or before `P` that touched it, and 0
   if none did. The store keeps it for the head:
   - a map from graph id to version, plus one *all-graphs* version (the last
     `Settings` or `Erasure` commit), so that `version(g) = max(map[g],
     all)`;
   - **maintained by the default projection**, in the same step that applies
     a commit to the runs, from the commit's delta; deriving the touched
     graphs from a commit's quads allocates per graph, never per quad
     (`[HotPath]`);
   - **persisted in `derived/`** as `index/graphs`, written with the
     projection's state and naming the same position and header hash, and
     beside each checkpoint as `checkpoints/<position>.graphs`; it is derived
     data, rebuilt from the log when absent, stale, or naming another log,
     like everything else in `derived/` (I8);
   - **consulted by the sequencer in the same step as today's check**: a
     scope's check is one lookup per graph in it.
5. **A graph's version at an earlier position** (an as-of read, ADR 0126) is
   served like R2: the nearest checkpoint's graph map at or below the
   position, overlaid with the graphs the commits after it touched. Its cost
   is the log distance, the bound of ADR 0114 point 3 applies, and a
   checkpoint written before this milestone, which carries no map, is
   treated as absent for this purpose.
6. **The conflict's evidence.** `Dataset.ConflictsAsync(p, scope, limit,
   maxDistance)` lists the commits in `(p, head]` that touched the scope,
   newest first, each with the graphs of the scope it touched: at most
   `limit` of them, reading at most `maxDistance` commits back from the head,
   and saying when it stopped short. It runs outside the sequencer, after the
   `Conflict` is answered, and reads the log as a subscription would.
7. **Resource-level concurrency is rejected, for now.** The unit is a graph.
   A version per resource (per subject) would need a resource-version index
   the size of the dataset, maintained on every quad of every commit;
   graphs are the unit the store, the grants (ADR 0107) and the protocol
   already share.

### The index's size

Measured at filing on the machine the milestone's benchmarks name: the
memory of the head's map and the time to open a dataset whose `index/graphs`
holds it, at 10⁵ and 10⁶ graphs. **The numbers are entered here by the
commit that adds the measurement, before the pull request leaves draft**; a
dataset with a graph per entity at a million entities is the case to know
the cost of, and no different structure is adopted before that cost is
known.

## Alternatives considered

- **The scope as the declared graph scope in a commit's metadata** (spec §1,
  the named-graph scope). It is a declaration that the store never enforces,
  recorded in the log; conflating it with a concurrency precondition would
  give a logged field a second meaning.
- **Only `Data` commits touch graphs.** Narrower, and it makes "`S` = every
  graph" weaker than today's check (a settings change between `p` and the
  head would no longer conflict). Rejected on the maintainer's decision.
- **Version every graph by a counter of its own** rather than by position.
  Gives small tags, and loses what a position says: that the graph's
  content as of any later position up to the next touch is the content at
  that position, which is what makes an as-of tag (ADR 0126) a strong one.
- **Rebuild the map from the runs.** The runs do not keep positions per quad
  once merged, so it cannot be done; the log is the only source.

## Consequences

- Writers of disjoint graphs never conflict; writers of a shared graph
  conflict exactly as before.
- **`derived/` gains two blob kinds.** No byte of `log/` and no existing
  `derived/` kind changes; the derived format stays 3, and a store before
  this milestone ignores the new blobs.
- **Opening a dataset reads the map**, whose size is the number of graphs
  ever touched. The section above states it.
- **A conflict can say why**, without scanning the log unboundedly.

## Checks

- **Checked against the accepted ADRs** (0001–0124) and specification 1.6.
  Touches **0010** (normalisation reads only the request's quads), **0011**
  (amended), **0016** (the index is part of the default projection, rebuilt
  and persisted under its contract), **0041** and **0070** (a derived blob
  beside the runs), **0057** (amended), **0107** (graphs are the unit of
  grants too), **0114** (the as-of bound), **0124** (the check runs against
  the staged state of a batch). No conflict.
- **Layer ownership.** `Varve.Store`, layer 4.
- **Analyzer rule.** None new; the touched-graph derivation is `[HotPath]`.
- **Open questions owned.** None.
