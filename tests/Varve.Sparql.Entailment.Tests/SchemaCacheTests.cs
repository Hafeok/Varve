// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Varve.Rdf;
using Varve.Sparql.Entailment.Model;
using Xunit;
using static Varve.Sparql.Entailment.Tests.Support;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>
/// ADR 0193's schema cache: a schema is kept while the graphs its scoping
/// graph merges keep their versions, and saturated again when one changes;
/// a graph a caller cannot read keys as a graph with no quad.
/// </summary>
public class SchemaCacheTests
{
    private static readonly RdfTerm SubClassOf = IriOf(RdfsNs + "subClassOf");
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly TBoxCover[] Covers = [new TBoxCover([Tbox], GraphScope.All)];

    [Fact]
    public void A_second_query_over_the_same_versions_reuses_the_schema()
    {
        IQuadSource source = Versioned(Students(), (null, 1), (Tbox, 1));
        SchemaCache cache = new();
        List<string> uncached = Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers);

        Assert.Equal(uncached, Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers, cache));
        long misses = cache.Misses.Value;
        Assert.Equal(0, cache.Hits.Value);

        Assert.Equal(uncached, Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers, cache));
        Assert.Equal(misses, cache.Misses.Value);
        Assert.True(cache.Hits.Value > 0);
    }

    [Fact]
    public void A_new_version_of_a_merged_graph_saturates_the_schema_again()
    {
        SchemaCache cache = new();
        Assert.Empty(Run("SELECT * { ?x a :Agent }", Versioned(Students(), (null, 1), (Tbox, 1)), EntailmentRegime.Rdfs, Covers, cache));

        InMemoryDatasetBuilder changed = Students();
        changed.Add(Named("Person"), SubClassOf, Named("Agent"), Tbox);
        Assert.Equal(["?x=<http://example.org/a>"], Run("SELECT * { ?x a :Agent }", Versioned(changed, (null, 1), (Tbox, 2)), EntailmentRegime.Rdfs, Covers, cache));
    }

    [Fact]
    public void A_graph_the_caller_cannot_read_does_not_share_its_schema()
    {
        InMemoryDatasetBuilder builder = new();
        RdfTerm g = Named("g");
        builder.Add(Named("C"), SubClassOf, Named("D"), g);
        builder.Add(Named("a"), Type, Named("C"), g);
        IQuadSource versioned = Versioned(builder, (g, 5));
        IQuadSource reader = GraphScopedQuadSource.Wrap(versioned, GraphScope.Of([g], [], DefaultGraphAccess.Included));
        IQuadSource outsider = GraphScopedQuadSource.Wrap(versioned, GraphScope.Of([], [], DefaultGraphAccess.Included));
        const string Query = "SELECT * { GRAPH :g { ?x a :D } }";

        SchemaCache outsiderFirst = new();
        Assert.Empty(Run(Query, outsider, EntailmentRegime.Rdfs, [], outsiderFirst));
        Assert.Equal(["?x=<http://example.org/a>"], Run(Query, reader, EntailmentRegime.Rdfs, [], outsiderFirst));

        SchemaCache readerFirst = new();
        Assert.Equal(["?x=<http://example.org/a>"], Run(Query, reader, EntailmentRegime.Rdfs, [], readerFirst));
        Assert.Empty(Run(Query, outsider, EntailmentRegime.Rdfs, [], readerFirst));
    }

    [Fact]
    public void The_cache_keeps_at_most_its_bound()
    {
        IQuadSource source = Versioned(Students(), (null, 1), (Tbox, 1), (Named("g"), 1));
        SchemaCache cache = new(1);
        Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers, cache);
        Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdf, Covers, cache);
        long misses = cache.Misses.Value;

        // The RDF schema displaced the RDFS one.
        Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers, cache);
        Assert.True(cache.Misses.Value > misses);
    }

    [Fact]
    public void A_source_that_names_no_version_saturates_per_request()
    {
        IQuadSource source = Students().ToDataset();
        SchemaCache cache = new();
        Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers, cache);
        Run("SELECT * { ?x a :Person }", source, EntailmentRegime.Rdfs, Covers, cache);
        Assert.Equal(0, cache.Hits.Value);
        Assert.True(cache.Misses.Value >= 2);
    }

    private static InMemoryDatasetBuilder Students()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Named("Student"), SubClassOf, Named("Person"), Tbox);
        builder.Add(Named("a"), Type, Named("Student"));
        return builder;
    }

    // A source naming the versions it is given; a graph not given has none.
    private static VersionedSource Versioned(InMemoryDatasetBuilder builder, params (RdfTerm? Graph, long Version)[] versions) =>
        new VersionedSource(builder.ToDataset(), versions);

    private sealed class VersionedSource(InMemoryDataset inner, (RdfTerm? Graph, long Version)[] versions) : IQuadSource, IGraphVersionSource
    {
        public IEqualityComparer<TermHandle> TermComparer => inner.TermComparer;

        public bool TryInternalise(RdfTerm term, out TermHandle handle) => inner.TryInternalise(term, out handle);

        public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => inner.TryExternalise(handle, out term);

        public bool Contains(in Quad quad) => inner.Contains(in quad);

        public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) => inner.Match(subject, predicate, @object, graph);

        public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) => inner.Estimate(subject, predicate, @object, graph);

        public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => inner.TryGetInlineValue(handle, out value);

        public bool TryGetGraphVersion(GraphPattern graph, out GraphVersion version)
        {
            RdfTerm? name = null;
            if (graph.Match == GraphMatch.Named && !inner.TryExternalise(graph.Graph, out name))
            {
                version = GraphVersion.Empty;
                return false;
            }

            foreach ((RdfTerm? g, long v) in versions)
            {
                if (g is null ? name is null : name is not null && g.Equals(name))
                {
                    version = new GraphVersion(v);
                    return true;
                }
            }

            version = GraphVersion.Empty;
            return true;
        }
    }
}
