using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Varve.Conformance.Tests;

/// <summary>
/// Every manifest entry across every wired suite, read once.
/// </summary>
/// <remarks>
/// Test cases carry only their IRI — a string, which xUnit serialises without
/// ceremony and which reads exactly as itself in a test name and in
/// <c>baseline/passing.txt</c>. Everything else about an entry is looked up
/// here by that IRI.
/// </remarks>
internal static class Catalogue
{
    /// <summary>Entries in manifest order, suite by suite.</summary>
    internal static IReadOnlyList<ManifestEntry> Entries { get; } = ReadAll();

    /// <summary>Entries by test IRI.</summary>
    internal static IReadOnlyDictionary<string, ManifestEntry> ByIri { get; } = Index(Entries);

    /// <summary>The entries of one suite, for the run summary.</summary>
    internal static IReadOnlyList<ManifestEntry> Of(ConformanceSuite suite)
    {
        List<ManifestEntry> entries = [];

        foreach (ManifestEntry entry in Entries)
        {
            if (entry.TestIri.StartsWith(suite.BaseIri + "#", StringComparison.Ordinal))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static List<ManifestEntry> ReadAll()
    {
        if (!TestData.IsCheckedOut)
        {
            return [];
        }

        List<ManifestEntry> all = [];

        foreach (ConformanceSuite suite in ConformanceSuite.All)
        {
            all.AddRange(ManifestReader.Read(suite));
        }

        return all;
    }

    private static ReadOnlyDictionary<string, ManifestEntry> Index(IReadOnlyList<ManifestEntry> entries)
    {
        Dictionary<string, ManifestEntry> byIri = new(StringComparer.Ordinal);

        foreach (ManifestEntry entry in entries)
        {
            byIri[entry.TestIri] = entry;
        }

        return new ReadOnlyDictionary<string, ManifestEntry>(byIri);
    }
}
