// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>The json-ld-api entries of every kind, by suite and by IRI.</summary>
internal static class JsonLdCatalogue
{
    internal static IReadOnlyList<ManifestEntry> Entries { get; } = ReadAll();

    internal static IReadOnlyDictionary<string, ManifestEntry> ByIri { get; } = Index(Entries);

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
        if (!TestData.IsJsonLdCheckedOut)
        {
            return [];
        }

        List<ManifestEntry> all = [];

        foreach (ConformanceSuite suite in ConformanceSuite.All)
        {
            if (suite.Format == RdfFormat.JsonLd)
            {
                all.AddRange(ManifestReader.Read(suite));
            }
        }

        foreach (ConformanceSuite suite in ConformanceSuite.JsonLdApi)
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

/// <summary>
/// The json-ld-api expand and fromRdf suites (ADR 0112): each entry's result
/// is a JSON document, compared by the suite's JSON-LD object comparison —
/// members in any order, arrays as sets except under <c>@list</c>, language
/// tags without regard to case. The toRdf suite is a dataset comparison and
/// runs with the syntax tests.
/// </summary>
public class JsonLdApiConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> ExpandCases() => Cases(JsonLdOperation.Expand);

    public static IEnumerable<TheoryDataRow<string>> FromRdfCases() => Cases(JsonLdOperation.FromRdf);

    private static IEnumerable<TheoryDataRow<string>> Cases(JsonLdOperation operation)
    {
        if (!TestData.IsJsonLdCheckedOut)
        {
            yield break;
        }

        foreach (ManifestEntry entry in JsonLdCatalogue.Entries)
        {
            if (entry.JsonLd!.Operation == operation)
            {
                yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExpandCases))]
    public void Expand(string testIri)
    {
        ManifestEntry entry = JsonLdCatalogue.ByIri[testIri];
        Assert.True(File.Exists(entry.ActionPath), "Manifest entry " + testIri + " names a file that is not present: " + entry.ActionPath);

        ArrayBufferWriter output = new();
        JsonLdResult result = JsonLdExpander.Expand(File.ReadAllBytes(entry.ActionPath), output, VarveParserSubject.JsonLdOptionsFor(entry.ActionIri, entry.JsonLd));

        if (entry.Expected == ExpectedOutcome.IsRejected)
        {
            Assert.False(result.Succeeded, Describe(entry) + " must fail with '" + entry.JsonLd!.ExpectErrorCode + "' but expanded to:\n" + Encoding.UTF8.GetString(output.Written));
            Assert.True(
                result.Error!.ToString().StartsWith(entry.JsonLd!.ExpectErrorCode + ":", StringComparison.Ordinal),
                Describe(entry) + " must fail with '" + entry.JsonLd.ExpectErrorCode + "' but failed with: " + result.Error);
            return;
        }

        Assert.True(result.Succeeded, Describe(entry) + " must expand, but failed: " + result.Error);
        Assert.True(entry.ResultPath is not null, Describe(entry) + " has no expected document.");
        CompareJson(entry, output.Written.ToArray(), File.ReadAllBytes(entry.ResultPath!));
    }

