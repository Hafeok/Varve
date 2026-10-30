// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Xunit;
using static Varve.Sparql.Entailment.Tests.Support;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>
/// ADR 0193 point 2's one full read, surfaced: the graphs whose content says
/// something of the vocabulary itself are named, wherever they are.
/// </summary>
public class FullReadTests
{
    private static readonly RdfTerm SubClassOf = IriOf(RdfsNs + "subClassOf");
    private static readonly RdfTerm SubPropertyOf = IriOf(RdfsNs + "subPropertyOf");
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly TBoxCover[] Covers = [new TBoxCover([Tbox], GraphScope.All)];

    [Fact]
    public void A_schema_that_says_nothing_of_the_vocabulary_forces_no_full_read()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Named("Student"), SubClassOf, Named("Person"), Tbox);
        builder.Add(Named("a"), Type, Named("Student"));
        builder.Add(Named("C"), SubClassOf, Named("D"), Named("g"));
        Assert.Empty(EntailmentRewriter.FullReads(builder.ToDataset(), Covers, cancellationToken: TestContext.Current.CancellationToken).Graphs);
    }

    [Fact]
    public void A_redefinition_in_a_data_graph_names_that_graph()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Named("Student"), SubClassOf, Named("Person"), Tbox);
        builder.Add(Type, SubPropertyOf, Named("classifiedAs"), Named("g"));
        builder.Add(Named("a"), Type, Named("Student"), Named("g"));
        Assert.Equal([Named("g")], EntailmentRewriter.FullReads(builder.ToDataset(), Covers, cancellationToken: TestContext.Current.CancellationToken).Graphs);
    }

    [Fact]
    public void A_redefinition_in_the_TBox_or_the_default_graph_names_it()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Type, SubPropertyOf, Named("classifiedAs"), Tbox);
        builder.Add(IriOf(RdfsNs + "Resource"), SubClassOf, Named("Thing"));
        builder.Add(Named("a"), Type, Named("Student"));
        Assert.Equal([null, Tbox], EntailmentRewriter.FullReads(builder.ToDataset(), Covers, cancellationToken: TestContext.Current.CancellationToken).Graphs);
    }

    [Fact]
    public void A_cache_answers_a_second_report_without_saturating_again()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Type, SubPropertyOf, Named("classifiedAs"), Named("g"));
        SchemaCache cache = new();
        IQuadSource source = new SchemaCacheTests.VersionedSource(builder.ToDataset(), [(Named("g"), 3)]);
        EntailmentRewriter.FullReads(source, Covers, cache, TestContext.Current.CancellationToken);
        long misses = cache.Misses.Value;
        Assert.Equal([Named("g")], EntailmentRewriter.FullReads(source, Covers, cache, TestContext.Current.CancellationToken).Graphs);
        Assert.Equal(misses, cache.Misses.Value);
    }
}
