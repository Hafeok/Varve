// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Sparql.Tests;

/// <summary>
/// The parser allocates the tree and nothing else (<c>docs/spec/sparql-grammar.md</c>
/// §8): two queries of different sizes with a fixed vocabulary, and the
/// difference in what parsing them allocates is <em>equal</em> to the
/// difference for building the same two trees by hand.
/// </summary>
/// <remarks>
/// <para>
/// A difference rather than an absolute number, as <c>docs/testing.md</c> §4
/// asks, so that the fixed cost of a parse — a lexer buffer, the interning
/// table, the prologue — cancels. Equal rather than small, because a pooled
/// builder that leaked one growth step per pattern would be "small" and
/// wrong. Both sides are run twice first so that every pool has reached the
/// size the larger query needs.
/// </para>
/// <para>
/// Each side is measured three times and the smallest reading counts. The
/// first pull request run reported the parse 152 bytes over on one runner and
/// 152 under on another: a 16-element reference array, the shared array
/// pool's smallest bucket, which the pooled lists rent. That pool drops its
/// thread-local arrays on a gen-2 collection under high memory pressure, so
/// the collection this test used to force between warm-up and measurement
/// was what made the next rent allocate on a loaded runner. The allocation
/// counter is monotonic and needs no collection, so none is forced now; and
/// a stray allocation only ever adds, so the minimum of several readings is
/// the cost.
/// </para>
/// </remarks>
public class AllocationTests
{
    private const int Small = 50;
    private const int Large = 400;

    private static readonly string[] Predicates = ["p", "q", "r", "s", "t"];
    private static readonly string[] Variables = ["a", "b", "c", "d"];

    private static readonly byte[] SmallQuery = Query(Small);
    private static readonly byte[] LargeQuery = Query(Large);

    private static readonly TriplePattern[] SmallScratch = new TriplePattern[Small];
    private static readonly TriplePattern[] LargeScratch = new TriplePattern[Large];

    // The predicate IRIs as the parser sees them: bytes to copy from, so that
    // the hand-built side allocates the term and nothing on the way to it.
    private static readonly byte[][] PredicateIris = Array.ConvertAll(Predicates, p => Encoding.UTF8.GetBytes("http://example.org/" + p));

    private static byte[] Query(int patterns)
    {
        StringBuilder builder = new("PREFIX ex: <http://example.org/>\nSELECT ?a ?b WHERE {\n");

        for (int i = 0; i < patterns; i++)
        {
            builder.Append('?').Append(Variables[i % Variables.Length])
                .Append(" ex:").Append(Predicates[i % Predicates.Length])
                .Append(" ?").Append(Variables[(i + 1) % Variables.Length])
                .Append(" .\n");
        }

        return Encoding.UTF8.GetBytes(builder.Append("}\n").ToString());
    }

    /// <summary>The same tree the parser produces, built with the same public factories.</summary>
    private static SelectQuery Build(int patterns)
    {
        TriplePattern[] scratch = patterns == Small ? SmallScratch : LargeScratch;

        for (int i = 0; i < patterns; i++)
        {
            scratch[i] = new TriplePattern(
                new VariablePattern(new Variable(Variables[i % Variables.Length])),
                new TermPattern(RdfTerm.Iri(PredicateIris[i % Predicates.Length])),
                new VariablePattern(new Variable(Variables[(i + 1) % Variables.Length])));
        }

        Bgp bgp = new(AlgebraList.From<TriplePattern>(scratch));
        Project project = new(bgp, AlgebraList.Of(new Variable("a"), new Variable("b")));
        Prologue prologue = new(null, AlgebraList.Of(new PrefixDeclaration("ex", RdfTerm.Iri("http://example.org/"u8))), null);
        return new SelectQuery(prologue, null, project);
    }

    private static long Measure(Func<object> action)
    {
        for (int i = 0; i < 2; i++)
        {
            GC.KeepAlive(action());
        }

        long least = long.MaxValue;

        for (int i = 0; i < 3; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            object result = action();
            long after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(result);
            least = Math.Min(least, after - before);
        }

        return least;
    }

    [Fact]
    public void parsing_allocates_the_tree_and_nothing_else()
    {
        // Warm both sizes on both paths first, so that every pooled array the
        // larger parse rents has been rented before.
        for (int i = 0; i < 2; i++)
        {
            GC.KeepAlive(SparqlParser.ParseQuery(SmallQuery));
            GC.KeepAlive(SparqlParser.ParseQuery(LargeQuery));
            GC.KeepAlive(Build(Small));
            GC.KeepAlive(Build(Large));
        }

        long parsedSmall = Measure(static () => SparqlParser.ParseQuery(SmallQuery));
        long parsedLarge = Measure(static () => SparqlParser.ParseQuery(LargeQuery));
        long builtSmall = Measure(static () => Build(Small));
        long builtLarge = Measure(static () => Build(Large));

        long parsedPerPattern = parsedLarge - parsedSmall;
        long builtPerPattern = builtLarge - builtSmall;

        Assert.True(
            parsedPerPattern == builtPerPattern,
            string.Create(
                CultureInfo.InvariantCulture,
                $"parsing {Large - Small} more patterns allocated {parsedPerPattern} bytes; building them by hand allocated {builtPerPattern}"));

        // And the trees are the same trees.
        Assert.Equal(Build(Large), SparqlParser.ParseQuery(LargeQuery));
    }

    [Fact]
    public void the_hand_built_tree_is_what_the_parser_produces()
    {
        Assert.Equal(Build(Small), SparqlParser.ParseQuery(SmallQuery));
    }
}
