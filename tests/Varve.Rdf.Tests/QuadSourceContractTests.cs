// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
