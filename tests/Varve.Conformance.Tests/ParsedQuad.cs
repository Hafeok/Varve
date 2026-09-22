using System;

namespace Varve.Conformance.Tests;

/// <summary>
/// One quad, each term in canonical N-Triples term syntax.
/// </summary>
/// <remarks>
/// <para>
/// Terms are strings because term equality <em>is</em> string equality on that
/// syntax: an IRI, a blank node and a literal each have one canonical spelling,
/// and two terms are the same term exactly when their spellings match (RDF 1.1
/// Concepts §3.3). Keeping them as four separate strings rather than one line
/// is what lets the isomorphism check see which position a blank node is in
/// without re-parsing the line — and an N-Quads line cannot be split on spaces,
/// because a literal may contain them.
/// </para>
/// <para>
/// A blank node is exactly a term beginning <c>_:</c>, which no other term
/// form can.
/// </para>
/// </remarks>
internal sealed record ParsedQuad(string Subject, string Predicate, string Object, string? Graph)
{
    internal bool SubjectIsBlank => IsBlank(Subject);

    internal bool ObjectIsBlank => IsBlank(Object);

    internal bool GraphIsBlank => Graph is not null && IsBlank(Graph);

    internal static bool IsBlank(string term) =>
        term.StartsWith("_:", StringComparison.Ordinal);

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
    internal ParsedQuad Rename(System.Collections.Generic.IReadOnlyDictionary<string, string> mapping) =>
        new(
            Renamed(Subject, mapping),
            Predicate,
            Renamed(Object, mapping),
            Graph is null ? null : Renamed(Graph, mapping));

    private static string Renamed(
        string term, System.Collections.Generic.IReadOnlyDictionary<string, string> mapping)
    {
        if (!IsBlank(term))
        {
            return term;
        }

        return mapping.TryGetValue(term, out string? renamed) ? renamed : "_:?";
    }
}
