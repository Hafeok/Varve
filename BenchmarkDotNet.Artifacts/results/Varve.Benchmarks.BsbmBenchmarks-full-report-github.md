The full run. Its Q10 row is the first formulation, which returned no rows in any engine; the widened Q10 is in Varve.Benchmarks.BsbmBenchmarks-report-github.md, run alone afterwards with VARVE_BSBM_QUERIES.

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-YFEFPZ : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

```

| Method       | Query                | Mean             | Error          | StdDev         | Ratio  | RatioSD | Gen0      | Gen1      | Allocated   | Alloc Ratio |
|------------- |--------------------- |-----------------:|---------------:|---------------:|-------:|--------:|----------:|----------:|------------:|------------:|
| DotNetRdf    | Q1 ty(...)meric [23] |      1,238.06 us |      74.462 us |      49.252 us |   1.00 |    0.05 |   39.0625 |    7.8125 |   716.62 KB |        1.00 |
| VarveStore   | Q1 ty(...)meric [23] |        249.65 us |       8.776 us |       5.805 us |   0.20 |    0.01 |    4.8828 |         - |    85.59 KB |        0.12 |
| VarveDataset | Q1 ty(...)meric [23] |    275,987.99 us |  15,895.058 us |   9,458.894 us | 223.24 |   11.27 |         - |         - |    69.78 KB |        0.10 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q10 cheap offers     |        295.32 us |      26.345 us |      17.425 us |   1.00 |    0.08 |    5.3711 |         - |    90.26 KB |        1.00 |
| VarveStore   | Q10 cheap offers     |         20.32 us |       0.462 us |       0.275 us |   0.07 |    0.00 |    0.9155 |         - |     15.5 KB |        0.17 |
| VarveDataset | Q10 cheap offers     |     27,428.33 us |     900.825 us |     595.840 us |  93.19 |    5.96 |         - |         - |    14.19 KB |        0.16 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q2 product details   |        648.28 us |      53.317 us |      35.266 us |   1.00 |    0.07 |   14.6484 |    0.9766 |    256.1 KB |        1.00 |
| VarveStore   | Q2 product details   |         23.57 us |       1.377 us |       0.911 us |   0.04 |    0.00 |    1.1902 |         - |    20.42 KB |        0.08 |
| VarveDataset | Q2 product details   |     27,523.89 us |   1,227.491 us |     642.002 us |  42.57 |    2.35 |         - |         - |    19.11 KB |        0.07 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q3 ne(...)IONAL [23] |      2,044.99 us |     161.754 us |     106.990 us |   1.00 |    0.07 |   46.8750 |         - |   820.08 KB |        1.00 |
| VarveStore   | Q3 ne(...)IONAL [23] |        308.20 us |      23.465 us |      15.521 us |   0.15 |    0.01 |    6.8359 |         - |    120.2 KB |        0.15 |
| VarveDataset | Q3 ne(...)IONAL [23] |    339,370.62 us |  21,314.565 us |  12,683.956 us | 166.35 |    9.98 |         - |         - |   100.68 KB |        0.12 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q4 union             |      4,310.04 us |     555.254 us |     367.266 us |   1.01 |    0.11 |  125.0000 |   15.6250 |   2334.6 KB |        1.00 |
| VarveStore   | Q4 union             |        495.84 us |      12.171 us |       7.243 us |   0.12 |    0.01 |    9.7656 |         - |   175.14 KB |        0.08 |
| VarveDataset | Q4 union             |    555,773.05 us |  26,501.655 us |  17,529.204 us | 129.74 |   10.59 |         - |         - |   142.42 KB |        0.06 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q5 similar products  |     79,480.19 us |   9,204.208 us |   6,088.014 us |   1.01 |    0.11 | 1000.0000 |  375.0000 | 19057.66 KB |        1.00 |
| VarveStore   | Q5 similar products  |      6,655.67 us |     359.192 us |     237.583 us |   0.08 |    0.01 |  101.5625 |    7.8125 |  1775.37 KB |        0.09 |
| VarveDataset | Q5 similar products  |  7,212,054.57 us | 237,672.852 us | 141,435.302 us |  91.25 |    7.24 |         - |         - |  1361.13 KB |        0.07 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q7 of(...)views [21] |        786.32 us |      55.798 us |      29.183 us |   1.00 |    0.05 |   15.6250 |         - |   293.03 KB |        1.00 |
| VarveStore   | Q7 of(...)views [21] |         42.91 us |       2.546 us |       1.515 us |   0.05 |    0.00 |    2.0142 |         - |    34.95 KB |        0.12 |
| VarveDataset | Q7 of(...)views [21] |     64,507.04 us |   1,771.489 us |   1,171.730 us |  82.14 |    3.36 |         - |         - |    31.66 KB |        0.11 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Q8 recent reviews    |        439.57 us |      54.609 us |      36.120 us |   1.01 |    0.11 |   11.7188 |    0.4883 |   196.87 KB |        1.00 |
| VarveStore   | Q8 recent reviews    |         45.33 us |       2.672 us |       1.767 us |   0.10 |    0.01 |    1.4038 |         - |    24.38 KB |        0.12 |
| VarveDataset | Q8 recent reviews    |     39,768.03 us |   2,118.524 us |   1,401.273 us |  91.03 |    7.90 |         - |         - |    22.41 KB |        0.11 |
|              |                      |                  |                |                |        |         |           |           |             |             |
| DotNetRdf    | Qa ra(...)ducer [22] |    142,437.18 us |  16,280.974 us |  10,768.856 us |   1.01 |    0.10 | 2000.0000 | 1000.0000 | 40778.41 KB |        1.00 |
| VarveStore   | Qa ra(...)ducer [22] |      9,362.41 us |     987.926 us |     653.452 us |   0.07 |    0.01 |  187.5000 |   15.6250 |  3324.19 KB |        0.08 |
| VarveDataset | Qa ra(...)ducer [22] | 11,601,902.31 us | 249,036.472 us | 148,197.610 us |  81.87 |    5.95 |         - |         - |  2674.45 KB |        0.07 |
