// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve.Sparql.Store.Tests;

/// <summary>
/// An update's quad sources under the term-lookup contract (ADR 0022,
/// amended 2026-10-30; #191): a term denied is in no quad the source yields.
/// Both look terms up in the source they wrap, and load nothing.
/// </summary>
public class QuadSourceContractTests
{
    private static readonly RdfTerm Absent = Iri("absent");

    [Fact]
    public void a_with_graph_view_never_denies_a_term_it_holds()
    {
        InMemoryDataset dataset = Filled();
        Assert.True(dataset.TryInternalise(Iri("g"), out TermHandle g));

        Assert.Contains(Iri("in-g"), QuadSourceContract.Holds(() => new DefaultGraphView(dataset, g), [Absent]));
        Assert.Contains(Iri("in-g"), QuadSourceContract.Holds(() => new DefaultGraphView(dataset, TermHandle.None), [Absent]));
    }

    /// <summary>The recorder notes an IRI it could not name; it denies only what its inner source denies.</summary>
    [Fact]
    public void a_read_recorder_never_denies_a_term_it_holds()
    {
        InMemoryDataset dataset = Filled();

        Assert.Contains(Iri("o"), QuadSourceContract.Holds(() => new ReadRecorder(dataset), [Absent]));
    }

    private static InMemoryDataset Filled()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("s"), Iri("p"), Iri("o"));
        builder.Add(Iri("s"), Iri("p"), Iri("in-g"), Iri("g"));
        return builder.ToDataset();
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + local));
}