    [Theory]
    [MemberData(nameof(FromRdfCases))]
    public void FromRdf(string testIri)
    {
        ManifestEntry entry = JsonLdCatalogue.ByIri[testIri];
        Assert.True(File.Exists(entry.ActionPath), "Manifest entry " + testIri + " names a file that is not present: " + entry.ActionPath);

        ArrayBufferWriter output = new();
        JsonLdWriteOptions options = new()
        {
            UseNativeTypes = entry.JsonLd!.UseNativeTypes,
            UseRdfType = entry.JsonLd.UseRdfType,
            RdfDirection = entry.JsonLd.Direction,
        };

        InvalidOperationException? refused = null;

        try
        {
            using JsonLdWriter writer = new(output, in options);
            ParseResult parsed = NQuadsParser.Parse(
                File.ReadAllBytes(entry.ActionPath),
                (in QuadView quad) => writer.Write(in quad),
                new ParseOptions { Syntax = RdfSyntax.NQuads, Version = RdfVersion.Rdf11 });
            Assert.True(parsed.Succeeded, Describe(entry) + "'s input is not N-Quads: " + parsed.FirstError);
        }
        catch (InvalidOperationException exception)
        {
            refused = exception;
        }

        if (entry.Expected == ExpectedOutcome.IsRejected)
        {
            Assert.True(refused is not null, Describe(entry) + " must be refused with '" + entry.JsonLd.ExpectErrorCode + "' but was written:\n" + Encoding.UTF8.GetString(output.Written));
            Assert.True(
                refused!.Message.StartsWith(entry.JsonLd.ExpectErrorCode + ":", StringComparison.Ordinal),
                Describe(entry) + " must be refused with '" + entry.JsonLd.ExpectErrorCode + "' but was refused with: " + refused.Message);
            return;
        }

        Assert.True(refused is null, Describe(entry) + " must be written, but was refused: " + refused?.Message);
        Assert.True(entry.ResultPath is not null, Describe(entry) + " has no expected document.");
        CompareJson(entry, output.Written.ToArray(), File.ReadAllBytes(entry.ResultPath!));
    }

    private static void CompareJson(ManifestEntry entry, byte[] actual, byte[] expected)
    {
        using JsonDocument actualDocument = JsonDocument.Parse(actual);
        using JsonDocument expectedDocument = JsonDocument.Parse(expected);

        Assert.True(
            Same(actualDocument.RootElement, expectedDocument.RootElement, inList: false),
            Describe(entry) + " produced a different document.\n  produced:\n" + Encoding.UTF8.GetString(actual) + "\n  expected:\n" + Encoding.UTF8.GetString(expected));
    }

    /// <summary>The suite's JSON-LD object comparison (json-ld-api tests/README.md).</summary>
    internal static bool Same(JsonElement left, JsonElement right, bool inList)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
            {
                int leftCount = 0;

                foreach (JsonProperty property in left.EnumerateObject())
                {
                    leftCount++;

                    if (!right.TryGetProperty(property.Name, out JsonElement other))
                    {
                        return false;
                    }

                    bool language = string.Equals(property.Name, "@language", StringComparison.Ordinal);
                    bool list = string.Equals(property.Name, "@list", StringComparison.Ordinal);

                    if (language && property.Value.ValueKind == JsonValueKind.String && other.ValueKind == JsonValueKind.String)
                    {
                        if (!string.Equals(property.Value.GetString(), other.GetString(), StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!Same(property.Value, other, list))
                    {
                        return false;
                    }
                }

                int rightCount = 0;

                foreach (JsonProperty _ in right.EnumerateObject())
                {
                    rightCount++;
                }

                return leftCount == rightCount;
            }

            case JsonValueKind.Array:
            {
                if (left.GetArrayLength() != right.GetArrayLength())
                {
                    return false;
                }

                if (inList)
                {
                    JsonElement.ArrayEnumerator a = left.EnumerateArray();
                    JsonElement.ArrayEnumerator b = right.EnumerateArray();

                    while (a.MoveNext() && b.MoveNext())
                    {
                        if (!Same(a.Current, b.Current, inList: false))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                // As sets: each item on the left matches an unmatched item on the right.
                List<JsonElement> remaining = [.. right.EnumerateArray()];

                foreach (JsonElement item in left.EnumerateArray())
                {
                    int match = remaining.FindIndex(candidate => Same(item, candidate, inList: false));

                    if (match < 0)
                    {
                        return false;
                    }

                    remaining.RemoveAt(match);
                }

                return remaining.Count == 0;
            }

            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return left.GetRawText() == right.GetRawText() || (left.TryGetDouble(out double x) && right.TryGetDouble(out double y) && x == y);
            default:
                return true;
        }
    }

    private static string Describe(ManifestEntry entry) =>
        "JSON-LD " + entry.JsonLd!.Operation + " test '" + entry.Name + "'" + (entry.Comment is null ? "" : " (" + entry.Comment + ")");
}
