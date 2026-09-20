using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// Every entry of every wired W3C syntax manifest, one test case each, named by
/// its test IRI.
/// </summary>
/// <remarks>
/// See <c>docs/adr/0007-w3c-conformance-harness.md</c>. At milestone 1 no
/// parser is registered, so every case fails with "no parser registered". That
/// is the expected result, it is why the conformance job does not gate, and the
/// ratchet in <c>eng/ratchet.cs</c> is what gates instead.
/// </remarks>
public class SyntaxConformanceTests
{
    /// <summary>
    /// Enumerates the cases at discovery time.
    /// </summary>
    /// <remarks>
    /// Returns nothing when the submodule is not checked out, so that a clone
    /// without <c>--recurse-submodules</c> produces an understandable failure
    /// from <see cref="SubmoduleGuardTests"/> rather than a discovery crash in
    /// every case at once.
    /// </remarks>
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ManifestEntry entry in Catalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri)
            {
                TestDisplayName = entry.TestIri,
            };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string testIri)
    {
        ManifestEntry entry = Catalogue.ByIri[testIri];

        Assert.True(
            File.Exists(entry.ActionPath),
            "Manifest entry " + testIri + " names a file that is not present: " + entry.ActionPath);

        IParserSubject? subject = ParserSubjects.Current;

        if (subject is null)
        {
            Assert.Fail(
                "no parser registered — " + Describe(entry)
                + ". This is the expected result until milestone 3; see ADR 0007.");
            return;
        }

        ParseOutcome outcome = subject.Parse(entry.Format, entry.ActionPath);

        switch (entry.Expected)
        {
            case ExpectedOutcome.Parses:
                Assert.True(
                    outcome.Succeeded,
                    Describe(entry) + " must parse, but was rejected: " + (outcome.Error ?? "(no reason given)"));
                break;

            case ExpectedOutcome.IsRejected:
                Assert.False(
                    outcome.Succeeded,
                    Describe(entry) + " is ill-formed and must be rejected, but it parsed.");
                break;

            default:
                throw new InvalidOperationException(
                    "Unhandled expectation " + entry.Expected.ToString() + " for " + testIri + ".");
        }
    }

    private static string Describe(ManifestEntry entry) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Format} test '{entry.Name}'{(entry.Comment is null ? "" : $" ({entry.Comment})")}");
}
