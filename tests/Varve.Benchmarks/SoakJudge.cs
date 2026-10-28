// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--soak-judge &lt;output&gt;</c>: the soak gate applied to a soak's output
/// (ADR 0215 point 3, restating <c>FlatWithinABand</c>), so that a run in CI
/// passes or fails without a reader. From minute 30 to the end, every sample
/// of the working set less the dataset's own lies within ±25% of those
/// samples' median; the median of the last ten minutes is within 10% of
/// minutes 30–40; handles and <c>derived/</c> files stay within their bounds.
/// A run whose dataset is not steady from minute 30 is not judged, since the
/// gate would measure the dataset (ADR 0215 point 1). Exit 0 holds, 1 misses,
/// 2 cannot judge.
/// </summary>
[DesignDecision(typeof(TheSoakGateJudged.FlatWithinABand), Scope = ExceptionScope.Boundary)]
internal static class SoakJudge
{
    internal const double SteadyFrom = 30;
    internal const double Band = 0.25;
    internal const double Drift = 0.10;
    internal const double SteadyQuads = 0.05;
    internal const int MaxHandles = 200;
    internal const int MaxDerivedFiles = 64;

    [DesignDecision(typeof(TheSoakGateJudged.JudgedByAScript), Scope = ExceptionScope.Boundary)]
    internal static int Run(string path, TextWriter output)
    {
        Verdict verdict = Judge(File.ReadAllLines(path));

        foreach (string line in verdict.Lines)
        {
            output.WriteLine(line);
        }

        return verdict.Exit;
    }

    /// <summary>
    /// <c>--soak-judge-fixtures &lt;directory&gt;</c>: the judge's failure path,
    /// proven on every run of the pipeline. Each fixture's name says what the
    /// judge must say of it: <c>holds-</c> exits 0, <c>misses-</c> 1 and
    /// <c>cannot-</c> 2 (tests/fixtures/soak-judge/README.md).
    /// </summary>
    internal static int RunFixtures(string directory, TextWriter output)
    {
        int wrong = 0;
        string[] fixtures = [.. Directory.GetFiles(directory, "*.csv").Order(StringComparer.Ordinal)];

        foreach (string fixture in fixtures)
        {
            string name = Path.GetFileName(fixture);
            int expected = name.StartsWith("holds-", StringComparison.Ordinal) ? 0 : name.StartsWith("misses-", StringComparison.Ordinal) ? 1 : name.StartsWith("cannot-", StringComparison.Ordinal) ? 2 : -1;
            Verdict verdict = Judge(File.ReadAllLines(fixture));
            bool right = verdict.Exit == expected;
            wrong += right ? 0 : 1;
            output.WriteLine((right ? "ok   " : "FAIL ") + name + ": exit " + verdict.Exit + ", expected " + expected + "; " + verdict.Lines[0]);
        }

        output.WriteLine(fixtures.Length + " fixtures, " + wrong + " judged wrong");
        return fixtures.Length > 0 && wrong == 0 ? 0 : 1;
    }

    internal sealed record Verdict(int Exit, IReadOnlyList<string> Lines);

