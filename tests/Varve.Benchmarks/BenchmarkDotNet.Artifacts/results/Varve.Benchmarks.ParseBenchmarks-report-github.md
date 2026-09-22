```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-XUEJKP : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=8  UnrollFactor=1  
WarmupCount=3  

```
| Method                         | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0       | Gen1       | Gen2      | Allocated   | Alloc Ratio |
|------------------------------- |-----------:|----------:|---------:|------:|--------:|-----------:|-----------:|----------:|------------:|------------:|
| &#39;Varve — views&#39;                |   148.5 ms |   3.42 ms |  1.79 ms |  1.00 |    0.02 |          - |          - |         - |       856 B |        1.00 |
| &#39;Varve — owned terms&#39;          |   167.0 ms |  11.43 ms |  5.08 ms |  1.12 |    0.03 |  1000.0000 |          - |         - |  24480848 B |   28,599.12 |
| &#39;Varve — into InMemoryDataset&#39; |   401.4 ms |  10.58 ms |  5.53 ms |  2.70 |    0.05 |  4000.0000 |  3000.0000 | 1000.0000 |  90194768 B |  105,367.72 |
| &#39;dotNetRDF — TripleStore&#39;      | 2,826.5 ms | 102.56 ms | 53.64 ms | 19.03 |    0.40 | 37000.0000 | 19000.0000 | 2000.0000 | 660680976 B |  771,823.57 |
