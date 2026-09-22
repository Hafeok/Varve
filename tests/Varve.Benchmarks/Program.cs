// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
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
    internal static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--datasets")
        {
            PrintDatasets();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }

    /// <summary>
    /// Prints what each dataset is, so <c>README.md</c> states measured numbers
    /// rather than remembered ones. A document's byte count is what separates a
    /// quads-per-second figure for Turtle from one for N-Quads: Turtle says the
    /// same thing in far fewer bytes, so the two are not comparable per quad.
    /// </summary>
    private static void PrintDatasets()
    {
        Report("N-Quads", Dataset.Utf8.Length, Dataset.Quads);
        Report("Turtle", TurtleDataset.Utf8.Length, TurtleDataset.Quads);

        static void Report(string name, int bytes, int quads) =>
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: {bytes:N0} bytes, {quads:N0} quads, {(double)bytes / quads:N1} bytes/quad"));
    }
}
