using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// A parse must not depend on how the input arrived.
/// </summary>
/// <remarks>
/// <para>
/// For every manifest input of every format, this parses the file whole and
/// then parses it again split into two segments at each byte offset in turn,
/// and requires the same answer every time: the same quads when it is
/// accepted, the same error kind and the same position when it is not.
/// </para>
/// <para>
/// It is an <em>oracle</em> and not a conformance test. It never asks whether
/// the answer agrees with the specification — the suites do that — only whether
/// the parser agrees with itself. That makes it cheap to be right about: the
/// expected value is computed, not written down, so a corpus can be pointed at
/// it without anyone reading the files.
/// </para>
/// <para>
/// This is the test that would have caught every chunked-path defect found in
/// the Turtle reader by hand: a keyword decided on too few bytes, a number or a
/// language tag ending where the buffer did, an escape near the end mistaken
/// for truncation, and a blank node counter that was not rewound. Each of them
/// is exactly "the split changed the answer". It stays for every later format,
/// and a new one is wired in by adding its suite rather than by remembering to
/// think about chunking.
/// </para>
/// <para>
/// Negative cases are included deliberately: a parser that rejects the right
/// documents for the wrong reason, or names the wrong place, is reporting
/// something a user will act on. The position is the part most likely to go
/// quietly wrong under chunking, because it is the one thing a buffer-relative
/// computation gets right on a whole document and wrong on a fragment.
/// </para>
/// </remarks>
public class ChunkBoundaryOracleTests
{
    /// <summary>
    /// Inputs above this many bytes are split at a stride rather than at every
    /// offset.
    /// </summary>
    /// <remarks>
    /// The work is quadratic in file size — every offset reparses the whole
    /// file — and a handful of suite inputs are tens of kilobytes. The stride
    /// keeps the suite's whole running time in seconds while still cutting
    /// those files in hundreds of places, including, by the
    /// <see cref="Offsets"/> construction, every offset near each end where the
    /// interesting boundaries are.
    /// </remarks>
    private const int EveryOffsetUpTo = 4096;

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ManifestEntry entry in OracleCatalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri)
            {
                TestDisplayName = entry.TestIri,
            };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void the_answer_does_not_depend_on_where_the_input_was_split(string testIri)
    {
        ManifestEntry entry = OracleCatalogue.ByIri[testIri];
        IParserSubject subject = ParserSubjects.Current
            ?? throw new InvalidOperationException("no parser registered");

        ParseOutcome whole = subject.Parse(entry.Format, entry.ActionPath);
        int length = (int)new FileInfo(entry.ActionPath).Length;
        List<string> disagreements = [];

        foreach (int at in Offsets(length))
        {
            ParseOutcome split = subject.ParseSplit(entry.Format, entry.ActionPath, at);

            if (!Same(whole, split))
            {
                disagreements.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  split at {at}/{length}: {Describe(split)}"));

                if (disagreements.Count == 5)
                {
                    break;
                }
            }
        }

        if (disagreements.Count > 0)
        {
            StringBuilder message = new();
            message.Append(entry.Format).Append(' ').Append(Path.GetFileName(entry.ActionPath))
                .Append("\n  whole:          ").Append(Describe(whole)).Append('\n')
                .AppendJoin('\n', disagreements);

            Assert.Fail(message.ToString());
        }
    }

    /// <summary>
    /// Every split point for a small file; for a large one, every offset in the
    /// first and last kilobyte and a stride through the middle.
    /// </summary>
    private static IEnumerable<int> Offsets(int length)
    {
        if (length <= EveryOffsetUpTo)
        {
            for (int at = 0; at <= length; at++)
            {
                yield return at;
            }

            yield break;
        }

        const int edge = 1024;
        int stride = Math.Max(1, length / 512);

        for (int at = 0; at <= edge; at++)
        {
            yield return at;
        }

        for (int at = edge + 1; at < length - edge; at += stride)
        {
            yield return at;
        }

        for (int at = Math.Max(edge + 1, length - edge); at <= length; at++)
        {
            yield return at;
        }
    }

    private static bool Same(ParseOutcome whole, ParseOutcome split)
    {
        if (whole.Succeeded != split.Succeeded)
        {
            return false;
        }

        if (!whole.Succeeded)
        {
            // The rendered error is kind, position and IRI reason together, so
            // comparing it compares all three.
            return string.Equals(whole.Error, split.Error, StringComparison.Ordinal);
        }

        if (whole.Quads.Count != split.Quads.Count)
        {
            return false;
        }

        for (int i = 0; i < whole.Quads.Count; i++)
        {
            if (!string.Equals(whole.Quads[i], split.Quads[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(ParseOutcome outcome)
    {
        if (!outcome.Succeeded)
        {
            return "rejected, " + outcome.Error;
        }

        StringBuilder text = new();
        text.Append(outcome.Quads.Count).Append(" quad(s)");

        for (int i = 0; i < outcome.Quads.Count && i < 3; i++)
        {
            text.Append("\n      ").Append(outcome.Quads[i]);
        }

        return text.ToString();
    }
}
