// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The query path from request to response, per solution (ADR 0095, 7a's
/// definition of done): the difference in bytes the whole process allocates
/// between two answers of different sizes, divided by the difference in
/// solutions (<c>docs/testing.md</c> §4). Process-wide, because the request
/// crosses Kestrel's threads; each size is measured several times and the
/// least reading kept, since another thread can only add. Process-wide also
/// means another test class's allocations count, so the class runs alone,
/// after every parallel test has finished: least-of-six is no defence against
/// a neighbour that allocates during every reading.
/// </summary>
[Collection(nameof(AllocationTests))]
public class AllocationTests
{
    private const int Small = 2_000;
    private const int Large = 6_000;

    /// <summary>
    /// The evaluator's row, <c>8 × (w + ⌈w/64⌉)</c> bytes and a 24-byte array
    /// header (<c>sparql-evaluation.md</c> §11), for the pattern's three slots:
    /// 56 bytes.
    /// </summary>
    private const long RowBytes = (8 * (3 + 1)) + 24;

    /// <summary>
    /// A solution costs the evaluator's row and nothing else: nothing from the
    /// protocol, nothing from the writer (<c>sparql-results.md</c> §4), and
    /// nothing from naming its terms, whether they repeat or each solution's
    /// subject is a term of its own. The terms were committed in this process,
    /// so the store's term cache holds them (ADR 0079); a term read cold from
    /// disk costs its term once, when the cache loads it, and not per solution.
    /// </summary>
    /// <remarks>
    /// With a caller whose scope is every graph the view is not wrapped (ADR
    /// 0106); with a scope that names the default graph the request runs
    /// through <see cref="Rdf.GraphScopedQuadSource"/>, which decides each
    /// graph once and not per quad: the same 56 bytes a solution.
    /// </remarks>
    [Theory]
    [InlineData("SELECT ?p ?o { ?s ?p ?o }", "all")]
    [InlineData("SELECT ?s ?o { ?s ?p ?o }", "all")]
    [InlineData("SELECT ?s ?o { ?s ?p ?o }", "scoped")]
    public async Task a_solution_costs_the_evaluators_row_and_nothing_else(string query, string scope)
    {
        long perSolution = await PerSolutionAsync(query, scope == "scoped");
        TestContext.Current.TestOutputHelper?.WriteLine(query + " (" + scope + "): " + perSolution + " bytes a solution");
        Assert.Equal(RowBytes, perSolution);
    }

    private static async Task<long> PerSolutionAsync(string query, bool scoped)
    {
        await using Dataset small = await StoreOf(Small);
        await using Dataset large = await StoreOf(Large);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("small", small);
        datasets.Add("large", large);
        Rdf.GraphScope readable = Rdf.GraphScope.Of([Rdf.RdfTerm.Iri("http://ex/g"u8)], [], Rdf.DefaultGraphAccess.Included);
        IAccessScopes scopes = scoped ? new DefaultGraphOnly(new Rdf.CallerScope(readable, readable, Rdf.AdminAccess.None)) : EveryoneEverything.Instance;
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, accessScopes: scopes, stopping: P.Ct);
        byte[] buffer = new byte[64 * 1024];

        async Task<long> Measure(string name)
        {
            long least = long.MaxValue;

            for (int i = 0; i < 6; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long before = GC.GetTotalAllocatedBytes(precise: true);
                // A raw socket, not HttpClient: the client shares the process,
                // and only the server's bytes are the query path's.
                using TcpClient client = new();
                await client.ConnectAsync(host.Address.Host, host.Address.Port, P.Ct);
                using NetworkStream stream = client.GetStream();
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                    "GET /datasets/" + name + "/sparql?query=" + Uri.EscapeDataString(query) + " HTTP/1.1\r\nHost: x\r\nAccept: application/sparql-results+json\r\nConnection: close\r\n\r\n"), P.Ct);

                while (await stream.ReadAsync(buffer, P.Ct) > 0)
                {
                }

                long after = GC.GetTotalAllocatedBytes(precise: true);
                least = Math.Min(least, after - before);
            }

            return least;
        }

        await Measure("small");
        long smallCost = await Measure("small");
        long largeCost = await Measure("large");
        return (largeCost - smallCost) / (Large - Small);
    }

    private static async Task<Dataset> StoreOf(int count)
    {
        Dataset dataset = await P.NewDatasetAsync();
        Store.Log.CommitRequest request = new();

        foreach (int i in Enumerable.Range(0, count))
        {
            request.Assert(Rdf.RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://ex/s" + i)), Rdf.RdfTerm.Iri("http://ex/p"u8), Rdf.RdfTerm.Iri("http://ex/o"u8));
        }

        await dataset.CommitAsync(request, P.Ct);
        return dataset;
    }
}

/// <summary>The scope seam answering one fixed scope, for the scoped reading.</summary>
internal sealed class DefaultGraphOnly(Rdf.CallerScope scope) : IAccessScopes
{
    public Rdf.CallerScope ScopesOf(System.Security.Claims.ClaimsPrincipal caller, Model.DatasetName dataset) => scope;
}

/// <summary>The collection that runs <see cref="AllocationTests"/> alone.</summary>
[CollectionDefinition(nameof(AllocationTests), DisableParallelization = true)]
public sealed class AllocationTestsRunAlone;
