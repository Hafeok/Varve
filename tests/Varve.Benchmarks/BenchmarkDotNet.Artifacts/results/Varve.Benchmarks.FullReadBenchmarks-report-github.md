```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Job-DBWHYL : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

InvocationCount=4  IterationCount=8  UnrollFactor=1  
WarmupCount=2  

```
| Method        | People | Redefinition | Mean         | Error        | StdDev      | Gen0       | Gen1       | Gen2      | Allocated  |
|-------------- |------- |------------- |-------------:|-------------:|------------:|-----------:|-----------:|----------:|-----------:|
| **CommitThenAsk** | **10000**  | **data**         |   **710.909 ms** |    **69.506 ms** |  **36.3532 ms** |  **4250.0000** |  **2000.0000** |         **-** |  **572.24 MB** |
| **CommitThenAsk** | **10000**  | **none**         |     **6.980 ms** |     **3.193 ms** |   **1.6700 ms** |          **-** |          **-** |         **-** |    **1.45 MB** |
| **CommitThenAsk** | **10000**  | **tbox**         |   **694.286 ms** |    **22.673 ms** |  **10.0671 ms** |  **4250.0000** |  **2000.0000** |         **-** |  **572.24 MB** |
| **CommitThenAsk** | **100000** | **data**         | **8,870.492 ms** |   **219.796 ms** | **114.9574 ms** | **44000.0000** | **15500.0000** | **2250.0000** | **5698.26 MB** |
| **CommitThenAsk** | **100000** | **none**         |     **6.746 ms** |     **2.197 ms** |   **0.9755 ms** |          **-** |          **-** |         **-** |    **1.45 MB** |
| **CommitThenAsk** | **100000** | **tbox**         | **9,889.311 ms** | **1,016.454 ms** | **531.6253 ms** | **44250.0000** | **15250.0000** | **2250.0000** | **5698.26 MB** |
