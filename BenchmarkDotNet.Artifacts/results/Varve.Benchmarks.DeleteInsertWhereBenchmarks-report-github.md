```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-KGWPIM : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

InvocationCount=2  IterationCount=8  UnrollFactor=1  
WarmupCount=2  

```
| Method    | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1      | Allocated | Alloc Ratio |
|---------- |-----------:|---------:|---------:|------:|--------:|----------:|----------:|----------:|------------:|
| Varve     |   185.9 ms | 34.07 ms | 17.82 ms |  0.11 |    0.01 |         - |         - | 125.14 MB |        0.41 |
| DotNetRdf | 1,747.1 ms | 82.81 ms | 43.31 ms |  1.00 |    0.03 | 2000.0000 | 1500.0000 | 301.89 MB |        1.00 |
