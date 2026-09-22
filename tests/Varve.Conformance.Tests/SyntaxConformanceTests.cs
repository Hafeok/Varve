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

        ParseOutcome outcome = subject.Parse(entry.Format, entry.ActionPath, entry.ActionIri);

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

            case ExpectedOutcome.Evaluates:
                Evaluate(entry, subject, outcome);
                break;

            default:
                throw new InvalidOperationException(
                    "Unhandled expectation " + entry.Expected.ToString() + " for " + testIri + ".");
        }
    }

    /// <summary>
    /// Compares the parse with the dataset the entry's <c>mf:result</c> holds,
    /// up to a bijection of blank nodes.
    /// </summary>
    /// <remarks>
    /// The result of a Turtle test is N-Triples and of a TriG test is N-Quads,
    /// by the suites' own convention. Reading it with the line parser rather
    /// than the Turtle one is deliberate: the expected side of a comparison
    /// should go through as little of the code under test as it can.
    /// </remarks>
    private static void Evaluate(ManifestEntry entry, IParserSubject subject, ParseOutcome outcome)
    {
        Assert.True(
            outcome.Succeeded,
            Describe(entry) + " must parse, but was rejected: " + (outcome.Error ?? "(no reason given)"));

        Assert.True(
            entry.ResultPath is not null,
            Describe(entry) + " is an evaluation test with no mf:result to compare against.");

        RdfFormat resultFormat = entry.Format == RdfFormat.TriG ? RdfFormat.NQuads : RdfFormat.NTriples;
        ParseOutcome expected = subject.Parse(resultFormat, entry.ResultPath!, entry.ActionIri);

        Assert.True(
            expected.Succeeded,
            Describe(entry) + "'s expected result does not parse as " + resultFormat + ": "
            + (expected.Error ?? "(no reason given)"));

        IsomorphismResult comparison = Isomorphism.Compare(outcome.Quads, expected.Quads);

        // "Inconclusive" is not "different": the first is a limit of the check
        // and the second a finding about the parser, and reporting one as the
        // other sends someone looking for a bug that is not there.
        string verdict = comparison.Verdict == IsomorphismVerdict.Inconclusive
            ? " could not be compared with what it must produce — "
            : " produced a different dataset — ";

        Assert.True(
            comparison.IsSame,
            Describe(entry) + verdict + comparison.Reason
            + ".\n  parsed:\n" + Lines(outcome.Quads) + "  expected:\n" + Lines(expected.Quads));
    }

    private static string Lines(System.Collections.Generic.IReadOnlyList<ParsedQuad> quads)
    {
        System.Text.StringBuilder text = new();

        foreach (ParsedQuad quad in quads)
        {
            text.Append("    ").Append(quad.ToString()).Append('\n');
        }

        return text.ToString();
    }

    private static string Describe(ManifestEntry entry) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Format} test '{entry.Name}'{(entry.Comment is null ? "" : $" ({entry.Comment})")}");
}
