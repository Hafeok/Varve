# 0159 — The extent merge policy: size-tiered with a fan-out and a cap

## Status

**Accepted — filed unaccepted by milestone Extent maintenance of #110,
2026-10-19** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Extent maintenance plan,
2026-10-19:
- size-tiered with a stated fan-out;
- `MaxSetBytes` in scope, its default stated from the REC M measurement;
- the latency gate relative, within one run on one machine: head reads at
  100 checkpoints with merges within 1.5× of that run's 10-checkpoint row,
  at p50 and p99; an as-of read inside the largest set within 2× of the same
  row's.

**Amended 2026-10-23** (ADR 0068), by #133, unaccepted until the maintainer
accepts it (ADR 0066): the cap no longer bounds an as-of read's cost, and
its default stays 256 MiB for other reasons
([ADR 0178](0178-the-format-5-upgrade-and-the-set-cap.md)); see the end.

Builds on [ADR 0158](0158-background-extent-merges.md), which says how a
merge runs. This ADR says which sets merge, and when.

Records decision set [`the-extent-merge-policy`](../decisions/the-extent-merge-policy.md).

## Context

Two costs pull against each other:
- **A read of the present merges one run per set,** so it wants few sets.
- **An as-of read inside a set narrows that set,** rewriting its rows
  restricted to the window (ADR 0138). It costs the set's size, so it wants
  small sets.

Writes add a third cost: every merge rewrites its inputs, so a byte
checkpointed is written again once per merge it takes part in.

REC M (ADR 0141) is front-loaded. Its first commits build the portfolio, and
the later ones are small changes, so a chain of equal windows has one large
set and many small ones.

## Decision

**1. The set is the unit of merge.** A set's six order extents, its
dictionary, graph-version index and commit index share one window in the
catalog and merge together. The bound on sets is therefore the bound on
extents per permutation.

**2. Two rules choose the next merge**, both over sets below half the cap
(point 3), neither making a set past the cap. Here `F` is the fan-out.
- **Similar sizes.** The oldest run of `F` adjacent sets whose largest is at
  most `F` times their smallest merges into one. Sets arrive at one size when
  checkpoints are taken on a schedule, and their merge lands one size class
  up, so each byte is rewritten about once per class it climbs. Classing sets
  by fixed tiers instead would leave sets straddling a tier's edge — 0.9 MiB
  and 1.1 MiB, say — never side by side, and the chain unbounded. The
  property test found exactly that.
- **Pressure.** When more sets than the bound stand below half the cap,
  whatever their sizes, the adjacent run of up to `F` with the fewest bytes
  together merges.

**3. The cap, `MaxSetBytes`.**
- A set past half of it is not merged again, and no merge makes a set past it.
- So an as-of read inside a set costs at most a capped set's narrowing.
- The default is **256 MiB**. At REC M the largest set a merge makes is then
  211 MiB, against 183 MiB for the largest set of a 10-checkpoint chain
  without merges. That keeps the as-of read inside the largest set within the
  gate (point 6).

**4. The bound** is at most `(F − 1) × (T + 1)` sets below half the cap, where
`T` is the tier of half the cap: `⌈log_F((MaxSetBytes / 2) / 1 MiB)⌉`. On top
of that come fewer than `2 × total / MaxSetBytes` sets past half the cap, and
one merge in flight.
- At the defaults, `F = 4` and 256 MiB, the bound below half the cap is 15.
- At REC M the chain held 3 to 10 sets after each of 100 checkpoints, 5 at the
  hundredth.
- A merge is a pure function of its inputs (ADR 0158), so the bound costs no
  answer.

**5. Merges are triggered by the catalog, never by time.**
- A merge is considered when the catalog changes — a checkpoint, a drop or
  another merge — in the dataset's maintenance, one merge a round, until none
  is due.
- A checkpoint on the API starts a round when maintenance runs in the
  background.
- Nothing is triggered by elapsed time.

**6. The policy is configurable within limits, and the default holds the
gate.**
- `ExtentMergePolicy` in `DatasetOptions`, on the server
  `Varve:Store:ExtentMerges`: `Enabled` (true), `FanOut` (2 to 16, 4 by
  default) and `MaxSetBytes` (1 MiB or more, 256 MiB by default).
