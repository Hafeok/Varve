```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.80GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  Job-ZJPUIJ : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=8  UnrollFactor=1  
WarmupCount=1  

```
| Method | Size | Binding | Mean        | Error      | StdDev      | Gen0     | Gen1    | Allocated  |
|------- |----- |-------- |------------:|-----------:|------------:|---------:|--------:|-----------:|
| **Commit** | **M**    | **none**    |    **857.3 μs** |   **270.6 μs** |    **96.50 μs** |        **-** |       **-** |   **30.54 KB** |
| **Commit** | **M**    | **reject**  | **11,858.7 μs** | **5,730.0 μs** | **2,996.91 μs** | **100.0000** | **50.0000** | **2471.28 KB** |
| **Commit** | **S**    | **none**    |    **506.4 μs** |   **135.7 μs** |    **70.98 μs** |        **-** |       **-** |   **35.38 KB** |
| **Commit** | **S**    | **reject**  |  **7,624.3 μs** | **3,236.3 μs** | **1,692.64 μs** | **100.0000** |       **-** | **2294.61 KB** |
