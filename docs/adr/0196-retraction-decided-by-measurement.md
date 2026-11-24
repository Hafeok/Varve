# 0196 — Retraction in the closure, decided by measurement

## Status

**Accepted — filed unaccepted by Inference, part 2 (#171, under #95),
2026-11-06**
(ADR 0066). Decided by the maintainer on the plan in #95 (2026-10-27),
question 4b: three candidates, measured on REC M histories with retractions,
the choice made on the measurement. Acceptance is the maintainer's act on the
pull request.

**The measurement and the choice are in** (point 6). Recursive counting led
on the first rule, by p99 per commit alone. **The maintainer ruled on
2026-11-19 that DRed is chosen**, and amended point 5's rule to weigh the
build: a cold start, a generation and a recovery each pay it, and counting
builds in about twice DRed's time. Counting and Backward/Forward, as
retraction strategies, have moved to `tests/Varve.Benchmarks`; the proof
search stays in `src/` for explain (ADR 0199).

Amends **0172** (deletion as a second `Propagation`; join anchors in
Inference), by a dated amendment there.

## Context

Insertion into a least fixpoint is semi-naive propagation, which `Varve.Derivation`
already holds (ADR 0172 point 2). Retraction is not its mirror: a fact derived
from a retracted premise may still hold through another derivation, and
OWL 2 RL/RDF's rules are recursive (`cax-sco` over `rdf:type`, `prp-trp`,
`scm-sco`), so a derivation can support itself through a cycle. Three
algorithms are sound on recursive Datalog, and no one wins on every input
(Motik, Nenov, Piro and Horrocks, *Maintenance of Datalog materialisations
revisited*, AIJ 2019):

1. **DRed**, in AIJ 2019's form, which never repeats a derivation:
   over-delete everything a retracted fact derives, then re-derive what still
   holds.
2. **Counting**, recursive, from AIJ 2019. Classic counting is unsound on
   recursive strata. It keeps a count of derivations per fact, per iteration,
   which cuts against "store nothing per entailment"; this ADR weighs that.
3. **Backward/Forward** (Motik et al., AAAI 2015), with FBF (AIJ 2019) for
   stratified negation: for each fact that loses a derivation, search
   backwards for another before deleting it. GraphDB's "smooth delete" is in
   essence this. Its backward search is also explain's proof search (ADR 0199).

## Decision

### 1. Each candidate is a second `Propagation`

Each is a deletion pass with its own transfer over the binding's
`DependencyGraph` (ADR 0172 point 4); `Propagation` is unchanged. Insertion is
the existing semi-naive pass. A rule's join is anchored at each changed fact:
the fact is unified with every body pattern it matches, and the bindings are
pre-bound into the rest of the body, evaluated over the current closure.
These join anchors live in `Varve.Inference`, by ADR 0172's amendment.

### 2. All three are built behind one internal strategy, and all three pass

While the measurement runs, the three live in `Varve.Inference` behind one
internal strategy, `IRetraction`, and **each passes ADR 0195 point 9's
property** (incremental equals from scratch at every position, byte for byte).
A candidate that fails it is not measured.

### 3. The workload

REC M generated histories, by the RecPortfolio generator: 622 buildings,
25,000 commits, profiles `rdfs` and `owl2rl`, over REC 4.1.0 and its pinned
Brick. The retraction rates:

| What | Share of commits |
|---|---:|
| retract 1–3 whole entities, every quad of each | 20% |
| retract or change an `rdf:type` | 1% |
| change the TBox: remove or re-point a `subClassOf` edge, half within the top three levels | 0.1% |

### 4. What is reported, per candidate and profile

- closure size, implicit and total;
- time per commit: median, p99, maximum;
- memory, the count store included for counting;
- **the worst case named**: the commit, its delta, and the facts over-deleted
  (DRed), recounted (counting) or checked (B/F).

The worst case of interest is a TBox edit near Brick's `Point` or `Equipment`
roots. A benchmark row compares that edit maintained incrementally with a
from-scratch generation (ADR 0195 point 4).

### 5. What decides

