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
/// (ADR 0215, restating <c>FlatWithinABand</c>), so that a run in CI passes or
/// fails without a reader. What is judged is the working set less the
/// dataset's own and less the collector's committed-but-unused memory, which
/// is reported beside it under a ceiling of its own: from minute 30 to the
/// end every sample of it lies within ±25% of those samples' median, and the
/// median of the last ten minutes is no more than 10% over minutes 30–40.
/// Handles and <c>derived/</c> files stay within bounds the configuration
/// gives (<see cref="SoakBounds"/>). A run whose dataset is not steady from
/// minute 30, or whose collector is not the shipped one, is not judged.
/// Exit 0 holds, 1 misses, 2 cannot judge.
/// </summary>
[DesignDecision(typeof(TheSoakGateJudged.FlatWithinABand), Scope = ExceptionScope.Boundary)]
internal static class SoakJudge
{
    internal const double SteadyFrom = 30;
    internal const double Band = 0.25;
    internal const double Drift = 0.10;
    internal const double SteadyQuads = 0.05;

    /// <summary>The collector a judged run must have had: the shipped one (ADR 0110), as <see cref="Soak.GarbageCollection"/> prints it.</summary>
    [DesignDecision(typeof(TheSoakGateJudged.TheShippedCollectorOrNotJudged), Scope = ExceptionScope.Boundary)]
    internal const string ShippedCollector = "workstation, concurrent, ConserveMemory 5";

    /// <summary>
    /// The ceiling on the collector's committed memory: twice the live heap
    /// after a full collection, plus this many MB (ADR 0215).
    /// </summary>
    [DesignDecision(typeof(TheSoakGateJudged.CommittedUnderItsOwnCeiling), Scope = ExceptionScope.Boundary)]
    internal const double CommittedMarginMb = 128;

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
        string? collector = null;
        double? handlesBefore = null;

        for (int i = 0; i < lines.Count && header < 0; i++)
        {
            if (lines[i].StartsWith("minutes,", StringComparison.Ordinal))
            {
                header = i;
            }
            else if (lines[i].StartsWith("# gc ", StringComparison.Ordinal))
            {
                collector = lines[i]["# gc ".Length..];
            }
            else if (lines[i].StartsWith("# handles before the dataset ", StringComparison.Ordinal))
            {
                handlesBefore = double.Parse(lines[i]["# handles before the dataset ".Length..], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        if (header < 0)
        {
            return new Verdict(2, ["no soak table: the output has no header line"]);
        }

        // The configuration is the shipped one, or the gate measures another.
        if (collector != ShippedCollector)
        {
            return new Verdict(2, ["the run's collector is " + (collector ?? "not stated") + ", not the shipped " + ShippedCollector + " (ADR 0110)"]);
        }

        if (handlesBefore is not double before)
        {
            return new Verdict(2, ["the run does not state its handles before the dataset opened"]);
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

        int minutes, workingSet, own, quads, handles, derived, commits, managed, heap, committed, segments;

        try
        {
            (minutes, workingSet, own, quads, handles, derived) = (Column("minutes"), Column("working set MB"), Column("own MB"), Column("quads"), Column("open handles"), Column("derived files"));
            (commits, managed, heap, committed, segments) = (Column("commits"), Column("managed MB"), Column("heap MB"), Column("committed MB"), Column("log segments"));
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

        // What the collector committed and does not use: returned on its own
        // schedule, so it is not judged in the band but under its own ceiling.
        double Headroom(double[] row) => Math.Max(0, row[committed] - row[heap]);
        double Net(double[] row) => row[workingSet] - row[own] - Headroom(row);
        double median = Median([.. steady.Select(Net)]);

        foreach (double[] row in steady)
        {
            if (Math.Abs(Net(row) - median) > Band * median)
            {
                holds = false;
                said.Add("band: at minute " + Mb(row[minutes]) + ", " + Mb(Net(row)) + " MB is outside ±25% of the median " + Mb(median) + " MB");
            }
        }

        double reference = Median([.. rows.Where(r => r[minutes] >= 30 && r[minutes] < 40).Select(Net)]);
        double last = Median([.. rows.Where(r => r[minutes] > end - 10).Select(Net)]);
        double drift = (last / reference) - 1;

        // Growth is what the gate is for; a working set that shrinks holds.
        if (drift > Drift)
        {
            holds = false;
            said.Add("drift: the last ten minutes' median " + Mb(last) + " MB is " + Percent(drift) + "% over minutes 30–40's " + Mb(reference) + " MB, 10% allowed");
        }

        double headroomMax = 0;

        foreach (double[] row in rows.Where(r => r[minutes] >= SteadyFrom))
        {
            double ceiling = (2 * row[managed]) + CommittedMarginMb;
            headroomMax = Math.Max(headroomMax, Headroom(row));

            if (row[committed] > ceiling)
            {
                holds = false;
                said.Add("committed: at minute " + Mb(row[minutes]) + ", " + Mb(row[committed]) + " MB committed over the ceiling " + Mb(ceiling) + " MB, twice the live heap " + Mb(row[managed]) + " MB and " + Mb(CommittedMarginMb));
            }
        }

        int handlesOver = 0, derivedOver = 0;
        double handlesMax = 0, derivedMax = 0, handlesBound = 0, derivedBound = 0;

        foreach (double[] row in rows)
        {
            double fileBound = SoakBounds.DerivedFiles((long)row[commits], (long)row[quads]);
            double handleBound = SoakBounds.Handles((int)before, (long)row[commits], (long)row[quads], (int)row[segments]);
            handlesMax = Math.Max(handlesMax, row[handles]);
            derivedMax = Math.Max(derivedMax, row[derived]);
            handlesBound = Math.Max(handlesBound, handleBound);
            derivedBound = Math.Max(derivedBound, fileBound);
            handlesOver += row[handles] > handleBound ? 1 : 0;
            derivedOver += row[derived] > fileBound ? 1 : 0;
        }

        if (handlesOver + derivedOver > 0)
        {
            holds = false;
            said.Add("bounds: " + handlesOver + " samples over the handles bound and " + derivedOver + " over the derived-files bound (ADR 0215): at most "
                + Mb(handlesMax) + " handles against " + Mb(handlesBound) + ", " + Mb(derivedMax) + " derived files against " + Mb(derivedBound));
        }

        said.Insert(0, (holds ? "holds" : "misses") + ": " + steady.Count + " samples from minute 30 to " + Mb(end) + " under " + ShippedCollector
            + "; working set less the dataset's own and the collector's unused, median " + Mb(median) + " MB, drift " + Percent(drift)
            + "%; the collector's unused, at most " + Mb(headroomMax) + " MB; quads steady at " + settled.ToString("F0", CultureInfo.InvariantCulture)
            + "; handles ≤ " + Mb(handlesMax) + " of " + Mb(handlesBound) + ", derived files ≤ " + Mb(derivedMax) + " of " + Mb(derivedBound));
        return new Verdict(holds ? 0 : 1, said);
    }

    private static string Mb(double value) => value.ToString(value == Math.Floor(value) ? "F0" : "F1", CultureInfo.InvariantCulture);

    private static string Percent(double fraction) => (fraction * 100).ToString("+0.0;-0.0", CultureInfo.InvariantCulture);

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
