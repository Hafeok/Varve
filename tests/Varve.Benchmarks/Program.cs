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

        if (args.Length == 1 && args[0] == "--sparql-corpus")
        {
            SparqlCorpus.Load().Print();
            return;
        }

        if (args.Length == 1 && args[0] == "--store-sizes")
        {
            StoreSizes.PrintAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Length == 2 && args[0] == "--bsbm-export")
        {
            Bsbm.Export(args[1]);
            return;
        }

        if (args.Length == 2 && args[0] == "--update-export")
        {
            UpdateWorkload.Export(args[1]);
            return;
        }

        if (args.Length == 2 && args[0] == "--export-cases")
        {
            Differential.Export(args[1]);
            return;
        }

        if (args.Length == 2 && args[0] == "--differential")
        {
            Differential.Compare(args[1]);
            return;
        }

        if (args.Length is 1 or 2 && args[0] == "--suite-time")
        {
            SuiteTime.Run(args.Length == 2 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5);
            return;
        }

        if (args.Length == 2 && args[0] == "--convert-rdfxml")
        {
            ConvertRdfXml.Run(args[1]);
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
