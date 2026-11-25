// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using CsCheck;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// The governing-graph seam (ADR 0107, amended 2026-10-15): a governed graph is
/// allowed exactly when its governor is, by every path a scope decides, and a
/// grant naming the governed graph itself has no effect. The rule here is a
/// test's own: "http://gov/{x}" is governed by "http://ex/{x}". Governed by
/// a set (ADR 0190 point 6): "http://set/ab" by "http://ex/a" and
/// "http://ex/b", "http://set/a-default" by "http://ex/a" and the default
/// graph, allowed exactly when every governor is.
/// </summary>
public class GovernedGraphTests
{
    private static bool Rule(RdfTerm graph, out RdfTerm? governor)
    {
        string iri = Encoding.UTF8.GetString(graph.Lexical);

        if (iri == "http://gov/default")
        {
            governor = null;
            return true;
        }

        if (iri.StartsWith("http://gov/", StringComparison.Ordinal))
        {
            governor = Iri("http://ex/" + iri["http://gov/".Length..]);
            return true;
        }

        governor = null;
        return false;
    }

    private static bool SetRule(RdfTerm graph, out IReadOnlyList<RdfTerm?>? governors)
    {
        governors = Encoding.UTF8.GetString(graph.Lexical) switch
        {
            "http://set/ab" => [Iri("http://ex/a"), Iri("http://ex/b")],
            "http://set/a-default" => [Iri("http://ex/a"), null],
            _ => null,
        };
        return governors is not null;
    }

    private static readonly string[] Names = ["http://ex/a", "http://ex/b", "http://gov/a", "http://gov/b", "http://gov/default", "http://other/c", "http://set/ab", "http://set/a-default"];

    [Fact]
    public void a_graph_governed_by_a_set_needs_every_governor_and_not_its_own_grant()
    {
        GraphScope a = GraphScope.Of([Iri("http://ex/a"), Iri("http://set/ab")], [], DefaultGraphAccess.Excluded).WithGovernor(Rule).WithGovernors(SetRule);
        GraphScope ab = GraphScope.Of([Iri("http://ex/a"), Iri("http://ex/b")], [], DefaultGraphAccess.Excluded).WithGovernors(SetRule).WithGovernor(Rule);
        GraphScope withDefault = GraphScope.Of([Iri("http://ex/a")], [], DefaultGraphAccess.Included).WithGovernors(SetRule);

        Assert.False(a.Allows(Iri("http://set/ab")));
        Assert.True(a.Allows(Iri("http://gov/a")));
        Assert.True(ab.Allows(Iri("http://set/ab")));
        Assert.True(ab.Allows(Iri("http://gov/b")));
        Assert.False(ab.Allows(Iri("http://set/a-default")));
        Assert.True(withDefault.Allows(Iri("http://set/a-default")));
        Assert.True(a.IsGoverned(Iri("http://set/ab")) && a.IsGoverned(Iri("http://gov/a")) && !a.IsGoverned(Iri("http://ex/a")));
        Assert.Same(GraphScope.All, GraphScope.All.WithGovernors(SetRule));
    }

    [Fact]
    public void a_governed_graph_follows_its_governor_and_not_its_own_grant()
    {
        GraphScope scope = GraphScope.Of([Iri("http://ex/a"), Iri("http://gov/b")], [], DefaultGraphAccess.Excluded).WithGovernor(Rule);

        Assert.True(scope.Allows(Iri("http://gov/a")));
        Assert.False(scope.Allows(Iri("http://gov/b")));
        Assert.False(scope.Allows(Iri("http://gov/default")));
        Assert.True(scope.Allows(Iri("http://ex/a")));
        Assert.False(scope.Allows(Iri("http://ex/b")));
        Assert.Same(GraphScope.All, GraphScope.All.WithGovernor(Rule));
    }

    [Fact]
    public void the_scoped_source_decides_as_the_scope_does_through_every_pattern() =>
        Gen.Select(Gen.Bool.Array[Names.Length], Gen.Bool, Gen.Bool.Array[2]).Sample((explicitly, includesDefault, prefixes) =>
        {
            List<RdfTerm> graphs = [];

            for (int i = 0; i < Names.Length; i++)
            {
                if (explicitly[i])
                {
                    graphs.Add(Iri(Names[i]));
                }
            }

            List<string> starts = [];

            if (prefixes[0])
            {
                starts.Add("http://ex/");
            }

            if (prefixes[1])
            {
                starts.Add("http://gov/");
            }

            GraphScope scope = GraphScope.Of([.. graphs], [.. starts], includesDefault ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded).WithGovernor(Rule).WithGovernors(SetRule);
            InMemoryDatasetBuilder builder = new();
            builder.Add(Iri("s"), Iri("p"), Iri("o"), null);

            foreach (string name in Names)
            {
                builder.Add(Iri("s"), Iri("p"), Iri("o"), Iri(name));
            }

            InMemoryDataset dataset = builder.ToDataset();
            GraphScopedQuadSource scoped = new(dataset, scope);
            HashSet<string> seen = [];
            using IQuadCursor cursor = scoped.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.AnyNamed);

            while (cursor.MoveNext())
            {
                Assert.True(dataset.TryExternalise(cursor.Current.Graph, out RdfTerm? graph));
                seen.Add(Encoding.UTF8.GetString(graph.Lexical));
            }

            foreach (string name in Names)
            {
                bool expected = Expected(name, graphs, starts, includesDefault);

                if (seen.Contains(name) != expected || scope.Allows(Iri(name)) != expected)
                {
                    return false;
                }

                Assert.True(dataset.TryInternalise(Iri(name), out TermHandle handle));

                if (scoped.Allows(handle) != expected)
                {
                    return false;
                }
            }

            return true;
        }, iter: 2_000);

    // The oracle: a governed graph is its governor's answer; any other graph
    // is in the set or under a prefix.
    private static bool Expected(string name, List<RdfTerm> graphs, List<string> prefixes, bool includesDefault)
    {
        if (name == "http://gov/default")
        {
            return includesDefault;
        }

        if (name.StartsWith("http://gov/", StringComparison.Ordinal))
        {
            return Expected("http://ex/" + name["http://gov/".Length..], graphs, prefixes, includesDefault);
        }

        if (name == "http://set/ab")
        {
            return Expected("http://ex/a", graphs, prefixes, includesDefault) && Expected("http://ex/b", graphs, prefixes, includesDefault);
        }

        if (name == "http://set/a-default")
        {
            return Expected("http://ex/a", graphs, prefixes, includesDefault) && includesDefault;
        }

        return graphs.Exists(g => Encoding.UTF8.GetString(g.Lexical) == name) || prefixes.Exists(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
