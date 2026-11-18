# 0195 — The inference projection

## Status

**Filed unaccepted by Inference, part 2 (#171, under #95), 2026-11-06**
(ADR 0066). Decided by the maintainer on the plan in #95 (2026-10-27),
question 4a, and on #171's plan comment (2026-11-06), which verified this
ADR's two premises and fixed its erasure precondition. Acceptance is the
maintainer's act on the pull request.

**Revised 2026-11-16**, before acceptance, on the maintainer's answers to
#171's status comment (comment 6916253220): closed rows held under a ceiling
in bytes (point 5), the closure's memory limit and `inference-unavailable`
(point 6), `varve:explicit` among the surfaces of the seam (point 7), and the
inconsistency on readiness (point 6). The changes follow ADRs 0238, 0240,
0242 and 0244, which main gained after this ADR was first filed.

Amends **0119** (`inference-behind`, `inference-unavailable`), by a dated
amendment there.

## Context

Tier 2 of #95 serves the closure of a binding's graphs under the `rdfs` or
`owl2rl` profile: every quad the rules entail, materialised, so that a query
under `owl2-rl` or over `varve:implicit` (ADR 0198) is an index read, not a
derivation. The brief says where such state belongs: "the log is the source of
truth and every index is a projection of it". ADR 0174 built the first
projection whose output is computed from more than the delta, the validation
report, and fixed the rulings this ADR shares: rows with validity intervals,
generations published whole, the local cache only, the floor at the head on a
cold start, lag reported and never unready.

**Two premises, verified before any row was built** (#171, 2026-11-06):

1. **The term dictionary is append-only.** `D` restricted to canonical ids is
   injective (spec §1), ids are counter-allocated by the sequencer (ADR 0012),
   and "an entry, once allocated, is in `D` at every later position with the
   same term" (spec §8, ADR 0042 amended 2026-10-07). `TermDictionary` states
   it in the code: a canonical id is never reused and never changes its term.
   So a store handle means one term at every position at or above the one that
   allocated it, and an inline id means its value everywhere. **Rows can be
   over store handles**, with no dictionary of their own.
2. **Erasure keeps handles stable, but not the closure.** Erasure appends a
   commit and destroys a key, and changes no byte of the log (spec §9, I10).
   No handle changes. But equality of private terms is by value (spec §6):
   private ids are not interned, a readable private term equals any term of
   its value, and a shredded one equals only itself. So:
   - a rule joining two private terms of one value must fire, and a join by id
     misses it;
   - after a key is destroyed, a conclusion drawn through such a join loses its
     support **at every position at once**, whether or not it names a private
     term itself.

   Erasure mode is not built: the log format refuses a private term and the
   erasure-mode setting, and milestone 9 brings both. Equality by id is exact
   while it is off.

## Decision

### 1. One projection per materialising binding

`InferenceProjection`, in `Varve.Inference.Store` (layer 5, ADR 0191), is an
`IProjection` (spec §7, ADR 0016) over one TBox binding whose profile is
`varve:RDFS` or `varve:OWL2RL` (ADR 0194, amended by ADR 0197). It is fed the
dataset's closed commits in order, by `CatchUpAsync` or a subscription, as ADR
0174 point 1's projection is. It is rebuildable from the log, holds its own
position, and writes nothing to the log.

### 2. Rows

- **A row is one implicit quad**, entailed and not explicit, with a validity
  interval `[asserted, retracted)` in ADR 0138's style. **The closure at p is
  the explicit quads at p together with the rows valid at p.**
- **A quad retracted but still entailed** opens a row at its retraction. A
  quad asserted that was implicit closes its row at the assertion.
- **Rows are over store handles**, by premise 1. The rules invent no terms:
  RDFS and OWL 2 RL/RDF have no existentials, and every constant they name is
  a vocabulary term, a TBox term or a rules-graph term. A vocabulary constant
  the dictionary lacks, such as `rdfs:Resource`, is staged once per read
  through the staging view (ADR 0058), never allocated.
- **A row's handles exist at its interval.** A row is valid as of p only at or
  above the allocation of every handle it names, which holds by construction:
  a conclusion cannot be derived before its premises exist. The as-of property
  (point 9) checks it.
- **Generalised triples** (a literal subject, from RDFS's `rdfs` and D's
  datatype rules) are held for the rules' joins and never served (SPARQL 1.1
  Entailment Regimes §3.3).
- **Indices.** SPO, POS and OSP over handles, in memory, for the rules' joins.
- **What is held is bounded by the data and by configuration, never by the
  commits** (ADR 0240 point 1). The open rows and the indices are the closure
  at the projection's position, bounded by the data. Closed rows are history,
  and are held only up to a ceiling (point 5).

### 3. Placement

Each row sits in the graph ADR 0190 places it in: an intra-graph conclusion in
its data graph (point 2), a conclusion drawn only from the TBox and rules
graphs in `varve:inferred/{enc(binding)}/tbox`, and a cross-graph conclusion
in the premise-set graph of each of its minimal premise sets (point 6). The
projection keeps the table from each premise-set graph to its set. ADR 0198
names the graphs and refuses writing them.

**Cross-graph growth is measured before it is stored differently.** The number
of distinct premise sets, and so of reserved graphs, is measured on a REC M
cross-graph binding over per-building graphs and reported per binding on
readiness (`premiseSets`). If it grows faster than linearly in the bound
graphs, this ADR's amendment says so before any other layout is chosen.

- **How it is computed** (revised 2026-11-16). A cross-graph binding holds
  one closure, of its data graphs' merge over the TBox closure, and each
  fact's minimal premise sets over it, as pairs of a fact and a set kept
  under absorption (ADR 0190 point 6). A data commit updates the merge
  incrementally and recomputes the sets over the whole closure, diffing
  them with the sets before to find the rows that moved: the cost of a
  commit is the closure's, not the delta's. Explaining a row closes only
  the graphs of its set, so no proof cites a graph outside it.
- **What REC gives** (measured 2026-11-16, `Varve.Benchmarks
  --inference-premise-sets S <profile>`): no cross-graph premise set on
  REC S under `rdfs` or `owl2rl`, its buildings sharing nothing but the
  TBox; 37 to 41 of 0.9 million facts have more than one set, each of one
  graph. REC M gives none either, closing 7, 15, 31 and all 63 of its
  buildings' graphs under `rdfs` (17.4 million facts at 63), so it cannot
  answer the growth question.
- **Cross-graph premise sets are unmeasured** (revised 2026-11-18), until
  REC M with links between buildings has run (ADR 0141, amended
  2026-11-18): `--inference-premise-sets M <profile> links=<per mille>`, at
  20 and 200 under both profiles, in the nightly's `premise-sets` job or by
  hand. It never gates. Run by hand on REC S at 20 and 200, under both
  profiles, it gives no cross-graph set: REC 4.1 and its Brick state no
  restriction, key, functional, transitive or chain property, and every
  two-premise RDFS rule has a schema premise, so no rule joins two data
  facts of different graphs. Links only raise the facts with more than one
  set, each of one graph (49 at 20; 169 under `rdfs` and 409 under `owl2rl`
  at 200). Whether premise sets grow faster than linearly in the graphs
  needs a TBox or data that joins them; that is put to the maintainer on
  #171.
- **What recomputing costs.** The sets over REC M's 63 graphs take 236 s,
  after a 90 s closure, and about 50 bytes a fact beside it (4,378 MiB held
  in all). Recomputed on every data commit, that would be minutes a commit,
  so they are maintained from the delta instead, as the closure is
  (revised 2026-11-16): `PremiseSets.Reach` and `Update`, delete and
  derive again over the sets of the facts a retraction can reach.
- **What maintaining costs** (`Varve.Benchmarks --inference-premise-sets
  M rdfs 50`, the last 50 commits of REC M, one cross-graph merge of its
  63 graphs). It builds in 411 s, 17.4 million facts, a peak working set
  of 7.6 GB. A commit's median is 3.4 ms and its worst 2.8 s (a merge of
  rooms): over all 50, the merge's closure 87 ms, the reach 18 s, the sets
  2.2 s, 658 facts' sets changing. A set holding no retracted graph stands;
  one holding one is confirmed by a proof search before any fact is
  derived again from all its instances, which a fact many graphs derive,
  `p rdf:type rdf:Property`, would otherwise cost every use of `p`: 2,008
  s over the same commits before. The reach is the larger part now
  (#334).
  Under `owl2rl` (`… M owl2rl 50`): 23.0 million facts, built in 1,107 s,
  a peak of 7.6 GB; a commit's median 19 ms and its worst 1.3 s (a move);
  over all 50, the merge's closure 144 ms, the reach 22 s, the sets 115
  ms, 922 facts' sets changing.

### 4. Commits and generations

- **A data commit** is applied by the retraction strategy ADR 0196 chooses,
  insertion first by semi-naive propagation, and the rows change only where
  the closure does.
- **The TBox closure is shared.** A data graph's closure is its own quads'
  over the TBox closure, whose facts it holds as given, never twice. Each
  data graph holding its own copy cost, on REC S, as many facts again as the
  TBox closure for every graph (revised 2026-11-16, on the measurement
  below).
- **A generation** starts on a commit that changes a bound TBox graph or the
  rules graph, on the first commit after a rules graph that did not load, on
  a commit that allocates a term a rule names, and on any commit whose
  incremental pass fails. A generation rebuilds the TBox closure and every
  data graph's over it from the commit's state, and opens or closes only
  the rows that differ.
  - A TBox change is a generation because the TBox closure is shared beneath
    every data graph's and never changes under one. TBox commits are rare
    beside data commits; their cost is a from-scratch closure (point 5a).
  - A rules change that only adds rules without negation is semi-naive from the
    current closure. Any other rules change is from scratch.
  - Either way the guarantee is point 9's: every position equals the
    from-scratch closure.
- **Published whole.** The projection's position moves to a commit only once
  the commit is applied, generation included. A reader never sees a mixed
  closure.

### 5. Storage: the local cache only

- **One blob per binding**, `inference/{enc(binding)}/state-{position}`,
  written with `Dataset.WriteCheckedAsync` and read with
  `Dataset.TryReadCheckedAsync`, ADR 0213's checked envelope (derived kind 9,
  version 6): every 64 KiB block checked, the whole bound to the dataset, the
  position, and the log's header hash there. Older states are deleted.
