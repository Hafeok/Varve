# 0110 — The shipped runtime configuration: workstation concurrent GC, `ConserveMemory`, no heap limit of our own

## Status

**Proposed — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Decided by the maintainer on the Operability plan. Acceptance is
the maintainer's act on the pull request.

**Amends [0082](0082-the-soak-gate.md)** by a dated block: the gate is judged
under the configuration this ADR ships. Closes #61 with the soak reported
under it.

**Revisit condition:** a soak or a benchmark showing the shipped
configuration costing throughput that an operator would notice, or the
runtime changing what `ConserveMemory` does.

## Context

Milestone 6c separated the causes of the soak's working-set growth and found
the store's live heap flat (`docs/traceability/2026-10-05-issue-10-milestone-6c.md`).
What remained was the collector's: the large object heap, which the runtime
does not compact, fragmented by fences rebuilt at every merge, and committed
memory kept high when allocation is low. Its ablation table showed
`DOTNET_GCConserveMemory=5` compacting the large object heap and taking the
working set from 263 MB to 180 MB over twenty minutes, and its heap-limit
hour showed a 128 MB `DOTNET_GCHeapHardLimit` holding the band and the drift
with room to spare. The 7b soak under the default runtime missed the band at
two samples of a hundred and held the drift.

`Microsoft.NET.Sdk.Web` defaults a server to **server GC**: one heap per
core and committed memory kept proportional to what each has allocated,
which is the choice for a process that owns its machine. A Varve server runs
under a container memory limit beside other processes, holds a small live
heap, and allocates in bursts (an as-of read's tail, a checkpoint's blocks).
That is the profile the workstation collector with concurrent collections
fits, and the one the measurements were taken under.

## Decision

1. **`Varve.Server` ships with workstation concurrent GC and
   `System.GC.ConserveMemory=5`**, in its project file as
   `ServerGarbageCollection=false`, `ConcurrentGarbageCollection=true` and a
   `RuntimeHostConfigurationOption`, so that the tool's `runtimeconfig.json`
   and the Native AOT binary carry the same settings. Nothing is set in the
   container image that the binary does not already carry.
2. **No heap hard limit of Varve's own.** In a container the runtime bounds
   the heap at 75% of the cgroup memory limit by itself; outside one, the
   operator sets `DOTNET_GCHeapHardLimit` when the machine is shared, and the
   operator guide says so. The 128 MB limit of the 6c addendum stays a
   setting of the benchmark harness, never a product default: a limit chosen
   for a soak's workload is wrong for a bulk load's.
3. **The soak gate is judged under this configuration**, the benchmark
   process run with `DOTNET_gcServer=0`, `DOTNET_gcConcurrent=1` and
   `DOTNET_GCConserveMemory=5`, which are the same three knobs as
   environment variables; the default-runtime hour is reported beside it for
   comparison, as ADR 0082's amendment says.
4. **Throughput is measured, not assumed.** The 7a protocol benchmark
   (`--http-load`) runs under server GC and under the shipped configuration
   on the same machine in the same session, and this ADR's record in the
   Operability traceability record states the cost of the memory gate, or
   that there is none. Point 1 is revisited if the cost is one an operator
   would notice.

## Alternatives considered

- **Server GC with `GCHeapAffinitizeMask` and `GCHeapCount` tuned.** More
  knobs for the operator, and the defaults would still be wrong for the
  small-heap, burst-allocating profile measured.
- **A heap hard limit as a product default.** Rejected in point 2: the
  runtime already bounds the heap to the container, and a fixed number is
  right for one workload.
- **`System.GC.RetainVM=false` alone.** It releases segments but does not
  compact the large object heap, which the ablation showed to be the cause.

## Consequences

- The working set of a Varve server is the live heap plus the collector's
  modest headroom, and the soak gate is a number an operator can expect.
- A `Varve.Server` process on a dedicated machine with many cores leaves
  throughput on the table that server GC would take; point 4 says how much.
- `docs/operator/configure.md`'s runtime section states the configuration,
  the container's cgroup-derived heap limit, and when to set one by hand.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6.
  Touches **0082** (amended), **0101** (the host), **0105** (the tool carries
  the same runtimeconfig). No conflict.
- **Layer ownership.** `Varve.Server`, layer 6; the soak is a host
  measurement.
- **Analyzer rule.** None.
- **Open questions owned.** None.
