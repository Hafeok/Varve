// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests.Formats;

/// <summary>
/// The fixtures under <c>testdata/formats/</c> (ADR 0216): where they are, their
/// sums, the store workload their generators ran, and the expectations those
/// generators wrote, rendered as the generators render them.
/// </summary>
internal static class FormatFixtures
{
    /// <summary>The store workload's clock and dataset, as every store and object generator fixes them.</summary>
    internal static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    internal static readonly DatasetId Id = new(new Guid("6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d"));

    internal const int Commits = 120;

    internal const int Every = 40;

    /// <summary>A fixture's directory, its sums checked before a byte of it is read.</summary>
    internal static string Fixture(string name)
    {
        string fixture = Path.Combine(Root(), "testdata", "formats", name);

        foreach (string line in File.ReadAllLines(Path.Combine(fixture, "SHA256SUMS")).Where(l => l.Length > 0))
        {
            string file = line[66..];
            Assert.True(
                line[..64] == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture, file)))),
                name + "/" + file + " does not match its SHA256SUMS line.");
        }

        return fixture;
    }

    internal static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }

    internal static void Copy(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>
    /// The dataset at every position from 0 to the head, from the operations
    /// the generator issued (<c>expectations/operations.tsv</c>): set semantics,
    /// so an assert of a quad present and a retraction of one absent change
    /// nothing.
    /// </summary>
    internal static List<SortedSet<string>> States(string fixture)
    {
        SortedSet<string> current = new(StringComparer.Ordinal);
        List<SortedSet<string>> states = [new(current, StringComparer.Ordinal)];

        foreach (string line in File.ReadAllLines(Path.Combine(fixture, "expectations", "operations.tsv")).Where(l => l.Length > 0))
        {
            string[] parts = line.Split('\t');
            int position = int.Parse(parts[0], CultureInfo.InvariantCulture);

            while (states.Count <= position)
            {
                states.Add(new(current, StringComparer.Ordinal));
            }

            _ = parts[1] == "+" ? current.Add(parts[2]) : current.Remove(parts[2]);
            states[position] = new(current, StringComparer.Ordinal);
        }

        return states;
    }

    /// <summary>The dataset at every position from 0 to the head, as current code reads it.</summary>
    internal static async Task<List<SortedSet<string>>> ViewsAsync(Dataset dataset)
    {
        List<SortedSet<string>> views = [];

        for (long q = 0; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            SortedSet<string> quads = new(StringComparer.Ordinal);

            foreach (Quad quad in T.All(view))
            {
                quads.Add(Render(view, quad.Subject) + " " + Render(view, quad.Predicate) + " " + Render(view, quad.Object)
                    + (quad.Graph.IsNone ? string.Empty : " " + Render(view, quad.Graph)) + " .");
            }

            views.Add(quads);
        }

        return views;
    }

    // As the generators write a quad: an IRI in angle brackets, a plain
    // literal in quotes; the workload has no other term.
    private static string Render(DatasetView source, TermHandle handle)
    {
        Assert.True(source.TryExternalise(handle, out RdfTerm? term));
        string lexical = Encoding.UTF8.GetString(term.Lexical);
        return term.Kind == RdfTermKind.Iri ? "<" + lexical + ">" : "\"" + lexical + "\"";
    }

    /// <summary>The store workload, as every store and object generator runs it, against current code.</summary>
    internal static async Task WriteWorkloadAsync(IStorage storage, ManualClock clock)
    {
        DatasetOptions options = new() { Clock = clock, Maintenance = MaintenanceMode.Off, MemtableLimit = new QuadCount(64) };
        await using Dataset dataset = await Dataset.CreateAsync(storage, Id, options, T.Ct);

        for (int i = 1; i <= Commits; i++)
        {
            clock.Now = clock.Now.AddSeconds(7);
            string n = i.ToString(CultureInfo.InvariantCulture);
            CommitRequestBuilder request = new CommitRequestBuilder()
                .Assert(Iri("s" + (i % 17)), Iri("p" + (i % 3)), Literal("v" + n), Iri("g" + (i % 3)))
                .Assert(Iri("s" + (i % 11)), Iri("q"), Iri("o" + (i % 5)));

            if (i > 4)
            {
                string m = (i - 4).ToString(CultureInfo.InvariantCulture);
                request.Retract(Iri("s" + ((i - 4) % 17)), Iri("p" + ((i - 4) % 3)), Literal("v" + m), Iri("g" + ((i - 4) % 3)));
            }

            await dataset.CommitAsync(request, T.Ct);
            await dataset.MaintainAsync(T.Ct);

            if (i % Every == 0)
            {
                await dataset.CheckpointAsync(new Position(i), T.Ct);
            }
        }
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    private static RdfTerm Literal(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));

    /// <summary>Every file under a directory, by its relative path, with its bytes; the dataset's runtime files left out.</summary>
    internal static SortedDictionary<string, byte[]> Files(string directory) =>
        new(Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is not ("LOCK" or "LOCK.owner" or ".gitignore"))
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllBytes), StringComparer.Ordinal);
}