    internal static Verdict Judge(IReadOnlyList<string> lines)
    {
        List<string> said = [];
        int header = -1;

        for (int i = 0; i < lines.Count && header < 0; i++)
        {
            if (lines[i].StartsWith("minutes,", StringComparison.Ordinal))
            {
                header = i;
            }
        }

        if (header < 0)
        {
            return new Verdict(2, ["no soak table: the output has no header line"]);
        }

        string[] names = lines[header].Split(',');
        int Column(string name) => Array.IndexOf(names, name) is int at and >= 0 ? at : throw new InvalidDataException("The soak table has no column '" + name + "'.");
        List<double[]> rows = [];

        for (int i = header + 1; i < lines.Count; i++)
        {
            string[] cells = lines[i].Split(',');

            if (cells.Length != names.Length || !double.TryParse(cells[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            rows.Add([.. cells.Select(c => double.Parse(c, NumberStyles.Float, CultureInfo.InvariantCulture))]);
        }

        int minutes, workingSet, own, quads, handles, derived;

        try
        {
            (minutes, workingSet, own, quads, handles, derived) = (Column("minutes"), Column("working set MB"), Column("own MB"), Column("quads"), Column("open handles"), Column("derived files"));
        }
        catch (InvalidDataException missing)
        {
            return new Verdict(2, [missing.Message]);
        }

        List<double[]> steady = [.. rows.Where(r => r[minutes] >= SteadyFrom)];
        double end = rows.Count == 0 ? 0 : rows[^1][minutes];

        if (end < SteadyFrom + 20)
        {
            return new Verdict(2, ["the run is " + end.ToString("F1", CultureInfo.InvariantCulture) + " minutes; the gate needs its steady state from minute 30 and twenty minutes more"]);
        }

        // The dataset must be steady, or the gate measures it.
        double[] counts = [.. steady.Select(r => r[quads]).Where(q => q > 0)];
        double settled = Median(counts);

        if (counts.Length == 0 || counts.Any(q => Math.Abs(q - settled) > SteadyQuads * settled))
        {
            return new Verdict(2, ["the dataset is not steady from minute 30: quads " + (counts.Length == 0 ? "none" : counts.Min().ToString("F0", CultureInfo.InvariantCulture) + " to " + counts.Max().ToString("F0", CultureInfo.InvariantCulture))
                + " against " + (SteadyQuads * 100).ToString("F0", CultureInfo.InvariantCulture) + "% of their median; choose a smaller vocabulary (ADR 0215 point 1)"]);
        }

        bool holds = true;
        double Net(double[] row) => row[workingSet] - row[own];
        double median = Median([.. steady.Select(Net)]);

        foreach (double[] row in steady)
        {
            if (Math.Abs(Net(row) - median) > Band * median)
            {
                holds = false;
                said.Add("band: at minute " + row[minutes].ToString("F1", CultureInfo.InvariantCulture) + ", " + Net(row).ToString("F1", CultureInfo.InvariantCulture)
                    + " MB is outside ±25% of the median " + median.ToString("F1", CultureInfo.InvariantCulture) + " MB");
            }
        }

        double reference = Median([.. rows.Where(r => r[minutes] >= 30 && r[minutes] < 40).Select(Net)]);
        double last = Median([.. rows.Where(r => r[minutes] > end - 10).Select(Net)]);
        double drift = (last / reference) - 1;

        if (Math.Abs(drift) > Drift)
        {
            holds = false;
            said.Add("drift: the last ten minutes' median " + last.ToString("F1", CultureInfo.InvariantCulture) + " MB is " + (drift * 100).ToString("+0.0;-0.0", CultureInfo.InvariantCulture)
                + "% from minutes 30–40's " + reference.ToString("F1", CultureInfo.InvariantCulture) + " MB, 10% allowed");
        }

        double maxHandles = rows.Max(r => r[handles]);
        double maxDerived = rows.Max(r => r[derived]);

        if (maxHandles > MaxHandles || maxDerived > MaxDerivedFiles)
        {
            holds = false;
            said.Add("bounds: at most " + maxHandles.ToString("F0", CultureInfo.InvariantCulture) + " handles (" + MaxHandles + " allowed) and "
                + maxDerived.ToString("F0", CultureInfo.InvariantCulture) + " derived files (" + MaxDerivedFiles + " allowed)");
        }

        said.Insert(0, (holds ? "holds" : "misses") + ": " + steady.Count + " samples from minute 30 to " + end.ToString("F1", CultureInfo.InvariantCulture)
            + "; working set less the dataset's own, median " + median.ToString("F1", CultureInfo.InvariantCulture) + " MB, drift " + (drift * 100).ToString("+0.0;-0.0", CultureInfo.InvariantCulture)
            + "%; quads steady at " + settled.ToString("F0", CultureInfo.InvariantCulture) + "; handles ≤ " + maxHandles.ToString("F0", CultureInfo.InvariantCulture)
            + ", derived files ≤ " + maxDerived.ToString("F0", CultureInfo.InvariantCulture));
        return new Verdict(holds ? 0 : 1, said);
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        double[] sorted = [.. values.Order()];
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
