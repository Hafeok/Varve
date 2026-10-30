// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve.Shacl.Sparql.Tests;

/// <summary>
/// A SPARQL constraint's source under the term-lookup contract (ADR 0022,
/// amended 2026-10-30; #191): a term denied is in no quad it yields. It
/// names the terms the query pre-binds by their handles and every other by
/// the data source, and loads nothing.
/// </summary>
public class QuadSourceContractTests
{
    [Fact]
    public void a_prebound_source_never_denies_a_term_it_holds()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("s"), Iri("p"), Iri("o"), Iri("data"));
        builder.Add(RdfTerm.BlankNode("b"u8), Iri("p"), Iri("of-a-blank"), Iri("data"));
        InMemoryDataset data = builder.ToDataset();
        Assert.True(data.TryInternalise(Iri("data"), out TermHandle graph));
        Dictionary<RdfTerm, TermHandle> bound = new(RdfTerm.Comparer);

        Assert.Contains(Iri("of-a-blank"), QuadSourceContract.Holds(() => new PreboundSource(data, GraphPattern.Named(graph), bound), [Iri("absent")]));
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + local));
}
