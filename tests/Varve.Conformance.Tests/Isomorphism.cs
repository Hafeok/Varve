using System;
using System.Collections.Generic;

namespace Varve.Conformance.Tests;

/// <summary>
/// Whether two datasets are the same up to a bijection of their blank nodes.
/// </summary>
/// <remarks>
/// <para>
/// What an evaluation test asks. A document's blank node labels are its own
/// (<c>turtle.md</c> §4), so the expected result file and the parse cannot be
/// compared by label — only by whether some renaming of one is the other.
/// </para>
/// <para>
/// <strong>This is test code and stays test code</strong> (ADR 0030 §3).
/// RDFC-1.0 brings the production answer at a later milestone: it assigns each
/// blank node a canonical label, after which isomorphism is equality. Shipping
/// a second answer in <c>Varve.Rdf</c> would mean maintaining both and having
/// to decide which is authoritative when they disagree — and this one is a
/// backtracking search whose cost is exponential in an input nobody bounds,
/// which is fine at suite sizes and is not an API to hand anyone.
/// </para>
/// <para>
/// It is not taken from dotNetRDF, which this milestone is retiring: a harness
/// that uses another implementation to judge whether we agree with the
/// specification is measuring agreement with that implementation.
/// </para>
/// </remarks>
/// <summary>What a comparison came to.</summary>
internal enum IsomorphismVerdict
{
    /// <summary>The datasets are the same up to a bijection of blank nodes.</summary>
    Same,

    /// <summary>They are not, and the search proved it.</summary>
    Different,

    /// <summary>
    /// The search ran out of budget without an answer.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Different"/> on purpose. "Not isomorphic" is a
    /// finding about the parser; "I gave up" is a finding about this check, and
    /// reporting the second as the first would send someone looking for a bug
    /// in the reader that is not there.
    /// </remarks>
    Inconclusive,
}

/// <summary>A verdict and why.</summary>
/// <param name="Verdict">What the comparison came to.</param>
/// <param name="Reason">
/// What differs, or why the search stopped. Empty when the verdict is
/// <see cref="IsomorphismVerdict.Same"/>.
/// </param>
internal readonly record struct IsomorphismResult(IsomorphismVerdict Verdict, string Reason)
{
    internal static IsomorphismResult Same { get; } = new(IsomorphismVerdict.Same, "");

    internal static IsomorphismResult Different(string reason) =>
        new(IsomorphismVerdict.Different, reason);

    internal static IsomorphismResult Inconclusive(string reason) =>
        new(IsomorphismVerdict.Inconclusive, reason);

    internal bool IsSame => Verdict == IsomorphismVerdict.Same;
}

internal static class Isomorphism
{
    /// <summary>
    /// The most candidate mappings to try before giving up.
    /// </summary>
    /// <remarks>
    /// The search is exponential in the worst case, and a harness that hangs
    /// is worse than one that fails: a suite case that needs more than this is
    /// a case worth looking at by hand, and <see cref="Compare"/> says so
    /// rather than running until the build times out.
    /// </remarks>
    private const int MaxAttempts = 200_000;

    /// <summary>
    /// Compares two datasets, reporting why they differ when they do.
    /// </summary>
    internal static IsomorphismResult Compare(
        IReadOnlyList<ParsedQuad> actual, IReadOnlyList<ParsedQuad> expected)
    {
        if (actual.Count != expected.Count)
        {
            return IsomorphismResult.Different($"{actual.Count} quad(s), expected {expected.Count}");
        }

        List<string> actualBlanks = BlankNodes(actual);
        List<string> expectedBlanks = BlankNodes(expected);

        if (actualBlanks.Count != expectedBlanks.Count)
        {
            return IsomorphismResult.Different(
                $"{actualBlanks.Count} blank node(s), expected {expectedBlanks.Count}");
        }

        // The ground quads — those with no blank node anywhere — have to match
        // exactly, and checking them first turns most mismatches into a cheap
        // answer instead of a search that was never going to succeed.
        HashSet<ParsedQuad> expectedGround = [];

        foreach (ParsedQuad quad in expected)
        {
            if (!HasBlank(quad))
            {
                expectedGround.Add(quad);
            }
        }

        foreach (ParsedQuad quad in actual)
        {
            if (!HasBlank(quad) && !expectedGround.Contains(quad))
            {
                return IsomorphismResult.Different(
                    "this quad is not in the expected dataset: " + quad);
            }
        }

        if (actualBlanks.Count == 0)
        {
            return IsomorphismResult.Same;
        }

        Dictionary<string, List<string>> candidates =
            Candidates(actual, expected, actualBlanks, expectedBlanks);

        foreach (KeyValuePair<string, List<string>> entry in candidates)
        {
            if (entry.Value.Count == 0)
            {
                return IsomorphismResult.Different(
                    $"no blank node in the expected dataset can be {entry.Key}");
            }
        }

        // Most constrained first: it is the ordering that makes the search
        // finish, because a blank node with one candidate settles the rest.
        actualBlanks.Sort((left, right) => candidates[left].Count.CompareTo(candidates[right].Count));

        HashSet<ParsedQuad> expectedSet = [.. expected];
        Dictionary<string, string> mapping = new(StringComparer.Ordinal);
        HashSet<string> taken = new(StringComparer.Ordinal);
        int attempts = 0;

        if (Search(actual, expectedSet, actualBlanks, candidates, mapping, taken, 0, ref attempts))
        {
            return IsomorphismResult.Same;
        }

        // The budget being spent means the search abandoned subtrees it never
        // examined, so it has not shown the datasets differ — only that it
        // could not tell within the budget.
        return attempts > MaxAttempts
            ? IsomorphismResult.Inconclusive(
                $"the search gave up after {MaxAttempts} candidate mappings over "
                + $"{actualBlanks.Count} blank nodes, so whether these datasets match is unknown "
                + "rather than settled; this case needs looking at by hand")
            : IsomorphismResult.Different("no renaming of the blank nodes makes the two datasets equal");
    }

