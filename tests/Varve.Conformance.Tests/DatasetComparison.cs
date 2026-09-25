// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Rdf;

namespace Varve.Conformance.Tests;

/// <summary>
/// Whether an actual dataset is the expected one up to its blank nodes: the
/// comparison for <c>CONSTRUCT</c> and <c>DESCRIBE</c> results and for update
/// results (ADR 0059).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Canonical equality decides</strong>: the two datasets' RDFC-1.0
/// forms are equal. <strong>The backtracking check runs alongside and must
/// agree</strong>: when it reaches a verdict and the verdict differs, the
/// comparison fails and names both, because one of two independent
/// implementations of one relation is wrong. Its <c>Inconclusive</c> — it ran
/// out of budget — is not a disagreement; the canonical verdict stands and the
/// message says the cross-check was inconclusive.
/// </para>
/// <para>
/// A dataset RDFC-1.0 cannot canonicalise — a triple term with a blank node
/// inside (<c>rdf-canon.md</c> §3.1) — is compared by the backtracking check
/// alone, and the message says so when it fails.
/// </para>
/// </remarks>
internal static class DatasetComparison
{
    /// <summary>Null when the datasets are isomorphic, otherwise why not.</summary>
    internal static string? Compare(IReadOnlyList<DataQuad> actual, IReadOnlyList<DataQuad> expected)
    {
        IsomorphismResult backtracking = Isomorphism.Compare(Parsed(actual), Parsed(expected));
        (bool Same, string Form)? canonical = Canonical(actual, expected);

        if (canonical is null)
        {
            return backtracking.IsSame
                ? null
                : "not isomorphic (backtracking; RDFC-1.0 cannot canonicalise a blank node inside a triple term): " + backtracking.Reason + Listing(actual, expected);
        }

        (bool same, string form) = canonical.Value;

        if (backtracking.Verdict != IsomorphismVerdict.Inconclusive && backtracking.IsSame != same)
        {
            return "the comparisons disagree: canonical equality says " + (same ? "isomorphic" : "not isomorphic")
                + ", the backtracking check says " + backtracking.Verdict + " (" + backtracking.Reason + ")" + Listing(actual, expected);
        }

        return same
            ? null
            : "not isomorphic: the canonical forms differ"
                + (backtracking.Verdict == IsomorphismVerdict.Inconclusive ? " (the backtracking check was inconclusive)" : "; " + backtracking.Reason)
                + "\n  actual, canonical:\n" + form + Listing(actual, expected);
    }

    /// <summary>
    /// The same quads with every blank node renamed apart per source file, so
    /// that two files' <c>_:b0</c> stay two nodes, as separate loads keep them.
    /// </summary>
    internal static IEnumerable<DataQuad> Apart(IEnumerable<DataQuad> quads, int file)
    {
        foreach (DataQuad quad in quads)
        {
            yield return new DataQuad(Apart(quad.Subject, file), quad.Predicate, Apart(quad.Object, file), quad.Graph is null ? null : Apart(quad.Graph, file));
        }
    }

    private static RdfTerm Apart(RdfTerm term, int file) => term.Kind switch
    {
        RdfTermKind.BlankNode => RdfTerm.BlankNode(Encoding.UTF8.GetBytes("f" + file + "_" + Encoding.UTF8.GetString(term.Lexical))),
        RdfTermKind.TripleTerm => RdfTerm.TripleTerm(Apart(term.Subject!, file), term.Predicate!, Apart(term.Object!, file)),
        _ => term,
    };

    private static (bool Same, string Form)? Canonical(IReadOnlyList<DataQuad> actual, IReadOnlyList<DataQuad> expected)
    {
        try
        {
            CanonicalDataset a = RdfCanonicaliser.Canonicalise(Dataset(actual));
            CanonicalDataset b = RdfCanonicaliser.Canonicalise(Dataset(expected));
            return (a.NQuads.Span.SequenceEqual(b.NQuads.Span), Encoding.UTF8.GetString(a.NQuads.Span));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static InMemoryDataset Dataset(IReadOnlyList<DataQuad> quads)
    {
        InMemoryDataset dataset = new();

        foreach (DataQuad quad in quads)
        {
            dataset.Add(quad.Subject, quad.Predicate, quad.Object, quad.Graph);
        }

        return dataset;
    }

    private static List<ParsedQuad> Parsed(IReadOnlyList<DataQuad> quads) =>
        [.. quads.Select(q => new ParsedQuad(EvaluationData.Text(q.Subject), EvaluationData.Text(q.Predicate), EvaluationData.Text(q.Object), q.Graph is null ? null : EvaluationData.Text(q.Graph))).Distinct()];

    private static string Listing(IReadOnlyList<DataQuad> actual, IReadOnlyList<DataQuad> expected) =>
        "\n  actual:\n    " + string.Join("\n    ", Parsed(actual)) + "\n  expected:\n    " + string.Join("\n    ", Parsed(expected));
}
