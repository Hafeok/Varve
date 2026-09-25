// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// Every entry of every wired SPARQL syntax manifest, one test case each,
/// named by its test IRI and parsed at its suite's version.
/// </summary>
/// <remarks>
/// A positive case passes when it parses <em>and</em> what the serialiser
/// writes for it parses back to the identical tree: the corpus half of the
/// round-trip property (<c>docs/spec/sparql-algebra.md</c> §8), held to the
/// trees real queries produce rather than to generated ones alone. A negative
/// case passes when it is rejected. The ratchet in <c>eng/ratchet.cs</c> gates
/// both, by the same baseline as the RDF suites.
/// </remarks>
public class SparqlConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (SparqlManifestEntry entry in SparqlCatalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string testIri)
    {
        SparqlManifestEntry entry = SparqlCatalogue.ByIri[testIri];

        Assert.True(
            File.Exists(entry.ActionPath),
            "Manifest entry " + testIri + " names a file that is not present: " + entry.ActionPath);

        ISparqlSubject? subject = SparqlSubjects.Current;
        Assert.True(subject is not null, "no SPARQL parser registered");

        SparqlOutcome outcome = subject!.Parse(entry, asUtf16: false);

        if (!entry.MustParse)
        {
            Assert.False(outcome.Succeeded, Describe(entry) + " is ill-formed and must be rejected, but it parsed.");
            return;
        }

        Assert.True(outcome.Succeeded, Describe(entry) + " must parse, but was rejected: " + outcome.Error);

        // The corpus round trip: written back and parsed again, the same tree.
        string written = subject.Write(outcome.Tree!);
        byte[] bytes = Encoding.UTF8.GetBytes(written);
        SparqlParseOptions options = new(default, SparqlVersion.Sparql12);
        AlgebraNode again;

        if (entry.IsUpdate)
        {
            Assert.True(
                SparqlParser.TryParseUpdate(bytes, options, out Update? update, out SparqlParseError updateError),
                Describe(entry) + ": the serialised text does not parse: " + updateError + "\n" + written);
            again = update!;
        }
        else
        {
            Assert.True(
                SparqlParser.TryParseQuery(bytes, options, out Query? query, out SparqlParseError queryError),
                Describe(entry) + ": the serialised text does not parse: " + queryError + "\n" + written);
            again = query!;
        }

        Assert.True(outcome.Tree!.Equals(again), Describe(entry) + ": the serialised text parses to a different tree:\n" + written);
    }

    internal static string Describe(SparqlManifestEntry entry) =>
        entry.Suite + " " + entry.Name + (entry.Comment is null ? "" : " (" + entry.Comment + ")");
}

/// <summary>
/// Every corpus file parsed twice, as UTF-8 and as UTF-16, must give the same
/// tree or the same error kind at the same offset.
/// </summary>
/// <remarks>
/// The parser has no chunked entry point, so the chunk-boundary oracle of
/// <c>docs/testing.md</c> §2 does not apply to it; this is its analogue
/// (<c>docs/spec/sparql-grammar.md</c> §8): the same self-consistency question,
/// with the expected answer computed from the other path rather than authored.
/// </remarks>
public class SparqlEncodingAgreementTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (SparqlManifestEntry entry in SparqlCatalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_utf16_parse_agrees_with_the_utf8_parse(string testIri)
    {
        SparqlManifestEntry entry = SparqlCatalogue.ByIri[testIri];
        ISparqlSubject subject = SparqlSubjects.Current!;

        SparqlOutcome fromBytes = subject.Parse(entry, asUtf16: false);
        SparqlOutcome fromChars = subject.Parse(entry, asUtf16: true);

        Assert.Equal(fromBytes.Succeeded, fromChars.Succeeded);

        if (fromBytes.Succeeded)
        {
            Assert.True(fromBytes.Tree!.Equals(fromChars.Tree), SparqlConformanceTests.Describe(entry) + ": the two parses differ.");
        }
        else
        {
            Assert.Equal(fromBytes.Error.Kind, fromChars.Error.Kind);
            Assert.Equal(fromBytes.Error.Offset, fromChars.Error.Offset);
        }
    }
}