- **The rule, as amended by the maintainer on 2026-11-19.** On the
  `owl2rl` profile at REC M, over at least 1,000 commits:
  1. take the candidate with the lowest p99 time per commit;
  2. **prefer another whose p99 is within the bound** (at most twice the
     lowest, and under 50 ms) **and which builds from scratch in at most 0.6
     of the first's time**: about half;
  3. otherwise, where another is within 20% of the lowest p99 and lower on
     the maximum or on memory, the ADR states the trade and the maintainer
     rules.

  The build is weighed because each cold start, each generation (a TBox
  commit, a term a rule names) and each recovery from a stop pays it in full
  (ADR 0195 points 4 to 6), while a commit's p99 inside the bound is
  milliseconds either way.
- **The regime in which each wins** is stated, from the worst cases.
- **Whether a size threshold switches to a from-scratch generation** is
  stated: if a commit's over-deletion or check set exceeds a share of the
  closure beyond which a generation is cheaper on the measurement, the
  threshold is that share.
- **The losers leave `src/`.** Their code moves to `tests/Varve.Benchmarks`,
  where the measurement can be rerun, and where the engine's tests still
  hold them to the property (point 2) by linking them in.

### 6. The measurement

The machine: 4 cores, 15 GB, .NET 10.0.15, Release. `Varve.Benchmarks
--inference-closure <size> <profile> <commits> [budget minutes]`: the
closures built at the history less its last `<commits>` commits, each
graph's over the shared TBox closure (ADR 0195 point 4), then those commits
applied one by one under each candidate, each candidate in a closure of its
own, built from scratch first.

