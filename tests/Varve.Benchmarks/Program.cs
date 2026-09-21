using BenchmarkDotNet.Running;

namespace Varve.Benchmarks;

/// <summary>
/// The benchmark host.
/// </summary>
/// <remarks>
/// Not in CI, and not a gate. ADR 0027: a benchmark that gates turns a noisy
/// measurement into a flaky build, and a shared runner is the noisiest machine
/// there is. Numbers are taken deliberately, on a stated machine, and reported
/// with the machine.
/// </remarks>
internal static class Program
{
    internal static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
