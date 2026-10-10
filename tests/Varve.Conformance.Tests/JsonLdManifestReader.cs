// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Varve.JsonLd;

namespace Varve.Conformance.Tests;

/// <summary>
/// Reads a json-ld-api manifest (<c>toRdf-manifest.jsonld</c> and its
/// siblings), which is JSON-LD but a fixed shape of it: a <c>sequence</c> of
/// entries with <c>@id</c>, <c>@type</c>, <c>name</c>, <c>purpose</c>,
/// <c>input</c>, <c>expect</c> or <c>expectErrorCode</c>, and an
/// <c>option</c> map. Read as JSON, so that the suite does not depend on
/// the processor it tests.
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of entry are excluded by rule rather than exempted one by
/// one (ADR 0123, <c>ProcessingModeIsOnePointOne</c>): those whose
/// <c>specVersion</c> is <c>json-ld-1.0</c> (they test behaviour JSON-LD 1.1
/// changed), those whose <c>processingMode</c> is <c>json-ld-1.0</c> (they
/// test the 1.0 mode this processor does not have), and those with
/// <c>produceGeneralizedRdf</c> (Varve emits RDF, not generalized RDF).
/// <see cref="Excluded"/> counts them per suite, and <c>json-ld.md</c> §1
/// states the figures.
/// </para>
/// </remarks>
internal static class JsonLdManifestReader
{
    private static readonly Dictionary<string, int> ExcludedCounts = new(StringComparer.Ordinal);

    /// <summary>How many entries of a suite the rule excluded, after it has been read.</summary>
    internal static int Excluded(string suiteId) => ExcludedCounts.GetValueOrDefault(suiteId);

    internal static IReadOnlyList<ManifestEntry> Read(ConformanceSuite suite, JsonLdOperation operation)
    {
        List<ManifestEntry> entries = [];
        int excluded = 0;

        if (!File.Exists(suite.ManifestFile))
        {
            return entries;
        }

        string testsDirectory = Path.GetDirectoryName(suite.ManifestFile)!;

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(suite.ManifestFile));

        foreach (JsonElement entry in manifest.RootElement.GetProperty("sequence").EnumerateArray())
        {
            string id = entry.GetProperty("@id").GetString()!;
            string name = entry.GetProperty("name").GetString()!;
            string purpose = entry.TryGetProperty("purpose", out JsonElement purposeElement) ? purposeElement.GetString()! : "";
            string input = entry.GetProperty("input").GetString()!;
            bool negative = false;
            bool positiveSyntax = false;

            foreach (JsonElement type in entry.GetProperty("@type").EnumerateArray())
            {
                string typeName = type.GetString()!;
                negative |= string.Equals(typeName, "jld:NegativeEvaluationTest", StringComparison.Ordinal);
                positiveSyntax |= string.Equals(typeName, "jld:PositiveSyntaxTest", StringComparison.Ordinal);
            }

            string? baseOption = null;
            string? expandContext = null;
            RdfDirection direction = RdfDirection.None;
            bool useNativeTypes = false;
            bool useRdfType = false;
            bool skip = false;

            if (entry.TryGetProperty("option", out JsonElement option))
            {
                foreach (JsonProperty property in option.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "specVersion":
                            skip |= string.Equals(property.Value.GetString(), "json-ld-1.0", StringComparison.Ordinal);
                            break;
                        case "processingMode":
                            skip |= string.Equals(property.Value.GetString(), "json-ld-1.0", StringComparison.Ordinal);
                            break;
                        case "produceGeneralizedRdf":
                            skip |= property.Value.ValueKind == JsonValueKind.True;
                            break;
                        case "base":
                            baseOption = property.Value.GetString();
                            break;
                        case "expandContext":
                            expandContext = property.Value.GetString();
                            break;
                        case "rdfDirection":
                            direction = property.Value.GetString() switch
                            {
                                "i18n-datatype" => RdfDirection.I18nDatatype,
                                "compound-literal" => RdfDirection.CompoundLiteral,
                                _ => RdfDirection.None,
                            };
                            break;
                        case "useNativeTypes":
                            useNativeTypes = property.Value.ValueKind == JsonValueKind.True;
                            break;
                        case "useRdfType":
                            useRdfType = property.Value.ValueKind == JsonValueKind.True;
                            break;
                        default:
                            // useJCS (the only canonical form Varve writes),
                            // normative, and anything the suite adds later.
                            break;
                    }
                }
            }

            if (skip)
            {
                excluded++;
                continue;
            }

            string inputIri = ConformanceSuite.JsonLdPublishedRoot + input;
            string inputPath = Path.Combine(testsDirectory, input.Replace('/', Path.DirectorySeparatorChar));
            string? expectPath = entry.TryGetProperty("expect", out JsonElement expect)
                ? Path.Combine(testsDirectory, expect.GetString()!.Replace('/', Path.DirectorySeparatorChar))
                : null;
            string? errorCode = entry.TryGetProperty("expectErrorCode", out JsonElement code) ? code.GetString() : null;

            JsonLdCase options = new(
                operation,
                baseOption ?? inputIri,
                expandContext is null ? null : Path.Combine(testsDirectory, expandContext.Replace('/', Path.DirectorySeparatorChar)),
                expandContext is null ? null : ConformanceSuite.JsonLdPublishedRoot + expandContext,
                direction,
                useNativeTypes,
                useRdfType,
                errorCode);

            ExpectedOutcome expected = negative ? ExpectedOutcome.IsRejected
                : positiveSyntax || expectPath is null ? ExpectedOutcome.Parses
                : ExpectedOutcome.Evaluates;

            entries.Add(new ManifestEntry(
                suite.BaseIri + id,
                suite.Id,
                name,
                purpose.Length == 0 ? null : purpose,
                inputPath,
                inputIri,
                RdfFormat.JsonLd,
                expected,
                expectPath,
                options));
        }

        ExcludedCounts[suite.Id] = excluded;
        return entries;
    }
}
