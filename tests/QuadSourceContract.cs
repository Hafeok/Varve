// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve;

/// <summary>
/// The term-lookup contract of <see cref="IQuadSource.TryInternalise"/> (ADR
/// 0022, amended 2026-10-30): false means no quad the source will ever yield
/// holds the term, including quads it has not loaded yet. Linked into every
/// test project with a quad source of its own, and the standard test for a
/// new one (#191).
/// </summary>
/// <remarks>
/// <para>
/// A query names its terms before it matches its patterns, and a constant
/// the source denies compiles to a pattern that never runs. A source that
/// loads quads lazily, and answers "no such term" before it has, turns "not
/// loaded yet" into an empty answer (#183).
/// </para>
/// <para>
/// The check makes a source once, completes it, and collects every term its
/// quads hold. Then, for each term, it makes the source fresh, asks for that
/// term first, completes it, and asks again. A term denied at either lookup
/// must not be in what the completed source yields. Asking on a fresh source
/// matters: a lookup that loads something, such as naming a report graph,
/// would otherwise hide a denial that a query asking in another order sees.
/// </para>
/// <para>
/// <strong>A triple term's parts</strong> are checked when the source takes
/// the triple term apart by handle (<see cref="IQuadSource.TryGetTripleTermComponents"/>).
/// When it cannot, the evaluator matches the externalised triple term by
/// value, and a part the source denies still matches there
/// (<c>EvaluatorTests.A_term_only_inside_a_triple_term_is_found_by_a_triple_term_pattern_naming_it</c>).
/// </para>
/// <para>
/// <strong>Completing</strong> is a match of every quad in every graph, which
/// makes any lazy load the source has, followed by the caller's own step
/// when the source has a load a match does not make. A blank node, and a triple term with one inside, are not checked: a
/// source looks up no blank node by label (ADR 0044), and a query names
/// none.
/// </para>
/// </remarks>
internal static class QuadSourceContract
{
    /// <summary>
    /// Asserts the contract for the source <paramref name="fresh"/> makes,
    /// over every term the completed source yields and every term of
    /// <paramref name="probes"/>. <paramref name="fresh"/> must make the same
    /// source each time, in the same state.
    /// </summary>
    /// <returns>The terms the completed source yields, so that a test can show the check was not vacuous.</returns>
    internal static IReadOnlyCollection<RdfTerm> Holds(Func<IQuadSource> fresh, IEnumerable<RdfTerm>? probes = null, Action<IQuadSource>? complete = null)
    {
        ArgumentNullException.ThrowIfNull(fresh);

        HashSet<RdfTerm> yielded = Yielded(Complete(fresh(), complete));
        List<RdfTerm> terms = [.. yielded];
        if (probes is not null)
        {
            foreach (RdfTerm probe in probes)
            {
                if (!yielded.Contains(probe))
                {
                    terms.Add(probe);
                }
            }
        }

        foreach (RdfTerm term in terms)
        {
            if (HasBlank(term))
            {
                continue;
            }

            IQuadSource source = fresh();
            bool before = source.TryInternalise(term, out _);
            HashSet<RdfTerm> after = Yielded(Complete(source, complete));
            bool again = source.TryInternalise(term, out _);

            if (!before && after.Contains(term))
            {
                Assert.Fail($"{source.GetType().Name} denied {Show(term)} at its first lookup, and yields a quad holding it once loaded.");
            }

            if (!again && after.Contains(term))
            {
                Assert.Fail($"{source.GetType().Name} denies {Show(term)} once loaded, and yields a quad holding it.");
            }
        }

        return yielded;
    }

    private static IQuadSource Complete(IQuadSource source, Action<IQuadSource>? complete)
    {
        using (IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
            }
        }

        complete?.Invoke(source);
        return source;
    }

    /// <summary>Every term in a quad of <paramref name="source"/>, with the parts of each triple term it takes apart.</summary>
    private static HashSet<RdfTerm> Yielded(IQuadSource source)
    {
        HashSet<RdfTerm> terms = new(RdfTerm.Comparer);
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            Add(source, quad.Subject, terms);
            Add(source, quad.Predicate, terms);
            Add(source, quad.Object, terms);
            if (!quad.Graph.IsNone)
            {
                Add(source, quad.Graph, terms);
            }
        }

        return terms;
    }

    // A triple term's parts are the source's terms when it takes the triple
    // term apart by handle. When it cannot, the evaluator matches the
    // externalised triple term by value, a part it denies included
    // (EvaluationHotPathScope.NestedPatternsExternalise), so no denial of one
    // is lost.
    private static void Add(IQuadSource source, TermHandle handle, HashSet<RdfTerm> terms)
    {
        Assert.True(source.TryExternalise(handle, out RdfTerm? term), $"{source.GetType().Name} yields a handle it cannot externalise.");
        if (terms.Add(term) && source.TryGetTripleTermComponents(handle, out TermHandle s, out TermHandle p, out TermHandle o))
        {
            Add(source, s, terms);
            Add(source, p, terms);
            Add(source, o, terms);
        }
    }

    private static bool HasBlank(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.BlankNode => true,
        RdfTermKind.TripleTerm => HasBlank(term.Subject!) || HasBlank(term.Predicate!) || HasBlank(term.Object!),
        _ => false,
    };

    private static string Show(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.TripleTerm => "<<( " + Show(term.Subject!) + " " + Show(term.Predicate!) + " " + Show(term.Object!) + " )>>",
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\"",
    };
}
