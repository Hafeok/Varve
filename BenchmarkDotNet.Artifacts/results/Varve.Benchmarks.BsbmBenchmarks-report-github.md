```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-YFEFPZ : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

IterationCount=10  WarmupCount=3  

```
| Method       | Query            | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1     | Allocated | Alloc Ratio |
|------------- |----------------- |-------------:|------------:|------------:|------:|--------:|----------:|---------:|----------:|------------:|
| DotNetRdf    | Q10 cheap offers |   102.199 ms |  10.1523 ms |   6.0414 ms |  1.00 |    0.08 | 1000.0000 | 333.3333 |  22.49 MB |        1.00 |
| VarveStore   | Q10 cheap offers |     6.817 ms |   0.4462 ms |   0.2951 ms |  0.07 |    0.00 |  109.3750 |  23.4375 |    1.8 MB |        0.08 |
| VarveDataset | Q10 cheap offers | 7,804.409 ms | 214.3870 ms | 141.8037 ms | 76.62 |    4.73 |         - |        - |   1.37 MB |        0.06 |
