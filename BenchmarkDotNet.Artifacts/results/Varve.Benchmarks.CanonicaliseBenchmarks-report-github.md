```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method    | Shape     | Mean       | Error    | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated | Alloc Ratio |
|---------- |---------- |-----------:|---------:|----------:|------:|--------:|----------:|----------:|---------:|----------:|------------:|
| **Varve**     | **Blank**     |   **255.4 ms** |  **5.09 ms** |  **14.35 ms** |     **?** |       **?** | **1000.0000** |  **500.0000** |        **-** | **177.67 MB** |           **?** |
| DotNetRdf | Blank     |         NA |       NA |        NA |     ? |       ? |        NA |        NA |       NA |        NA |           ? |
|           |           |            |          |           |       |         |           |           |          |           |             |
| **Varve**     | **Twins**     |   **270.0 ms** |  **5.38 ms** |  **15.19 ms** |     **?** |       **?** | **1000.0000** |  **500.0000** |        **-** | **200.28 MB** |           **?** |
| DotNetRdf | Twins     |         NA |       NA |        NA |     ? |       ? |        NA |        NA |       NA |        NA |           ? |
|           |           |            |          |           |       |         |           |           |          |           |             |
| **Varve**     | **Blank1000** |   **187.3 ms** |  **3.68 ms** |   **5.51 ms** |  **0.12** |    **0.01** |  **666.6667** |  **333.3333** | **333.3333** | **119.16 MB** |        **0.14** |
| DotNetRdf | Blank1000 | 1,524.7 ms | 39.72 ms | 113.34 ms |  1.01 |    0.11 | 6000.0000 | 3000.0000 |        - | 849.76 MB |        1.00 |

Benchmarks with issues:
  CanonicaliseBenchmarks.DotNetRdf: DefaultJob [Shape=Blank]
  CanonicaliseBenchmarks.DotNetRdf: DefaultJob [Shape=Twins]