- **A state identifies its binding**: its IRI, TBox graphs, rules graph,
  profile, graph set, the cross-graph and `sameAs` flags, and a format version.
  It is used only when every one matches and its position is at or below the
  head.
- **A miss is a rebuild.** A null from the read, a state of another binding,
  or a decoder's exception over verified bytes is a miss, and the projection
  starts as on a cold start. A damaged entry is never a wrong entailment.
- **The server** passes a file dataset's own `derived/`. A memory or
  object-storage dataset keeps the projection in memory, as ADR 0174 point 7
  rules.
- **A cold start begins at the head.** A projection with no usable state is
  reset to the head (`IProjection.ResetAsync`): one from-scratch closure there,
  which is its floor, and it follows the log from then on.
- **Below the floor** nothing is held. A closure read as of a position below
  it is the binding's from-scratch closure as of that position, computed when
  read, on every instance and after every restart, under the read's memory
  limit (`memory-limit-exceeded`). The operator guide says so.
- **The state holds the open rows and the closed rows in memory**, nothing
  older: no blob format for history. Durable history through the extent
  machinery is #313, after 1.0.

### 5a. Closed rows under a ceiling in bytes

- **Closed rows are kept in memory up to `InferenceOptions.HistoryBytes`**,
  32 MiB a binding by default, as ADR 0240's seek directory cache is; zero
  keeps none. Past it, the oldest are dropped first, **whole positions at a
  time**, so what is held is always a contiguous window of positions
  `[heldFrom, position]` ending at the projection's position.
