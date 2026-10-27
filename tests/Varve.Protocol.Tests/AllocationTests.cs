// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The query path from request to response, per solution (ADR 0095, 7a's
/// definition of done): the difference in bytes the whole process allocates
/// between two answers of different sizes, divided by the difference in
/// solutions (<c>docs/testing.md</c> §4). Process-wide, because the request
/// crosses Kestrel's threads; the two sizes are read interleaved, so that what
/// the process does over time falls on both, and each is the least of
/// <see cref="AllocationMeter.Readings"/> readings, since another thread can
/// only add. The sizes are 16,000 solutions apart, so that one buffer block
/// a side (a pool miss, an 8 KB allocation quantum) is under a byte a
/// solution. Process-wide also means another test class's allocations count,
/// so the class runs alone, after every parallel test has finished.
/// </summary>
/// <remarks>
/// The request travels over the host's in-memory transport, not a socket
/// (2026-10-14, #91). Over a socket the figure read 57 on a loaded machine
/// and 55 on a Windows runner, on this code and on <c>v0.1.0-preview.4</c>
/// alike: when the reader falls behind, flushes wait, a waiting await boxes
/// its state machine, and a woken pipe reader is a thread-pool work item,
/// so what the transport allocates grows with how far behind the reader is.
/// The in-memory transport's pipes never make a writer wait and run their
/// continuations inline, and its blocks are never evicted, so the bytes
/// counted are the server's code and nothing that depends on timing: 56 in
/// six runs of four cases on four busy cores, where the socket read 57 in
/// every one.
/// </remarks>
[Collection(nameof(AllocationTests))]
public class AllocationTests
{
    private const int Small = 2_000;
    private const int Large = 18_000;

    private static readonly byte[] LastChunk = "0\r\n\r\n"u8.ToArray();

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
    /// 0107); with a scope that names the default graph the request runs
    /// through <see cref="Rdf.GraphScopedQuadSource"/>, which decides each
    /// graph once and not per quad: the same 56 bytes a solution. With an
    /// <see cref="ActivityListener"/> on <c>Varve.Protocol</c> the request
    /// runs inside a span whose attributes are set once, and the row count it
    /// records is a local incremented per solution (ADR 0112): the same 56.
    /// </remarks>
    [Theory]
    [InlineData("SELECT ?p ?o { ?s ?p ?o }", "all")]
    [InlineData("SELECT ?s ?o { ?s ?p ?o }", "all")]
    [InlineData("SELECT ?s ?o { ?s ?p ?o }", "scoped")]
    [InlineData("SELECT ?s ?o { ?s ?p ?o }", "traced")]
    public async Task a_solution_costs_the_evaluators_row_and_nothing_else(string query, string scope)
    {
        using ActivityListener? listener = scope == "traced" ? Listening() : null;
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
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, accessScopes: scopes, inMemory: true, stopping: P.Ct);

        byte[] tail = new byte[LastChunk.Length];

        async Task<long> ReadOnce(string name)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            // The in-memory transport, not a socket: what a socket allocates
            // depends on how much is in flight, so on how fast this reader
            // keeps up with the server, and only the server's code is the
            // query path's (see the class remarks).
            System.IO.Pipelines.IDuplexPipe connection = await host.ConnectInMemoryAsync();
            await connection.Output.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
                "GET /datasets/" + name + "/sparql?query=" + Uri.EscapeDataString(query) + " HTTP/1.1\r\nHost: x\r\nAccept: application/sparql-results+json\r\nConnection: close\r\n\r\n"), P.Ct);

            // To the chunked body's last chunk: the transport stays open after
            // it. The last five bytes stay in the pipe, so the end is seen
            // whole however the response is split.
            while (true)
            {
                System.IO.Pipelines.ReadResult read = await connection.Input.ReadAsync(P.Ct);
                System.Buffers.ReadOnlySequence<byte> buffer = read.Buffer;
                long keep = Math.Min(buffer.Length, LastChunk.Length);
                System.Buffers.BuffersExtensions.CopyTo(buffer.Slice(buffer.Length - keep), tail);
                bool ended = keep == LastChunk.Length && tail.AsSpan().SequenceEqual(LastChunk);
                connection.Input.AdvanceTo(buffer.GetPosition(buffer.Length - keep), buffer.End);

                if (ended || read.IsCompleted)
                {
                    break;
                }
            }

            await connection.Input.CompleteAsync();
            await connection.Output.CompleteAsync();

            long after = GC.GetTotalAllocatedBytes(precise: true);
            return after - before;
        }

        // Warm both paths once, then the readings, interleaved (§4).
        await ReadOnce("small");
        await ReadOnce("large");
        long smallCost = long.MaxValue, largeCost = long.MaxValue;

        for (int reading = 0; reading < AllocationMeter.Readings; reading++)
        {
            smallCost = Math.Min(smallCost, await ReadOnce("small"));
            largeCost = Math.Min(largeCost, await ReadOnce("large"));
        }

        return (largeCost - smallCost) / (Large - Small);
    }

    // A listener that records every span of the protocol's source, as the
    // server's exporter would.
    private static ActivityListener Listening()
    {
        ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == TelemetryOptions.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task<Dataset> StoreOf(int count)
    {
        Dataset dataset = await P.NewDatasetAsync();
        Store.Log.CommitRequestBuilder request = new();

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
