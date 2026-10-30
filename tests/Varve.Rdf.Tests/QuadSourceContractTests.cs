// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// Layer 1's quad sources under the term-lookup contract (ADR 0022, amended
/// 2026-10-30; #191): a term denied is in no quad the source yields. None of
/// them loads anything: an in-memory dataset is immutable, and an overlay
/// and a graph scope look terms up in the source they wrap.
/// </summary>
public class QuadSourceContractTests
{
    private static readonly RdfTerm Absent = Iri("absent");

    [Fact]
    public void an_in_memory_dataset_never_denies_a_term_it_holds()
    {
        InMemoryDataset dataset = Filled(out _);

        Assert.Contains(Iri("o"), QuadSourceContract.Holds(() => dataset, [Absent]));
    }

    /// <summary>
    /// The overlay's delta holds the base's handles (ADR 0017), so a term the
    /// delta asserts is one the base names, held by no quad of the base here.
    /// </summary>
    [Fact]
    public void an_overlay_never_denies_a_term_its_delta_asserts()
    {
        _ = Filled(out InMemoryDatasetBuilder builder);
        Quad added = new(builder.Internalise(Iri("s")), builder.Internalise(Iri("p")), builder.Internalise(Iri("only-in-the-delta")), builder.Internalise(Iri("g")));
        Quad removed = new(builder.Internalise(Iri("s")), builder.Internalise(Iri("p")), builder.Internalise(Iri("o")), TermHandle.None);
        InMemoryDataset @base = builder.ToDataset();
        QuadOverlay overlay = new(@base, QuadDelta.Create([added], [removed]));

        Assert.Contains(Iri("only-in-the-delta"), QuadSourceContract.Holds(() => overlay, [Absent]));
    }

    [Fact]
    public void a_graph_scope_never_denies_a_term_a_graph_it_allows_holds()
    {
        InMemoryDataset dataset = Filled(out _);
        IQuadSource scoped = GraphScopedQuadSource.Wrap(dataset, GraphScope.Of([Iri("g")], [], DefaultGraphAccess.Excluded));

        Assert.Contains(Iri("in-g"), QuadSourceContract.Holds(() => scoped, [Absent, Iri("o")]));
    }

    /// <summary>
    /// The row-scoped source (ADR 0235) looks every term up in the source it
    /// wraps, so it denies none its inner holds, hidden rows included; and
    /// the quads it yields hold only terms its inner names. Over a restricted
    /// scope, with a hidden row, a link into it, and a triple term whose
    /// subjects are its components'.
    /// </summary>
    [Fact]
    public void a_row_scoped_source_never_denies_a_term_a_row_it_shows_holds()
    {
        InMemoryDatasetBuilder builder = new();
        RdfTerm g1 = RowFixture.IriOf(RowFixture.Classified[0]);
        RdfTerm person = RowFixture.IriOf(RowFixture.Person);
        RdfTerm type = RowFixture.IriOf(RowFixture.TypeIri);
        builder.Add(Iri("a"), type, person, g1);
        builder.Add(Iri("b"), type, person, g1);
        builder.Add(Iri("x"), RowFixture.IriOf(RowFixture.Owns), Iri("a"), g1);
        builder.Add(Iri("y"), RowFixture.IriOf(RowFixture.Owns), Iri("b"), g1);
        builder.Add(Iri("a"), RowFixture.IriOf(RowFixture.Knows), Iri("b"), g1);
        builder.Add(Iri("x"), RowFixture.IriOf(RowFixture.Label), RdfTerm.Literal("shown"u8), g1);
        builder.Add(Iri("r"), Iri("reifies"), RdfTerm.TripleTerm(Iri("x"), Iri("q"), Iri("a")), g1);
        builder.Add(Iri("outside"), Iri("p"), Iri("b"), RowFixture.IriOf(RowFixture.Outside));
        InMemoryDataset dataset = builder.ToDataset();
        RowScope scope = RowScope.Of([new(RowFixture.IriOf(RowFixture.Binding), SubjectScope.Of([Iri("a")], [], [], [], []))]);

        IReadOnlyCollection<RdfTerm> yielded = QuadSourceContract.Holds(() => new RowScopedQuadSource(dataset, new RowFixture(dataset), scope), [Absent, Iri("y")]);
        Assert.Contains(Iri("x"), yielded);
        Assert.Contains(RdfTerm.Literal("shown"u8), yielded);
        Assert.DoesNotContain(Iri("y"), yielded);
    }

    private static InMemoryDataset Filled(out InMemoryDatasetBuilder builder)
    {
        builder = new InMemoryDatasetBuilder();
        builder.Add(Iri("s"), Iri("p"), Iri("o"));
        builder.Add(Iri("s"), Iri("p"), Iri("in-g"), Iri("g"));
        builder.Add(Iri("r"), Iri("reifies"), RdfTerm.TripleTerm(Iri("s"), Iri("p"), RdfTerm.Literal("o"u8)), Iri("h"));
        return builder.ToDataset();
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + local));
}
