```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                                             | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1    | Allocated  | Alloc Ratio |
|--------------------------------------------------- |-----------:|----------:|----------:|------:|--------:|---------:|--------:|-----------:|------------:|
| &#39;Varve — 1.0 and 1.1 corpus&#39;                       |   761.1 μs |  14.95 μs |  25.79 μs |  1.00 |    0.05 |  17.5781 |       - |  303.73 KB |        1.00 |
| &#39;dotNetRDF — 1.0 and 1.1 corpus&#39;                   | 6,064.9 μs | 112.03 μs | 115.04 μs |  7.98 |    0.30 | 218.7500 |       - | 4065.59 KB |       13.39 |
| &#39;Varve — 1.0 and 1.1 corpus, parse and write back&#39; |   866.3 μs |  16.95 μs |  28.32 μs |  1.14 |    0.05 |  17.5781 |       - |  303.74 KB |        1.00 |
| &#39;Varve — 1.2 corpus&#39;                               |   577.5 μs |  10.70 μs |  14.99 μs |  0.76 |    0.03 |  18.5547 |       - |  323.59 KB |        1.07 |
| &#39;Varve — large update&#39;                             |   984.6 μs |  19.10 μs |  29.17 μs |  1.30 |    0.06 |  27.3438 | 12.6953 |  474.35 KB |        1.56 |
| &#39;dotNetRDF — large update&#39;                         | 4,440.5 μs |  77.74 μs |  68.92 μs |  5.84 |    0.21 | 140.6250 | 62.5000 | 2441.64 KB |        8.04 |
