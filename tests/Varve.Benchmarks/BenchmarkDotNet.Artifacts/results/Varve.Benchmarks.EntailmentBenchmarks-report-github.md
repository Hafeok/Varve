```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.403
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Toolchain=InProcessEmitToolchain  IterationCount=8  WarmupCount=2  

```
| Method             | People  | Mean        | Error      | StdDev     | Gen0    | Gen1   | Allocated  |
|------------------- |-------- |------------:|-----------:|-----------:|--------:|-------:|-----------:|
| **RewriteAgentsCold**  | **10000**   | **1,420.17 μs** | **109.642 μs** |  **57.345 μs** | **11.7188** | **1.9531** | **1639.86 KB** |
| RewriteAgentsWarm  | 10000   |    39.02 μs |   4.351 μs |   2.276 μs |  0.3662 |      - |   53.13 KB |
| RewriteClassesWarm | 10000   |    47.77 μs |   5.078 μs |   2.656 μs |  0.4883 |      - |    68.1 KB |
| **RewriteAgentsCold**  | **100000**  | **1,396.40 μs** | **102.198 μs** |  **53.451 μs** | **11.7188** | **1.9531** | **1639.88 KB** |
| RewriteAgentsWarm  | 100000  |    35.42 μs |   1.988 μs |   1.040 μs |  0.3662 |      - |   53.13 KB |
| RewriteClassesWarm | 100000  |    48.49 μs |   6.640 μs |   3.473 μs |  0.4883 |      - |    68.1 KB |
| **RewriteAgentsCold**  | **1000000** | **1,546.97 μs** | **274.423 μs** | **143.529 μs** | **11.7188** | **1.9531** | **1639.88 KB** |
| RewriteAgentsWarm  | 1000000 |    35.40 μs |   4.703 μs |   2.460 μs |  0.3662 |      - |   53.13 KB |
| RewriteClassesWarm | 1000000 |    50.10 μs |   9.278 μs |   4.853 μs |  0.4883 |      - |    68.1 KB |
