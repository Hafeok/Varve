```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                        | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated    | Alloc Ratio |
|------------------------------ |----------:|----------:|----------:|----------:|------:|--------:|----------:|----------:|----------:|-------------:|------------:|
| &#39;Varve — views&#39;               |  17.21 ms |  0.462 ms |  1.363 ms |  17.84 ms |  1.01 |    0.12 |         - |         - |         - |      6.13 KB |        1.00 |
| &#39;Varve — owned terms&#39;         |  27.59 ms |  0.638 ms |  1.881 ms |  28.50 ms |  1.61 |    0.18 |  281.2500 |         - |         - |  38131.13 KB |    6,217.56 |
| &#39;Varve — read and write back&#39; |  36.91 ms |  0.730 ms |  2.023 ms |  37.36 ms |  2.16 |    0.22 |  428.5714 |  428.5714 |  428.5714 |  15367.33 KB |    2,505.76 |
| &#39;dotNetRDF — Graph&#39;           | 770.81 ms | 15.281 ms | 28.702 ms | 772.16 ms | 45.09 |    4.26 | 2000.0000 | 1000.0000 | 1000.0000 | 308427.52 KB |   50,291.37 |
