```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-LCXOWM : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

```

| Method   | Arm            | Case   | Mean       | Error     | StdDev    | Gen0       | Gen1       | Gen2      | Allocated |
|--------- |--------------- |------- |-----------:|----------:|----------:|-----------:|-----------:|----------:|----------:|
| Evaluate | InlineAccessor | eq     |   135.9 ms |   7.90 ms |   2.82 ms |  2000.0000 |          - |         - |  45.78 MB |
| Evaluate | InlineAccessor | gt-1%  |   141.4 ms |   4.45 ms |   1.97 ms |  2000.0000 |          - |         - |  45.78 MB |
| Evaluate | InlineAccessor | gt-50% |   164.4 ms |  47.32 ms |  24.75 ms |  2000.0000 |          - |         - |  45.78 MB |
| Evaluate | InlineAccessor | gt-99% |   158.8 ms |   6.56 ms |   2.91 ms |  2000.0000 |          - |         - |  45.78 MB |
| Evaluate | InlineAccessor | order  |   785.4 ms |  27.48 ms |  14.37 ms |  8000.0000 |  7000.0000 |         - | 180.78 MB |
| Evaluate | Externalise    | eq     |   323.3 ms |  33.44 ms |  14.85 ms | 20000.0000 |          - |         - | 342.55 MB |
| Evaluate | Externalise    | gt-1%  |   311.9 ms |  18.16 ms |   8.06 ms | 20000.0000 |          - |         - | 342.55 MB |
| Evaluate | Externalise    | gt-50% |   322.0 ms |  26.34 ms |  11.70 ms | 20000.0000 |          - |         - | 342.55 MB |
| Evaluate | Externalise    | gt-99% |   325.8 ms |   7.09 ms |   2.53 ms | 20000.0000 |          - |         - | 342.55 MB |
| Evaluate | Externalise    | order  | 3,943.5 ms | 170.26 ms |  89.05 ms | 32000.0000 | 31000.0000 | 1000.0000 | 649.43 MB |
| Evaluate | Materialise    | eq     | 3,246.4 ms | 144.22 ms |  75.43 ms | 32000.0000 | 31000.0000 | 1000.0000 | 691.61 MB |
| Evaluate | Materialise    | gt-1%  | 3,286.6 ms | 136.51 ms |  71.40 ms | 32000.0000 | 31000.0000 | 1000.0000 | 691.61 MB |
| Evaluate | Materialise    | gt-50% | 3,337.7 ms | 115.45 ms |  60.38 ms | 32000.0000 | 31000.0000 | 1000.0000 | 691.61 MB |
| Evaluate | Materialise    | gt-99% | 3,415.9 ms | 232.84 ms | 121.78 ms | 32000.0000 | 31000.0000 | 1000.0000 | 691.61 MB |
| Evaluate | Materialise    | order  | 4,958.1 ms | 305.06 ms | 159.55 ms | 38000.0000 | 37000.0000 | 1000.0000 | 826.61 MB |