**REC M, `owl2rl`, 1,000 commits** (rerun 2026-11-19, on the architect's
review: a 50-commit sample's p99 is its worst commit). 63 graphs, 5,050,283
quads at commit 24,000 of 25,000; 23.1 million facts held, 18.0 million
rows. **The sample:** 1,000 commits, of which 432 retract something, **95
retract an entity** (every type of a subject, none asserted back) and **52
change a type** (a type retracted and another asserted for the same
subject). By kind: 349 points added, 219 tenants in, 155 splits, 130
moves, 52 retypes, 48 tenants out, 29 points retired, 18 merges. No commit
of it changes the TBox. That is 9.5% of commits retracting an entity and
5.2% changing a type, against point 3's 20% and 1%: REC M's generator
retracts by renovation kinds, not by those shares, and point 3's rates are
not what was measured.

| Candidate | Built from scratch (a cold start, a generation) | Heap held | Per commit p50 | p99 | Worst, and its commit | All 1,000 commits |
|---|---:|---:|---:|---:|---|---:|
| Recursive counting | **492.5 s** | 3,621 MiB, 165 B a fact | 0.45 ms | **11.40 ms** | 16.0 ms, 24,991 (a merge) | 1.0 s |
| DRed | **255.7 s** | 3,607 MiB, 164 B a fact | 0.36 ms | 19.67 ms | 30.3 ms, 24,697 (a merge) | 3.5 s |
| Backward/Forward | 257.5 s | 3,607 MiB, 164 B a fact | 0.53 ms | 1,283.57 ms | 1,407.5 ms, 24,706 (a merge) | 223.2 s |

Every candidate ends at the same 23,026,270 facts.

**REC M, `owl2rl`, 50 commits** (2026-11-16, the first measurement, at
commit 24,950; its p99 is its worst commit):

| Candidate | Built from scratch | Per commit p50 | p99 | Worst, and its commit |
|---|---:|---:|---:|---|
| Recursive counting | 575.1 s | 0.92 ms | 19.84 ms | 19.8 ms, 24,991 (a merge of rooms) |
| DRed | 302.4 s | 0.92 ms | 32.65 ms | 32.6 ms, 24,958 (a merge) |
| Backward/Forward | 292.8 s | 459.47 ms | 1,615.56 ms | 1,615.6 ms, 24,990 (a split) |

**REC S** (3 graphs, 203,047 quads; measured before the fact store held
counting's state in its records, so its memory column is that store's):

| Profile | Candidate | p99 | Worst | B a fact |
|---|---|---:|---:|---:|
| `rdfs` | Recursive counting | 7.5 ms | 7.5 ms | 590 |
| `rdfs` | DRed | 42.8 ms | 42.8 ms | 371 |
| `rdfs` | Backward/Forward | 520 ms | 520 ms | 391 |
| `owl2rl` | Recursive counting | 10.5 ms | 10.5 ms | 668 |
| `owl2rl` | DRed | 26.9 ms | 26.9 ms | 377 |
| `owl2rl` | Backward/Forward | 880 ms | 880 ms | 408 |

**The choice, by the amended rule: DRed.** At REC M under `owl2rl` over
1,000 commits:

| | Recursive counting | DRed | Backward/Forward |
|---|---:|---:|---:|
| p99 per commit | **11.40 ms** (the lowest) | 19.67 ms | 1,283.57 ms |
| p99 against the lowest | 1 | 1.73: within the bound (≤ 2, < 50 ms) | 113: outside it |
| Built from scratch | 492.5 s | **255.7 s** | 257.5 s |
| Build against the lowest p99's | 1 | **0.52**: at most 0.6 | 0.52 |

DRed's p99 is within the bound of counting's and it builds in 0.52 of
counting's time, so DRed is chosen. Backward/Forward builds as fast, but its
p99 is outside the bound. On the first measurement (50 commits) the same
holds: 32.65 ms against 19.84 ms (1.65), and 302.4 s against 575.1 s
(0.53). **The trade, stated:** per commit, counting would save 8.3 ms at p99
and 14.3 ms at its worst, 2.5 s over the 1,000 commits; DRed saves 237 s at
every build. The medians are within a tenth of a millisecond (0.45 ms and
0.36 ms).

**Where each wins.** Counting and DRed are alike at the median; DRed's worst
commits are merges whose over-deletion reaches far, which counting's counts
stop at once. Backward/Forward checks every fact a retraction reaches for a
proof: its median is now near the others', since most of the 1,000 commits
retract nothing, but a merge costs it over a second.

**No size threshold.** A data commit's worst, 16.0 ms, is four orders of
magnitude under a generation's 492.5 s; there is no share of the closure past
which a generation is cheaper on this measurement. A TBox commit is a
generation already (ADR 0195 point 4).

**The losers have left `src/`.** Recursive counting and Backward/Forward,
as retraction strategies, are in `tests/Varve.Benchmarks/Retraction`, behind
the engine's internal `Retraction`; `--inference-closure` measures all
three, and `Varve.Inference.Tests` links them in and holds them to the same
property as DRed. Backward/Forward's proof search stays in `src/` as
explain's (ADR 0199).

**What the fact record loses.** It carried counting's round and count, 8
bytes a fact: the record is now 64 bytes, its fact and its links in the five
indices, against 72. Nothing served read them: the round and count were
counting's bookkeeping, and DRed needs neither. Counting, measured from the
benchmarks, keeps them in a map of its own beside the closure. What the
closure loses with them is counting's retraction itself: a retraction no
longer stops at a fact whose count stays above zero, so DRed's worst commits,
merges whose over-deletion reaches far, cost it 30.3 ms where counting took
16.0 ms. The budget's costs per fact were measured again for DRed's build
(ADR 0195 point 6), whose working sets are now charged as counting's were.
On REC S, at the costs measured for counting, DRed's build needed 0.653
(`rdfs` per graph), 0.718 (`rdfs` cross-graph), 0.771 (`owl2rl` per graph)
and 0.847 (`owl2rl` cross-graph) of what the budget counted, against
counting's 0.815, 0.872, 0.883 and 0.747: less, except cross-graph under
`owl2rl`. `rdfs` now charges 40 bytes a fact beside the record, not 100,
and gives 0.832 and 0.809; `owl2rl` keeps 70. All four keep the margin of a
tenth.

## Alternatives considered

- **Choose by the literature.** AIJ 2019 shows that the winner depends on the
  input, so the choice is made on REC's.
- **From scratch on every retraction.** The baseline every row is compared
  with, and the fallback point 5's threshold may choose for large deletions.
- **Keep all three in `src/`.** Three implementations of one function, two of
  them unused, is scaffolding.
- **Choose by p99 per commit alone** (this ADR's first rule): chose counting,
  whose build is twice DRed's. Replaced by the amended rule, which weighs the
  build.

## Consequences

- Part 2 built three deletion passes and keeps one, DRed, in `src/`.
- No count is stored per entailment: the fact record is 64 bytes.

## Checks

- **Checked against the accepted ADRs** (0001–0194, 0206, 0213–0217, 0224,
  0225). Touches **0172** (amended: deletion and join anchors), **0195**.
  No conflict.
- **Layer ownership.** `Varve.Inference` (4).
- **Analyzer rule.** None new. Each transfer, each anchor and each check is
  `[HotPath]`.
- **Open questions owned.** Point 6's measurement, by this part.
