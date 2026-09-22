using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Varve.Conformance.Tests;

/// <summary>
/// Every manifest entry the chunk-boundary oracle reads.
/// </summary>
/// <remarks>
/// A superset of <see cref="Catalogue"/>: it covers the suites that are
/// ratcheted and the ones whose inputs exist but whose results are not claimed
/// yet (<see cref="ConformanceSuite.NotYetRatcheted"/>). Kept separate so that
/// the ratchet's corpus stays exactly the suites the ratchet reports on, and
/// widening the oracle can never quietly widen the baseline.
/// </remarks>
internal static class OracleCatalogue
{
    internal static IReadOnlyList<ManifestEntry> Entries { get; } = ReadAll();

    internal static IReadOnlyDictionary<string, ManifestEntry> ByIri { get; } = Index(Entries);

    /// <summary>The entries of one suite, for the guard counts.</summary>
    internal static IReadOnlyList<ManifestEntry> Of(ConformanceSuite suite)
    {
        List<ManifestEntry> entries = [];

        foreach (ManifestEntry entry in Entries)
        {
            if (string.Equals(entry.Suite, suite.Id, StringComparison.Ordinal))
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

        foreach (ConformanceSuite suite in ConformanceSuite.OracleCorpus)
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