    private static bool Search(
        IReadOnlyList<ParsedQuad> actual,
        HashSet<ParsedQuad> expected,
        List<string> blanks,
        Dictionary<string, List<string>> candidates,
        Dictionary<string, string> mapping,
        HashSet<string> taken,
        int depth,
        ref int attempts)
    {
        if (depth == blanks.Count)
        {
            return Matches(actual, expected, mapping);
        }

        string blank = blanks[depth];

        foreach (string candidate in candidates[blank])
        {
            if (++attempts > MaxAttempts)
            {
                return false;
            }

            if (!taken.Add(candidate))
            {
                continue;
            }

            mapping[blank] = candidate;

            // Every quad whose blank nodes are all mapped must already be one
            // of the expected quads. Checking as we go is what keeps the search
            // from exploring a subtree that cannot work.
            if (Consistent(actual, expected, mapping) &&
                Search(actual, expected, blanks, candidates, mapping, taken, depth + 1, ref attempts))
            {
                return true;
            }

            mapping.Remove(blank);
            taken.Remove(candidate);
        }

        return false;
    }

    private static bool Consistent(
        IReadOnlyList<ParsedQuad> actual,
        HashSet<ParsedQuad> expected,
        Dictionary<string, string> mapping)
    {
        foreach (ParsedQuad quad in actual)
        {
            if (FullyMapped(quad, mapping) && !expected.Contains(quad.Rename(mapping)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(
        IReadOnlyList<ParsedQuad> actual,
        HashSet<ParsedQuad> expected,
        Dictionary<string, string> mapping)
    {
        HashSet<ParsedQuad> renamed = [];

        foreach (ParsedQuad quad in actual)
        {
            renamed.Add(quad.Rename(mapping));
        }

        return renamed.Count == expected.Count && renamed.SetEquals(expected);
    }

    private static bool FullyMapped(ParsedQuad quad, Dictionary<string, string> mapping) =>
        (!quad.SubjectIsBlank || mapping.ContainsKey(quad.Subject))
        && (!quad.ObjectIsBlank || mapping.ContainsKey(quad.Object))
        && (!quad.GraphIsBlank || mapping.ContainsKey(quad.Graph!));

    /// <summary>
    /// Which expected blank nodes each actual one could be, by signature.
    /// </summary>
    /// <remarks>
    /// A blank node's signature is the multiset of quads it appears in, with
    /// every blank node — itself included — blanked out. Two nodes that sit in
    /// differently-shaped quads cannot be each other, so pairing them is work
    /// the search never has to do.
    /// </remarks>
    private static Dictionary<string, List<string>> Candidates(
        IReadOnlyList<ParsedQuad> actual,
        IReadOnlyList<ParsedQuad> expected,
        List<string> actualBlanks,
        List<string> expectedBlanks)
    {
        Dictionary<string, string> actualSignatures = Signatures(actual, actualBlanks);
        Dictionary<string, string> expectedSignatures = Signatures(expected, expectedBlanks);
        Dictionary<string, List<string>> candidates = new(StringComparer.Ordinal);

        foreach (string blank in actualBlanks)
        {
            List<string> matching = [];

            foreach (string other in expectedBlanks)
            {
                if (string.Equals(actualSignatures[blank], expectedSignatures[other], StringComparison.Ordinal))
                {
                    matching.Add(other);
                }
            }

            candidates[blank] = matching;
        }

        return candidates;
    }

    private static Dictionary<string, string> Signatures(
        IReadOnlyList<ParsedQuad> quads, List<string> blanks)
    {
        Dictionary<string, List<string>> parts = new(StringComparer.Ordinal);

        foreach (string blank in blanks)
        {
            parts[blank] = [];
        }

        foreach (ParsedQuad quad in quads)
        {
            ParsedQuad shape = new(
                Anonymise(quad.Subject),
                quad.Predicate,
                Anonymise(quad.Object),
                quad.Graph is null ? null : Anonymise(quad.Graph));

            Record(parts, quad.Subject, "s|" + shape);
            Record(parts, quad.Object, "o|" + shape);

            if (quad.Graph is not null)
            {
                Record(parts, quad.Graph, "g|" + shape);
            }
        }

        Dictionary<string, string> signatures = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, List<string>> entry in parts)
        {
            entry.Value.Sort(StringComparer.Ordinal);
            signatures[entry.Key] = string.Join('\n', entry.Value);
        }

        return signatures;
    }

    private static void Record(Dictionary<string, List<string>> parts, string term, string part)
    {
        if (ParsedQuad.IsBlank(term))
        {
            parts[term].Add(part);
        }
    }

    private static string Anonymise(string term) => ParsedQuad.IsBlank(term) ? "_:*" : term;

    private static bool HasBlank(ParsedQuad quad) =>
        quad.SubjectIsBlank || quad.ObjectIsBlank || quad.GraphIsBlank;

    private static List<string> BlankNodes(IReadOnlyList<ParsedQuad> quads)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ParsedQuad quad in quads)
        {
            Add(seen, quad.Subject);
            Add(seen, quad.Object);

            if (quad.Graph is not null)
            {
                Add(seen, quad.Graph);
            }
        }

        return [.. seen];
    }

    private static void Add(HashSet<string> seen, string term)
    {
        if (ParsedQuad.IsBlank(term))
        {
            seen.Add(term);
        }
    }
}
