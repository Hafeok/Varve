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

- **How it is computed** (revised 2026-11-16 and 2026-11-18). A cross-graph
  binding holds one closure, of its data graphs' merge over the TBox
  closure, and each fact's minimal premise sets over it, as pairs of a fact
  and a set kept under absorption (ADR 0190 point 6). A data commit updates
  the merge incrementally and the sets from its delta (below), which answers
  the facts whose sets changed: only their rows may move. Explaining a row
  closes only the graphs of its set, so no proof cites a graph outside it.
- **Under `rdfs` a premise set crosses graphs only between a data graph and
  the schema graph**, never between two data graphs: every two-premise RDFS
  rule has a schema premise, so one data fact meets the TBox, never another
  graph's data. With the TBox in its own graph that set is a data graph's
  own; there is no growth to measure, and it is not measured.
- **Under `owl2rl` it is measured with a benchmark axiom set**, below
  (revised 2026-11-18). REC 4.1 and its Brick
  state no restriction, key, functional, transitive or chain property, so
  REC with links between buildings (ADR 0141, amended 2026-11-18) gives no
  cross-graph set: on REC S at 20 and 200 links per thousand commits, none
  under either profile, the links only raising the facts with more than one
  set, each of one graph (49 at 20; 169 under `rdfs` and 409 under `owl2rl`
  at 200). The axiom set, `tests/Varve.Benchmarks/Axioms/rec-links.ttl`, not
  part of REC as published, makes `rec:feeds` transitive and adds the chain
  `feeds ∘ isLocationOf ⊑ feeds`, so a link from one building into another's
  room reaches what is located there. It is read only by the nightly's
  `premise-sets` job and by hand: `--inference-premise-sets M owl2rl
  links=<per mille> axioms=links`. It never gates.
  
  **Measured with it** (2026-11-18, 4 cores, .NET 10.0.15): closure size
  against live links, cross-graph sets and the facts holding one.

  | REC | Links ‰ | Graphs merged | Live links | Facts | Cross-graph sets | Facts with one | Premise sets took |
  |---|---|---|---|---|---|---|---|
  | S | 20 | 3 | 2 | 925,125 | 0 | 0 | — |
  | S | 200 | 3 | 62 | 926,077 | 1 | 12 | — |
  | S | 500 | 3 | 208 | 928,593 | 3 | 124 | — |
  | S | 1000 | 3 | 367 | 931,273 | 3 | 240 | — |
  | M | 20 | 7 | 26 | 2,591,593 | 0 | 0 | 55 s |
  | M | 20 | 15 | 54 | 5,553,814 | 0 | 0 | 141 s |
  | M | 20 | 31 | 101 | 11,477,643 | 0 | 0 | 383 s |
  | M | 20 | 63 | 184 | 23,026,910 | 0 | 0 | 1,132 s |
  | M | 200 | 7 | 227 | 2,596,047 | 1 | 12 | 160 s |
  | M | 200 | 15 | 462 | 5,562,799 | 3 | 36 | 686 s |
  | M | 200 | 31 | 999 | 11,494,399 | 9 | 108 | 3,239 s |

  The closure grows with the graphs and barely with the links: 184 links
  add nothing a set crosses at 20‰, and at 200‰ each link adds about 20
  facts. The cross-graph sets are few and two graphs wide.

  **The answer to #171's question** is that the premise sets do grow faster
  than the graphs, and the growth is in time, not in sets. Under this axiom
  set at 200‰, finding them takes **about four times as long when the
  graphs double**: 163 s for 7 graphs, 686 s for 15 (×4.2), and 3,239 s for
  31 (×4.7). At 20‰ it is ×2.6 to ×2.7. The merge's own closure grows the
  same way: 49 s, 198 s and 1,216 s. Counted, the work found grows as the
  data does, ×2.2 a doubling at both densities (rule instances, unions,
  (fact, set) pairs; `PremiseSets.Counts`), and almost every fact has one
  set. So the cost is in the search for rule instances, not in the pairs.
  The property chain's derivation enumerates every chain in the fact set
  for each anchor, which is quadratic in the data (#354). Until that is
  fixed, the operator guide limits cross-graph `owl2rl` bindings whose
  chains cross graphs, and the nightly runs 200‰ on at most 15 graphs; REC
  M at 200‰ on 63 graphs is not run.
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
- **What those runs had, needed and peaked at** (revised 2026-11-18). The
  7.6 GB is the peak, not the need, and not a ceiling the run met: it ran
  with no limit set, in a container of 13.35 GiB, where the collector's
  heap limit is three quarters of it, 10.0 GiB (10,256 MiB). Rerun under
  `DOTNET_GCHeapHardLimit`, with the benchmark reading its heap at each
  stage after a compacting collection (the harness's own history and
  bookkeeping, about 1.15 GiB of it, included):

  | Profile | Heap limit | Result | Live heap, merge closed / sets made | Peak working set |
  |---|---|---|---|---|
  | `rdfs` | 10.0 GiB | built | 4,524 / 5,531 MiB | 7,443 MiB |
  | `rdfs` | 9, 8 and 6 GiB | died: `OutOfMemoryException`, exit 134 (#335) | — | — |
  | `owl2rl` | 10.0 GiB | built | 4,731 / 5,738 MiB | 7,004 MiB |
  | `owl2rl` | 9 GiB | built | 4,731 / 5,738 MiB | 6,766 MiB |

  So the need, as a heap limit the build finishes under, is between 9 and
  10 GiB under `rdfs` and at most 9 GiB under `owl2rl`; the live heap,
  what is held once built, is 5.4 and 5.6 GiB. The build's transient is
  near twice the finished closure's heap (#339). The deaths were the
  defect the amendment of 2026-11-18 below fixes: under the budget, the
  same build is refused as `stopped (memory-limit)` instead.

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

## Amendment, 2026-11-18 — a closure too large is refused before the heap is asked

Filed by Inference, part 2 (#171, under #95), unaccepted until the
maintainer accepts it (ADR 0066). Decided by the maintainer on #171 and
#335 on 2026-11-18: #335 blocks this part, and is fixed here.

**What was wrong.** Point 6's budget counted facts at 256 bytes each, while
a from-scratch build needs more than twice that, so a `ClosureBytes` near
the heap limit let a build exhaust the heap first. On REC M, a cross-graph
build under a 6, 8 or 9 GiB heap limit died of an unhandled
`OutOfMemoryException` (exit 134) instead of being refused. `ApplyAsync` also
made any failed pass a generation, a rebuild that needed more memory still,
and the server's feed did not handle the result. Under the old code the gate
below fails: after 120 s the binding still reads `following` with a lag of
11, its feed dead, nothing refused.

It amends point 4 (generations), point 6 (the memory limit) and point 9
(what is tested):

1. **The budget is asked before every allocation.** `FactBudget` counts
   bytes. Every structure a closure grows asks it first:
   - each fact, at its profile's cost (`ClosureCosts.For`), with the
     projection's open row beside it (`InferenceOptions.RowBytes`);
   - the fact store's record array and table, at their new size before
     they grow;
   - each explicit fact;
   - each entry of a pass's record of changes;
   - each entry of counting's frontier, queue and seeded set while it
     lives;
   - each fact's premise sets in a cross-graph binding, and their
     propagation's entries.
   
   A build is **admitted** only if its given facts alone fit, and each given
   fact read is charged before it is buffered. A request past the limit
   throws `ClosureLimitExceededException` on the build's own thread before
   the allocation; the closure gives back everything it took when its owner
   drops it. Catching `OutOfMemoryException` is not the mechanism.
2. **One pool for the process.** Every binding's budget also takes from a
   pool shared by the process; a request is refused when either would
   pass. The pool is what is left of the heap limit after the memtable's
   worst case and a stated reserve (point 10). `SettingsCheck` refuses a
   `ClosureBytes` above the pool.
3. **The figures are measured, per profile.** `ClosureCosts.For(profile)`
   is set from what a build needs against what its budget counts, measured
   under `DOTNET_GCHeapHardLimit` (below), and a test fails when a build
   needs more than the budget assumes.
   
   **Measured** on REC S (3 graphs; `Varve.Benchmarks --closure-budget S
   <profile> <per-graph|cross>`): each from-scratch build's least heap
   limit, bisected to 1 MiB, less the heap held before it, over the
   budget's peak. The first estimates charged every profile alike: 128
   bytes a fact beside its record, 96 a working entry, 256 a fact a pass
   touches, 160 a fact's premise sets, 80 an explicit fact.

   | Profile | Build | Need ÷ counted, estimates | Off by | Need ÷ counted, measured figures |
   |---|---|---|---|---|
   | `rdfs` | per graph | 0.811 | counted 23% more than needed | 0.815 |
   | `rdfs` | cross-graph | 1.073 | counted 7% less: a build could outrun its limit | 0.872 |
   | `owl2rl` | per graph | 0.690 | counted 45% more | 0.883 |
   | `owl2rl` | cross-graph | 0.815 | counted 23% more | 0.747 |

   The measured figures are 100 bytes a fact under `rdfs` and 70 under
   `owl2rl`, and 280 a fact's premise sets under both; the working entry,
   touched fact and explicit fact keep their estimates, which no build
   showed off. Each build now needs at most 0.88 of what it is counted.

   **The margin** the budget keeps above them is **a tenth**: a build must
   finish under a heap limit of the heap held before it and 0.9 of its
   budget's peak, so the budget counts at least a ninth more than any build
   needs, room for the collector's own slack and for data unlike REC S's.
   The figures come from REC S alone, so the nightly's `closure-budget` job
   holds REC S and REC M to that margin, under both profiles, per graph and
   cross-graph (`tests/Varve.Benchmarks/closure-budget-margin.sh`), and
   fails when a build needs more; on REC S it also bisects the least heap,
   to report how far under the count each build stays. Run by hand on
   2026-11-18, REC M per graph finished within the margin under both
   profiles: under `rdfs` in 4.98 GiB (the heap before, 1.08 GiB, and 0.9 of
   a 4.34 GiB peak), under `owl2rl` in 4.88 GiB (0.9 of 4.22 GiB). REC M
   cross-graph is left to the nightly. REC M's peaks, by the same count:
   4.66 GB under `rdfs` (17.5 million facts) and 4.54 GB under `owl2rl`
   (23.1 million), the projection's rows (96 bytes a fact) besides.
4. **A generation is made for stated causes only:** a commit that changes a
   bound TBox graph, a commit that allocates a term a rule names, and a cold
   start or a saved state that does not check. "Any failed pass is a
   generation" is removed.
5. **A stopped binding stays refused.** A budget refusal, or an
   `OutOfMemoryException` that reaches a pass all the same, stops the
   binding at its memory limit (`stopped (memory-limit)`). A failure that
   no repeat can cure (point 9) stops it as `stopped (failed)`, answered
   `inference-unavailable` with `reason` `failed`. Either answers 503 with
   no `Retry-After`, `failure` `configuration`. Neither is tried again
   automatically: not by a data commit, a TBox commit, a reset or a timer.
   Point 6's "tried again … on a commit that starts a generation" is
   withdrawn. How an operator brings it back is point 8.
6. **Readiness reports `held` and `peak`** per binding: the bytes its
   closures hold now, and the most they have held, the build included, by
   the budget's count.
7. **The gate** (`Varve.Server.Tests`, `ClosureMemoryGateTests`) runs the
   server as a child process under a 512 MiB hard heap limit with
   `ClosureBytes` at the most the settings allow:
   - a closure past it is refused, as `stopped (memory-limit)`, and a read
     answers 503 `inference-unavailable` with no `Retry-After`;
   - the process stays up;
   - a second dataset's binding keeps answering;
   - the refused binding stays refused after a TBox commit.
   
   A second test, under both profiles, intra- and cross-graph, builds data
   whose budget peak is a tenth under its limit on the least heap the
   settings allow for it; the build must finish.

8. **How an operator brings a stopped binding back.** There are two
   acts, and no other:
   - **Restart the instance.** A projection's saved state that records a
     stop is not loaded: the restarted instance builds the binding from the
     head, once, under the limit it reads at start. A restart with nothing
     changed stops a `memory-limit` binding again, at the same place, since
     the same data needs the same memory; so the act is a restart with a
     raised `Varve:Inference:ClosureBytes`, or more memory for the
     instance, and readiness's `peak` from the stop says how much the build
     had asked for when it was refused. A `failed` binding restarted
     unchanged fails again unless the defect is fixed in the version
     started.
   - **Change the binding's declaration** in the management plane
     (`varve apply`, or a `PUT` to `/manage/datasets/{name}`): its profile,
     its TBox graphs, its data graphs or `varve:crossGraph`. The changed
     binding is another projection, built at once from the head, with no
     restart, under the limit in force. Re-applying the same declaration
     changes nothing and builds nothing.
   
   There is no "retry" act on a running instance: a retry under the same
   limit and the same data cannot cure either stop.
9. **What holds a pass off, and what stops a binding.** A store read or
   write that fails is not a stop. Every pass reads what it needs from the
   store — the delta's terms, whether a changed graph is emptied, a term a
   rule names — before it changes a closure, so a read that fails leaves the
   closures as they were and the position where it was. The failure is an
   `IOException`, a `TimeoutException` or an `HttpRequestException`. The
   feed logs it, holds off (one second, doubling to thirty), and resumes
   from the projection's position, as a follower's catch-up does, with no
   generation. A generation's build that fails to read is made again the
   same way, the closures in use kept meanwhile. A save of the projection's
   state that fails is logged; the next save tries again, and a restart
   before it rebuilds or reads the last good save. The failures that stop a
   binding, each because repeating it gives the same result:
   - **A budget refusal** (`memory-limit`): the same data needs the same
     memory, and the limit is read at start.
   - **An `OutOfMemoryException`** that reaches a pass all the same
     (`memory-limit`): the budget's figures undercounted (point 3), and a
     repeat asks the heap for the same bytes.
   - **Any other exception from the engine or the projection** (`failed`):
     a defect. The pass is a function of the commit and the closures, so
     the same commit fails again; repeating it would also spin.
   - **A store failure after a pass began changing a closure** (`failed`):
     the pass reads before it changes anything, so a read inside the change
     is itself a defect, and the closures are half changed: only a build can
     repair them, and a build is what the stop's recovery (point 8) makes.
   
   A test (`StorageFailureTests`) fails the store's reads during a pass,
   lets them recover, and requires the binding following again at the head
   with its generations unchanged and its rows the oracle's; a second fails
   a pass with a defect and requires `stopped (failed)` through a data
   commit, a TBox commit and a reset.
10. **The pool is what the heap limit leaves.** The pool is the collector's
   heap limit, less the memtable's worst case, less a reserve for queries,
   caches and requests (`Varve:Inference:ReserveBytes`). It is none when
   those leave nothing, and then every materialising binding is refused at
   its memory limit.

   ADR 0244's default memtable limit is a sixteenth of the heap limit at
   215 bytes an entry, between 10,000 and 1,000,000 entries. Its worst case
   is four times its limit (ADR 0244 point 5): a quarter of the heap limit
   up to a heap limit of 3.2 GiB, and 820 MiB above that.

   **The reserve is 2 GiB by default**, measured at the small-machine
   settings (`Varve.Benchmarks --server-reserve`): a 5 GiB heap limit,
   `MaxConcurrentReads` 4, `MaxQueryMemory` 256 MiB, and no binding. A
   million quads were loaded and the server restarted. Then, for 120 s,
   eight clients sorted, grouped, deduplicated and constructed the whole
   dataset, and a writer committed 200 quads at a time:

   | Heap limit | Peak resident | What the load got |
   |---|---|---|
   | 5 GiB | 2,852 MiB | 33 answered, 6 `read-limit-exceeded`, 3,419 commits |
   | 2 GiB | 2,135 MiB | 31 answered, 7 `read-limit-exceeded`, 3,154 commits |
   | 1.5 GiB | 1,527 MiB | 37 answered, 4 `read-limit-exceeded`, **3 failed with 500**, 3,096 commits |
   | 1.25 GiB | 1,356 MiB | the dataset failed: 80,125 commits and 821,813 reads refused `503 unavailable` |

   So the rest of the server needs more than 1.5 GiB and at most 2 GiB of
   heap there; the memtable was held to 50,000 entries (41 MiB at its worst)
   so that its share is known. The reserve is expected to grow with
   `MaxConcurrentReads` × `MaxQueryMemory`, which was not measured. An
   instance that raises either should raise the reserve: the default of 64
   concurrent reads could ask the queries alone for 16 GiB.

   **What they promise together.** The closures (at most the pool), the
   memtable at its worst and the reserve never pass the heap limit. At the
   small-machine settings (5 GiB):
   - the memtable's worst case is 820 MiB;
   - the reserve is 2 GiB;
   - the pool is 2.2 GiB;
   - `ClosureBytes` defaults to 1.25 GiB.

   Under a heap limit of 2.67 GiB the defaults leave no pool, so a small
   container serves no materialising binding until its operator lowers the
   reserve or the memtable. **An empty pool never stops the instance
   starting**: bindings are declared in the management plane after it has
   started, and a server that declares none runs at any heap limit
   (tested from 128 MiB to 4 GiB). A materialising binding declared on an
   instance with no pool is stopped at its memory limit as it is declared,
   before it builds anything, with a `limit` of 0 on readiness. Every
   closure read of it answers `503 inference-unavailable`, no
   `Retry-After`, its detail naming the heap limit, the memtable's worst
   case and `Varve:Inference:ReserveBytes`, and what to lower or raise; the
   log says the same at error level. Nothing tries it again; a restart with
   memory for a pool builds it. Other datasets, and its own tier-1 reads,
   are served (`ClosureMemoryGateTests`). A read of a stopped binding is
   refused before it is waited for, so it never answers `inference-behind`.

   The instance refuses at startup:
   - a `ClosureBytes` above the pool;
   - a negative `ReserveBytes`.

   Unset, `ClosureBytes` is a quarter of the heap limit, or the pool if
   that is less.

**The ledger.** The rulings are `BudgetAskedBeforeEveryAllocation`,
`OnePoolPerProcess`, `CostsMeasuredPerProfile`,
`GenerationsForStatedCausesOnly`, `StoppedStaysRefused`,
`StopsAreRecoveredByAnAct`, `StorageFailuresHoldOff`,
`PoolIsWhatTheHeapLeaves` and `MarginAboveTheMeasuredCosts`.