- **An as-of read below `heldFrom` is from scratch**, exactly as below the
  floor: the binding's closure as of that position, computed when read, under
  the read's memory limit. A read holds the rows it was given until it ends;
  dropping never takes them from under it.
- **The server's setting** is `Varve:Inference:HistoryBytes`. The gauge
  `varve.inference.history.bytes`, per binding, reports what is held, and
  readiness reports `heldFrom`.
- **What a from-scratch closure costs** is measured on REC M and stated here
  before this ADR is accepted (the benchmark of point 9's list): the time to
  close the dataset at the head under `rdfs` and under `owl2rl`, and its peak
  memory. It is what an old as-of read and a cold start pay. Measured
  2026-11-16 on REC M at its head (63 graphs, 5,053,193 quads; 4 cores,
  .NET 10.0.15; `Varve.Benchmarks --inference-closure M <profile>`):

  | Profile | Time | Facts held | Heap held | A fact | A data quad | Working set |
  |---|---|---|---|---|---|---|
  | `rdfs` | 63.0 s | 17,474,726 | 3,458 MiB | 208 B | 718 B | 5,802 MiB |
  | `owl2rl` | 274.3 s | 23,132,785 | 3,638 MiB | 165 B | 755 B | 6,470 MiB |

  The heap is what the closures hold after a compacting collection; the
  working set, the process's at the end, is the nearer to its peak. A
  closure first held each fact six times and counting's state beside it,
  about 500 bytes a fact: REC M under `rdfs` did not close in 15 GB. Each
  fact is now one record carrying counting's round and count and its links
  in the five indices (revised 2026-11-16).

### 6. Lag, `inference-behind`, and the memory limit

- **Readiness** reports, per binding, under its dataset's line as
  `inference`:
  - its lag, head minus projection position;
  - `heldFrom` (point 5a);
  - `premiseSets` for a cross-graph binding;
  - `inconsistent` and `clashes`, the count of clashes at its position
    (ADR 0197 point 5);
  - its state: `following`, `regenerating after erasure` (point 8), or
    `stopped (memory-limit)`, with `limit` and `reached`.

  None of these ever makes the dataset unready or "behind".
- **A read that needs the closure at a position the projection has not
  reached** waits two seconds, then answers `503 inference-behind`, with
  members `binding`, `position` and `projectedPosition`, and `Retry-After: 1`
  (ADR 0119, amended). A query that streams has its cut after the first byte
  as a `Varve-Error` trailer (ADR 0095).
- **A term never reads as absent while the closure is behind.** A term lookup
  that misses while a closure the read needs is behind yields a staged handle,
  as `ReportGraphs.TryInternalise` does since #186; any match through it then
  waits or answers `inference-behind`. The rule is ADR 0022's, as amended on
  2026-10-30 (`DenialCoversQuadsNotYetLoaded`), and every source this part
  adds is held to `tests/QuadSourceContract.cs`.

**The closure's memory is limited, and a binding over the limit stops.**
- `Varve:Inference:ClosureBytes` bounds what one binding's closure may hold:
  its facts, indices and open rows, by the projection's own accounting at a
  stated cost per quad. Unset, it is a quarter of the collector's heap limit
  (`GCMemoryInfo.TotalAvailableMemoryBytes`), the share ADR 0244 point 5
  gives the memtable's worst case. A value the operator sets is kept as set.
- **A materialisation that would pass it stops**, a cold start's or a
  generation's or a commit's, and serves no partial closure. Spilling the
  closure to disk is not in this part.
- **Every tier-2 read of a stopped binding answers `503
  inference-unavailable` at once**, with no wait and **no `Retry-After`**:
  members `binding`, `reason` `memory-limit`, `failure` `configuration` (ADR
  0242 ruling 2's class: the host's configuration cannot run it, and no retry
  helps), `limit` and `reached` in bytes, `reached` a lower bound on what the
  closure needs. The `detail` names the setting and says to raise it or the
  instance's memory and restart. Tier-1 regimes on the same graphs are still
  served.
- **It is tried again** at a restart, where the setting is read, and on a
  commit that starts a generation, under the same limit; never on a timer.
- **`docs/operator/small-machine.md` states the closure's bytes per quad**
  under each profile, from the benchmark, beside ADR 0244's figures, so that
  an operator can size `ClosureBytes` for a binding before applying it.
- **An idle server returns what a generation's work left**, as it does after a
  checkpoint (ADR 0244 point 6): the host's, not the projection's.

### 7. One point serves every tier-2 surface

The host resolves the closure for a read through one seam,
`IInferenceClosures` in `Varve.Protocol` (the twin of `IValidationReports`),
which `Varve.Protocol` defines and the server implements over the
projections. Every tier-2 surface goes through it:
- the views `varve:explicit` and `varve:implicit`, and the reserved graphs
  (ADR 0198);
- `Varve-Entailment: owl2-rl` where the closure serves it (ADR 0197);
- explain (ADR 0199).

**A refusal of tier 2 sits there and nowhere else**, in this order:
1. the row-level refusal, `403 entailment-not-available` (ADR 0238, amended
   2026-11-16 to carry no `binding`), which #14 adds at this seam;
2. a binding stopped by its memory limit, `503 inference-unavailable`
   (point 6);
3. point 8's refusal after erasure;
4. a projection behind the read's position, `503 inference-behind` after the
   wait (point 6).

So a caller who may not see the closure learns nothing of the binding's
state.

### 8. Erasure: equality by id, and the precondition milestone 9 must meet

- **Rows are over store handles and joins are by id.** That is exact only
  while erasure mode is off, which is every dataset until milestone 9.
- **Before erasure mode and a materialising binding can coexist**, milestone 9
  must meet this precondition:
  1. **Joins by value.** The rules join through the source's equality, every
     handle hashed by value, as spec §6 requires of a source whose dictionary
     holds private entries.
  2. **An `Erasure` commit starts a whole generation from scratch.** It
     invalidates every saved state and resets the floor to the commit. A purge
     of the rows that name a private term is not enough, since a conclusion
     that names none can lose its support (premise 2).
  3. **The old generation stops being served at once.** From the `Erasure`
     commit until the new generation reaches the head, every tier-2 surface is
     unavailable: the views, `varve:inferred/…`, `owl2-rl` where the closure
     serves it, and explain. The old generation's conclusions include data
     derived from the shredded value. The refusal sits at point 7's seam, and
     readiness says why (`inference: regenerating after erasure`).
  4. **The as-of property is restated under the equality after erasure.** I10
     changes the readability of private terms at every position at once, and
     with it the closure at every position: below the floor and above it, a
     closure read after the erasure is the closure under the new equality.
- **Open question, owned by milestone 9: meet it or refuse.** Milestone 9's
  erasure ADR either meets the precondition, or refuses erasure mode on a
  dataset with a materialising binding, and a materialising binding on a
  dataset in erasure mode. The maintainer leans towards refusing in the
  preview that brings erasure mode, and meeting it later.

### 9. The properties

- **Incremental equals from scratch at every position**, byte for byte in
  canonical N-Quads (RDFC-1.0, ADR 0059), over generated histories with
  retractions, TBox changes and rules changes, for each retraction candidate
  (ADR 0196). 40 histories × 60 commits on a pull request, 400 × 200 nightly.
- **As-of reads of the closure equal the from-scratch closure of the log at
  that position**, above and below the floor and below `heldFrom`, with the
  history ceiling at zero, at a few hundred bytes and at its default; and what
  is held never passes the ceiling.
- **A binding over its memory limit** answers `inference-unavailable` on every
  tier-2 surface and serves no partial closure, with the limit set below the
  closure's size in the tests.
- **ADR 0190's grant property**, over the tier-2 surfaces: 200 cases on a pull
  request, 5,000 nightly.
- **The cache-damage gate.** A byte zeroed, flipped or cut at any offset of a
  saved state yields a miss and the from-scratch closure, never a wrong
  entailment: one offset in 16 on a pull request, every offset nightly.
- **Behind is `inference-behind` or the answer.** With the projection held
  back at random, every read under a closure regime or view returns
  `503 inference-behind` or the from-scratch closure's answer at its position
  (#186's property, extended).

## Alternatives considered

- **Materialise into the log.** Rejected by the brief: the log holds asserted
  quads only, and an entailment is not an assertion.
- **A dictionary of the projection's own.** Needed only if handles were not
  stable across positions. Premise 1 shows they are, and a second dictionary
  would cost a translation per term on every read.
- **Purge on erasure** (the plan's first wording). Insufficient, by premise 2.
- **Share the closure as writer-written extents.** Would spare each instance
  its cold-start rebuild. A later decision, made on the cold-start benchmark
  at REC M and L if a restart there costs more than an operator accepts.
- **Replay the history on a cold start.** Each step is an as-of read whose cost
  grows with its distance from a checkpoint, so replay grows with the square
  of the history (ADR 0174 point 7).

## Consequences

- **A query under `owl2-rl` or over `varve:implicit` reads the rows**, at an
  index read's cost, once the projection has reached its position.
- **Every instance rebuilds on a cold start.** The benchmarks report the
  rebuild at REC M and L, closure size and materialisation time, and
  maintenance per commit.
- **Memory.** The closure's indices are in memory, per binding. The
  benchmarks report it with the closure's size.
- **A read below the floor** costs a from-scratch closure as of its position.

## Checks

- **Checked against the accepted ADRs** (0001–0194, 0206, 0213–0217, 0224,
  0225, 0239–0241, 0243), and against 0230–0238 (row-level access, filed
  unaccepted) and 0242 and 0244 (accepted on their pull requests). Touches:
  - **0012** and **0042**: the handles the rows are over;
  - **0016** and **0174**: the projection's contract and the rulings it shares;
  - **0019**: erasure, by point 8's precondition, owned by milestone 9;
  - **0022**: no term denied while the closure is behind;
  - **0058**: vocabulary constants staged per read;
  - **0095**, **0119**: `inference-behind`, amended in 0119;
  - **0138**: validity intervals;
  - **0190**: placement;
  - **0213**: the checked envelope;
  - **0238**: the row-level refusal at the seam, first in order;
  - **0240**: nothing held grows with the commits; closed rows under a
    ceiling, as its seek directories are;
  - **0242**: `inference-unavailable` classed by its ruling 2, and every
    problem page with its `## What to do`;
  - **0244**: the closure's limit follows the heap limit, and the idle
    release after a generation.

  No conflict.
- **Layer ownership.** `Varve.Inference.Store` (5): the projection and its
  state. `Varve.Inference` (4): the closure it maintains. `Varve.Protocol`
  (5): the seam `IInferenceClosures`, with no reference to
  `Varve.Inference.Store`.
- **Analyzer rule.** None new. Each rule firing, each changed quad and each
  row's match is `[HotPath]` (`VARVE0003`).
- **Open questions owned.** Point 8's "meet it or refuse", by milestone 9.