- Measured at REC M on files, a checkpoint every hundredth of the history and
  maintenance after each, in one run (`--extent-maintenance M`, the
  traceability record):

  | | Without merges, 10 sets | With merges, 100 checkpoints (5 sets) |
  |---|---:|---:|
  | Head: a quad's lookup, p50 / p99 | 59.0 / 123.8 µs | 12.1 / 37.8 µs |
  | Head: a subject's quads, p50 / p99 | 9.0 / 40.9 µs | 4.6 / 13.2 µs |
  | Head: an exact count, p50 / p99 | 37.7 / 86.7 µs | 14.9 / 31.3 µs |
  | As-of inside the largest set | 2,358 ms (183 MiB) | 2,736 ms (211 MiB) |

  Every head read is within 1.5× of the 10-set row — below it, in fact — and
  the as-of read inside the largest set is within 2× (1.16×). The gate
  compares the largest set of each chain: REC M's history is front-loaded, so
  the 10-checkpoint chain's "inside a set" figure, at a middle set, reads a
  small set (33 ms), and against it the merged chain's largest set would not
  be within 2×. The first run of the same benchmark gave 1.18×.
- **Write amplification:** merges rewrote 746 MB for 420 MB checkpointed over
  REC M's history, **1.77 bytes per byte**.
- **The absolute cost.** An as-of read inside the largest set at REC M takes
  **2,736 ms**. Narrowing rewrites the set's rows restricted to the window, so
  it is O(set size), and the figure grows with `MaxSetBytes`: the cap is what
  bounds it. Narrowing in O(matches) is a separate issue the maintainer is
  taking; `MaxSetBytes` is not lowered for it here.
- The as-of read at a set's end stays under 0.1 ms.

## Alternatives considered

- **Tiers by fixed size classes, a run of `F` in one class.** The first
  version. Over generated sizes the property test found chains where no two
  adjacent sets shared a class and nothing ever merged.
- **Pairwise merging whenever the newer set is a quarter of the older**, the
  projection runs' rule (ADR 0070). Each byte would be rewritten about
  `log_{1.25}(N)` times, some twenty at a hundred checkpoints, against about
  twice here.
- **Levelled merging**, one sorted set per level. Its write amplification is
  larger than tiering's for a workload that only appends.
- **No cap.** Head reads would be best, with everything in one set, but an
  as-of read anywhere inside the history would narrow the whole history:
  seconds at REC M, growing with the dataset.

## Consequences

- The operator guide's checkpoint budget (#92) is removed. The guide gives
  this policy, its bound, the measured write amplification and the cap's
  trade.
- `Keep` keeps its meaning: at most `Keep` sets, the oldest merged into the
  next by a drop.
- A higher fan-out rewrites less and keeps more sets; a smaller cap makes
  as-of reads inside a set cheaper and keeps more sets.

## Checks

- **Checked against the accepted ADRs** (0001–0147). Touches:
  - **0070**: maintenance off the sequencer;
  - **0078**: `CheckpointPolicy` unchanged;
  - **0138**: as-of reads inside a set narrow it.

  No conflict.
- **Layer ownership.** `ExtentMergePolicy`: `Varve.Store`, layer 4. The
  settings: `Varve.Server`, layer 6.
- **Analyzer rule.** None.
- **Tests.**
  - `ExtentMergeTests.the_policy_keeps_its_bound_over_any_sequence_of_sets`:
    500 generated sequences of sizes and fan-outs, merged to exhaustion after
    each arrival, never past the bound.
  - `a_tier_is_the_fan_out_logarithm_of_a_sets_bytes`, with sizes either
    side of a tier's edge merging.
  - The read latency gate, in `tests/Varve.Benchmarks`
    (`--extent-maintenance`).

## Amendment, 2026-10-23 — the cap bounds merges, not reads

Filed by #133, unaccepted until the maintainer accepts it (ADR 0066); ruled
in [ADR 0178](0178-the-format-5-upgrade-and-the-set-cap.md).

- Point 3's "so an as-of read inside a set costs at most a capped set's
  narrowing" holds only of a format-4 set. A format-5 set is sought
  ([ADR 0177](0177-seek-dont-narrow-intervals-beside-the-key.md)), and an
  as-of read inside it costs its matches at any size: 0.29× a head lookup
  inside the 211 MiB set at REC M.
- The cap now bounds one merge's work and the largest blob an upload
  carries. Its default stays **256 MiB**: at 1 GiB REC M's chain held 7 sets
  against 5, and head lookups were slower, 21.4 against 15.2 µs at p50.
- The consequence "a smaller cap makes as-of reads inside a set cheaper"
  is withdrawn. A larger cap keeps fewer sets past half of it on a dataset
  many times its size; the operator guide gives the trade.
