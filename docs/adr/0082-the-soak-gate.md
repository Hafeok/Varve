# 0082 — The soak gate: a flat working set within a stated band

## Status

**Accepted — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

States the 1.0 gate that issue #61 blocks (issue #15), and how it is measured,
so that "flat" is a number.

**Revisit condition:** a host or workload the soak does not represent being
named for 1.0.

**Amended 2026-10-09** (ADR 0068), by milestone Operability of #12 under ADR
[0110](0110-the-shipped-runtime-configuration.md): the gate is judged under
the shipped runtime configuration, the default-runtime figures beside it; see
the end.

**Superseded in part 2026-10-10 by ADR
[0110](0110-the-shipped-runtime-configuration.md)** (its point 5): the
drift's reference window is minutes 30–40, not 10–20. The band, the
workload, the samples and the bounds on handles and files stand.
`FlatWithinABand` is in 0110's set.

## Context

6a ran a one-hour soak and found the working set not flat (median 168 → 730
MB, peaks 2.8 GB), and did not separate its causes. The maintainer opened
#61, blocking the 1.0 soak gate, and asked 6c to separate them and state the
gate as a flat working set within a stated band. Two things make "flat" need
a definition: the 6a workload's dataset grows for the whole hour (its
vocabulary allows ten million quads), and a store whose dataset grows uses
more of something — at least the fences and the commit table.

## Decision

**The soak** is `--soak 60` of `tests/Varve.Benchmarks` with the checkpoint
policy on (`--policy`: every 1,000 commits, keeping 12, so that the as-of
reads at up to 10,000 positions back have a checkpoint within 1,000): one
writer committing batches of 50 with 25 ms between, two readers pinning and
scanning and reading as of a position, background maintenance, a memtable of
20,000. It samples every 30 seconds after a full collection, with a one-second
sampler of the peak between samples.

**The gate**: over the last 50 minutes, every 30-second sample of the working
set less the dataset's own lies within ±25% of their median, and the median of
the last ten minutes is within 10% of that of minutes 10 to 20; open file
handles and `derived/` files stay within fixed bounds. **The dataset's own** is
what grows with it and nothing else, and is reported per quad and per commit
beside the band:

- what a reader holds of its runs' and checkpoints' directories (ADR 0080):
  a run's whole directory — the fences and where each block begins, 40 bytes
  per 128 keys per section — and a twentieth of a checkpoint's, which is held
  sparsely; summed from each derived file's header, so the soak reads them
  from the files rather than from the store's internals;
- the table of commits, 144 bytes a commit, measured by opening a log of
  102,000 commits that leave the index empty against one of 2,000.

The raw working set is reported beside it, and its peaks.

The gate is run and reported at each milestone that touches the store; it is
not in CI.

## Alternatives considered

- **Bound the working set absolutely.** Machine- and runtime-dependent; a
  band around the run's own median is not.
- **A shrinking vocabulary so that the dataset reaches a steady state.**
  Hides growth that is the store's, not the dataset's.

## Consequences

- The 6c soak report states the band and whether it held, with the ablations
  that separate the causes.

## Checks

- **Checked against the accepted ADRs** (0001–0077). Touches **0070** and
  **0078** (what the soak exercises). No conflict.
- **Layer ownership.** None; a host's measurement.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-09 — judged under the shipped runtime configuration

Filed by milestone Operability of #12, unaccepted until the maintainer
accepts it (ADR 0066). Decided by the maintainer on the 7b report and the
Operability plan. It adds the configuration the gate is judged under; the
workload, the samples and the band above stand, and the drift's reference
window is superseded by ADR 0110 point 5 (2026-10-10, see the Status line).

**The decision.** The gate is judged under the runtime configuration
`Varve.Server` ships (ADR 0110: workstation concurrent GC,
`System.GC.ConserveMemory=5`, no heap hard limit of Varve's own), the
benchmark process run with the same three knobs as environment variables
(`DOTNET_gcServer=0`, `DOTNET_gcConcurrent=1`, `DOTNET_GCConserveMemory=5`).
The default-runtime hour is reported beside it for comparison, and the
128 MB `DOTNET_GCHeapHardLimit` of the 6c addendum stays a harness setting.
The one-hour soak is re-run under exactly the shipped configuration, and
that run closes #61 if it holds the gate. It did not, by ADR 0110 point 5's
two-hour evidence (2026-10-10), and #61 stays open.

**The reason.** 6c separated the causes and found the store's live heap
flat and the working set the collector's choice: the large object heap
uncompacted and committed memory kept high when allocation is low
(`docs/traceability/2026-10-05-issue-10-milestone-6c.md`). The gate as first
stated measured the default runtime's choices as if they were the store's.
A gate on the configuration the product ships measures what an operator
runs. The alternative, "the working set a host is expected to run with",
was the restatement the 6c report declined to make; this is not a
restatement: the criterion, the band and the drift are unchanged.

**The ledger.** `FlatWithinABand` moves to ADR 0110's set under the same
key, restated with the later window (the supersession in part). The ruling
of this amendment is the new key `JudgedUnderTheShippedConfiguration` in
this ADR's set, unaccepted until the maintainer accepts it.