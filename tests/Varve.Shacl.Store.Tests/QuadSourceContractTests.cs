// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Varve.Rdf;
using Varve.Shacl.Store.Model;
using Xunit;

namespace Varve.Shacl.Store.Tests;

/// <summary>
/// A binding's data graph under the term-lookup contract (ADR 0022, amended
/// 2026-10-30; #191): a term denied is in no quad it yields. It looks terms
/// up in the whole state and yields a subset of it, so it denies only what
/// the state does.
/// </summary>
public class QuadSourceContractTests
{
    [Fact]
    public void a_bindings_data_graph_never_denies_a_term_it_holds()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("s"), Iri("p"), Iri("in-data"), Iri("g/data"));
        builder.Add(Iri("S"), Iri("p"), Iri("in-shapes"), Iri("shapes"));
        builder.Add(Iri("s"), Iri("p"), Iri("elsewhere"), Iri("other"));
        InMemoryDataset state = builder.ToDataset();
        ShapesBinding binding = new(Iri("b"), Iri("shapes"), GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded), ValidationMode.Report);

        Assert.Contains(Iri("in-data"), QuadSourceContract.Holds(() => new DataGraphSource(state, binding), [Iri("absent"), Iri("in-shapes")]));
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + local));
}
