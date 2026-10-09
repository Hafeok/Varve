// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Conformance.Tests;

/// <summary>
/// One quad as the harness compares it: each term in canonical N-Quads text.
/// </summary>
/// <remarks>
/// A blank node is <c>_:label</c>, and since RDF 1.2 it may also stand inside
/// a triple term, <c>&lt;&lt;( _:a &lt;p&gt; _:b )&gt;&gt;</c>, nested to any
/// depth. Every question about a quad's blank nodes therefore walks the term
/// text rather than testing its first two bytes, which is what the first
/// RDF 1.2 Turtle evaluation run found this record getting wrong.
/// </remarks>
internal sealed record ParsedQuad(string Subject, string Predicate, string Object, string? Graph)
{
    internal bool SubjectIsBlank => IsBlank(Subject);

    internal bool ObjectIsBlank => IsBlank(Object);

    internal bool GraphIsBlank => Graph is not null && IsBlank(Graph);

    /// <summary>Whether a term is itself a blank node.</summary>
    internal static bool IsBlank(string term) =>
        term.StartsWith("_:", StringComparison.Ordinal);

    /// <summary>Whether any blank node stands anywhere in the quad, a triple term's inside included.</summary>
    internal bool HasBlank
    {
        get
        {
            foreach (string _ in BlankNodes())
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>Every blank node label in the quad, in order of appearance, repeats included.</summary>
    internal IEnumerable<string> BlankNodes()
    {
        foreach (string blank in BlankNodesIn(Subject))
        {
            yield return blank;
        }

        foreach (string blank in BlankNodesIn(Object))
        {
            yield return blank;
        }

        if (Graph is not null)
        {
            foreach (string blank in BlankNodesIn(Graph))
            {
                yield return blank;
            }
        }
    }

    /// <summary>
    /// The blank node labels in one term: the term itself when it is one, and
    /// each <c>_:label</c> token inside a triple term. A token starts at the
    /// term's start or after a space and runs to a space, a <c>)</c> or the end,
    /// which is where canonical N-Quads text puts the boundaries; a literal
    /// never starts with <c>_:</c> and holds no unquoted spaces before its
    /// closing quote that could be followed by one.
    /// </summary>
    internal static IEnumerable<string> BlankNodesIn(string term)
    {
        if (IsBlank(term))
        {
            yield return term;
            yield break;
        }

        if (!term.StartsWith("<<(", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach ((int start, int end) in BlankSpans(term))
        {
            yield return term[start..end];
        }
    }

    private static IEnumerable<(int Start, int End)> BlankSpans(string term)
    {
        int i = 0;

        while (i < term.Length)
        {
            if (term[i] == '"')
            {
                // Skip a literal's lexical form, escapes included, so that a
                // quoted "_:" is text and not a node.
                i++;

                while (i < term.Length && term[i] != '"')
                {
                    i += term[i] == '\\' ? 2 : 1;
                }

                i++;
                continue;
            }

            if (term[i] == '_' && i + 1 < term.Length && term[i + 1] == ':' && (i == 0 || term[i - 1] == ' '))
            {
                int end = i + 2;

                while (end < term.Length && term[end] is not (' ' or ')'))
                {
                    end++;
                }

                yield return (i, end);
                i = end;
                continue;
            }

            i++;
        }
    }

    /// <summary>The quad as an N-Quads line, for a failure message.</summary>
    public override string ToString() =>
        Graph is null
            ? $"{Subject} {Predicate} {Object} ."
            : $"{Subject} {Predicate} {Object} {Graph} .";

    /// <summary>
    /// The same quad with each blank node replaced by
    /// <paramref name="mapping"/>'s name for it, or by <c>_:?</c> where it has
    /// none yet.
    /// </summary>
    internal ParsedQuad Rename(IReadOnlyDictionary<string, string> mapping) =>
        new(
            Renamed(Subject, mapping),
            Predicate,
            Renamed(Object, mapping),
            Graph is null ? null : Renamed(Graph, mapping));

    /// <summary>The quad with every blank node replaced by <c>_:*</c>: its shape.</summary>
    internal ParsedQuad Shape() =>
        new(Anonymise(Subject), Predicate, Anonymise(Object), Graph is null ? null : Anonymise(Graph));

    internal static string Anonymise(string term) => Rewrite(term, static _ => "_:*");

    private static string Renamed(string term, IReadOnlyDictionary<string, string> mapping) =>
        Rewrite(term, label => mapping.TryGetValue(label, out string? renamed) ? renamed : "_:?");

    private static string Rewrite(string term, Func<string, string> replace)
    {
        if (IsBlank(term))
        {
            return replace(term);
        }

        if (!term.StartsWith("<<(", StringComparison.Ordinal))
        {
            return term;
        }

        System.Text.StringBuilder text = new();
        int at = 0;

        foreach ((int start, int end) in BlankSpans(term))
        {
            text.Append(term, at, start - at).Append(replace(term[start..end]));
            at = end;
        }

        return text.Append(term, at, term.Length - at).ToString();
    }
}
