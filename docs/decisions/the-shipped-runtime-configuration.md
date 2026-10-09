---
set: the-shipped-runtime-configuration
namespace: varve
adr: 0110
decisions:
  - key: WorkstationConcurrentGcWithConserveMemory
    statement: "Varve.Server ships with workstation concurrent GC and System.GC.ConserveMemory=5 in its project file, carried by the tool's runtimeconfig and the Native AOT binary alike"
  - key: NoHeapHardLimitOfOurOwn
    statement: "The product sets no heap hard limit; in a container the runtime bounds the heap at its share of the cgroup limit, and the 128 MB DOTNET_GCHeapHardLimit stays a benchmark-harness setting"
  - key: SoakJudgedUnderShippedConfiguration
    statement: "The soak gate is judged under the shipped runtime configuration, run with the same three knobs as environment variables, with the default-runtime figures reported beside it"
  - key: ThroughputCostMeasured
    statement: "The 7a protocol benchmark runs under server GC and under the shipped configuration side by side, and the record states the throughput cost of the memory gate or that there is none"
---

The rulings of [ADR 0110](../adr/0110-the-shipped-runtime-configuration.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
