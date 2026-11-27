// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Varve.Management;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.Tests.Objects;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// #254, fix (c): a cancel the system writer has answered is in force at a
/// dataset's writer that is another instance, for the next write it takes.
/// Two of the server's own <see cref="Fleet"/>s run in one process over a
/// store in memory (#353's seam): <c>a</c> writes the system dataset, and
/// <c>b</c> writes dataset <c>d</c> and follows the system dataset. Neither
/// runs its interval loop, so <c>b</c> learns of a management commit only when
/// something advances it. A write reaches <c>b</c> through the host's own
/// management-position step, and commits to <c>d</c> with the validators a
/// write carries.
/// </summary>
public sealed class CancelledMoveAcrossInstancesTests
{
    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Catalogue = Varve + "events-catalogue/default";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Frozen at <c>a</c> and applied at <c>b</c>, then cancelled at <c>a</c>
    /// and not yet applied at <c>b</c>: a catalogue write at <c>b</c> is
    /// accepted, because <c>b</c> advances once before it decides a write to
    /// a dataset that is moving. Before the fix it was refused
    /// <c>409 event-catalogue-moved</c> until <c>b</c>'s next interval.
    /// </summary>
    [Fact]
    public async Task A_catalogue_write_at_the_datasets_writer_after_a_cancel_the_system_writer_answered_is_accepted()
    {
        await using Pair pair = await Pair.StartAsync();
        await pair.MoveStateAsync("Moving");
        await pair.B.AdvanceAsync(Ct);
        Assert.Equal(CommitOutcome.Rejected, (await pair.WriteCatalogueAsync("while frozen")).Outcome);

        // The cancel, answered by the system writer; b has not applied it.
        long cancelled = await pair.MoveStateAsync("InDataset");
        Assert.True(pair.B.Current.Table.Position.Value < cancelled);

        CommitResult written = await pair.WriteCatalogueAsync("after the cancel");
        Assert.True(written.Outcome == CommitOutcome.Committed, Describe(written));
        Assert.True(pair.B.Current.Table.Position.Value >= cancelled);
    }

    /// <summary>
    /// Frozen and applied at <c>b</c>, cancelled at <c>a</c>, and then the
    /// store holding the system dataset cannot be read by <c>b</c>: a
    /// catalogue write at <c>b</c> is refused <c>503 management-behind</c>,
    /// which the client sends again (ADR 0246 point 10), never
    /// <c>409 event-catalogue-moved</c>, which it does not. A write to any
    /// other graph is decided as before, on the grants last applied (ADR 0163
    /// point 8). Once the store answers again, the catalogue write is
    /// accepted.
    /// </summary>
    [Fact]
    public async Task With_the_management_plane_unreadable_a_catalogue_write_to_a_moving_dataset_is_management_behind()
    {
        await using Pair pair = await Pair.StartAsync();
        await pair.MoveStateAsync("Moving");
        await pair.B.AdvanceAsync(Ct);
        await pair.MoveStateAsync("InDataset");

        pair.Store.Hook = (operation, name) => name.ToString().StartsWith(Fleet.SystemName + "/", StringComparison.Ordinal)
            ? () => throw new IOException("the store does not answer")
            : null;

        CommitResult refused = await pair.WriteCatalogueAsync("unconfirmed");
        Assert.Equal(CommitOutcome.Rejected, refused.Outcome);
        Assert.Equal(ProblemType.ManagementBehind.Value, ProblemTypeOf(refused));
        Assert.Equal(CommitOutcome.Committed, (await pair.WriteAsync("https://example.org/data", "an ordinary write")).Outcome);

        pair.Store.Hook = null;
        Assert.Equal(CommitOutcome.Committed, (await pair.WriteCatalogueAsync("readable again")).Outcome);
    }

    private static string Describe(CommitResult result) =>
        result.Outcome + ": " + string.Join(" | ", result.Report.Select(term => Encoding.UTF8.GetString(term.Lexical)));

    // The problem type a validator's refusal names, as the protocol reads its report.
    private static string? ProblemTypeOf(CommitResult result)
    {
        foreach (RdfTerm term in result.Report)
        {
            string text = Encoding.UTF8.GetString(term.Lexical);

            if (text.Contains("management-behind", StringComparison.Ordinal))
            {
                return ProblemType.ManagementBehind.Value;
            }

            if (text.Contains("event-catalogue-moved", StringComparison.Ordinal))
            {
                return ProblemType.EventCatalogueMoved.Value;
            }
        }

        return null;
    }

    private sealed class Pair : IAsyncDisposable
    {
        private readonly string _caches;
        private readonly HttpContextAccessor _accessor;

        // The catalogue's comment on Paid, as last committed.
        private string? _comment;

        private Pair(MemoryObjectStore store, Fleet a, Fleet b, HttpContextAccessor accessor, string caches)
        {
            Store = store;
            A = a;
            B = b;
            _accessor = accessor;
            _caches = caches;
        }

        internal MemoryObjectStore Store { get; }

        internal Fleet A { get; }

        internal Fleet B { get; }

        internal static async Task<Pair> StartAsync()
        {
            MemoryObjectStore store = new();
            string caches = Directory.CreateTempSubdirectory("varve-cancel-fleet-").FullName;
            HttpContextAccessor accessor = new();
            Fleet a = await StartAsync("a", store, caches, accessor);
            await RunningServer.DeclareAsync(a, "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
                + "<" + ManagementVocabulary.DatasetGraphPrefix + "d> {\n"
                + "  <urn:fleet:d> a varve:Dataset ; varve:datasetName \"d\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"b\" .\n"
                + "}\n");
            Fleet b = await StartAsync("b", store, caches, accessor);
            await b.AdvanceAsync(Ct);
            Assert.True(b.Datasets.TryResolve(new DatasetName("d"), out Dataset? d) && !d.IsFollower);
            Pair pair = new(store, a, b, accessor, caches);
            Assert.Equal(CommitOutcome.Committed, (await pair.WriteAsync(Catalogue, "before the move", declare: true)).Outcome);
            return pair;
        }

        private static async Task<Fleet> StartAsync(string instance, MemoryObjectStore store, string caches, HttpContextAccessor accessor)
        {
            ServerSettings settings = new();
            settings.Auth.Mode = "Anonymous";
            settings.ObjectStorage.Provider = "Azure";
            settings.ObjectStorage.ContainerUrl = "https://fleet.invalid/cancel";
            settings.ObjectStorage.CachePath = Path.Combine(caches, instance);
            settings.ObjectStorage.Instance = instance;
            settings.Fleet.Writer = "a";
            settings.Fleet.FollowInterval = TimeSpan.FromMinutes(1);
            return await Fleet.StartAsync(settings, ObjectStores.Over(store, settings.ObjectStorage, TimeProvider.System), recordBootstrap: false, TimeProvider.System, NullLogger.Instance, accessor, Ct);
        }

        /// <summary>States d's event declarations at a, the system writer, as one management commit; answers its position.</summary>
        internal async Task<long> MoveStateAsync(string state)
        {
            RdfTerm node = RdfTerm.Iri("urn:fleet:d"u8);
            RdfTerm property = RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + "eventDeclarations"));
            RdfTerm graph = RdfTerm.Iri(Encoding.UTF8.GetBytes(ManagementVocabulary.DatasetGraphPrefix + "d"));
            CommitRequestBuilder request = new() { Validators = [A.Validator] };

            foreach (string other in new[] { "Moving", "InDataset", "Moved" })
            {
                if (other != state)
                {
                    request = request.Retract(node, property, RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + other)), graph);
                }
            }

            CommitResult result = await A.System.CommitAsync(request.Assert(node, property, RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + state)), graph), Ct);
            Assert.Equal(CommitOutcome.Committed, result.Outcome);
            await A.AdvanceAsync(Ct);
            return result.Position.Value;
        }

        internal Task<CommitResult> WriteCatalogueAsync(string comment) => WriteAsync(Catalogue, comment);

        /// <summary>A data write to d at b, as the host takes it: its management-position step, then a commit with the validators a write carries.</summary>
        internal async Task<CommitResult> WriteAsync(string graph, string comment, bool declare = false)
        {
            RdfTerm g = RdfTerm.Iri(Encoding.UTF8.GetBytes(graph));
            CommitRequestBuilder Quads(CommitRequestBuilder request)
            {
                RdfTerm paid = RdfTerm.Iri("https://example.org/types/Paid"u8);
                RdfTerm said = RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#comment"u8);

                // One comment at a time: the declaration's shape allows one.
                if (graph == Catalogue && _comment is { } previous)
                {
                    request = request.Retract(paid, said, RdfTerm.Literal(Encoding.UTF8.GetBytes(previous)), g);
                }

                request = request.Assert(paid, said, RdfTerm.Literal(Encoding.UTF8.GetBytes(comment)), g);

                if (!declare)
                {
                    return request;
                }

                RdfTerm total = RdfTerm.Iri("https://example.org/types/Paid/total"u8);
                RdfTerm type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
                static RdfTerm V(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + local));
                static RdfTerm Typed(string lexical, string datatype) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical), RdfTerm.Iri(Encoding.UTF8.GetBytes("http://www.w3.org/2001/XMLSchema#" + datatype)));
                return request
                    .Assert(paid, type, V("EventType"), g)
                    .Assert(paid, V("eventName"), RdfTerm.Literal("Paid"u8), g)
                    .Assert(paid, V("eventClass"), RdfTerm.Iri("https://example.org/ns#Paid"u8), g)
                    .Assert(paid, V("eventVersion"), Typed("1", "integer"), g)
                    .Assert(paid, V("eventField"), total, g)
                    .Assert(paid, V("eventSelect"), RdfTerm.Literal("SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }"u8), g)
                    .Assert(total, type, V("EventField"), g)
                    .Assert(total, V("fieldName"), RdfTerm.Literal("total"u8), g)
                    .Assert(total, V("fieldDatatype"), RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#decimal"u8), g)
                    .Assert(total, V("fieldRequired"), Typed("true", "boolean"), g);
            }

            DefaultHttpContext context = new();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/datasets/d/sparql";
            _accessor.HttpContext = context;
            CommitResult? result = null;

            try
            {
                await ServerHost.ManagementPositionAsync(B)(context, async _ =>
                {
                    Assert.True(B.Datasets.TryResolve(new DatasetName("d"), out Dataset? d));
                    result = await d.CommitAsync(Quads(new CommitRequestBuilder { Validators = [B.OrderOf(new DatasetName("d"), B.Applied)] }), Ct);
                });
            }
            finally
            {
                _accessor.HttpContext = null;
            }

            CommitResult taken = result ?? throw new InvalidOperationException("The host's step did not pass the write on.");

            if (graph == Catalogue && taken.Outcome == CommitOutcome.Committed)
            {
                _comment = comment;
            }

            return taken;
        }

        public async ValueTask DisposeAsync()
        {
            await B.DisposeAsync();
            await A.DisposeAsync();
            Directory.Delete(_caches, recursive: true);
        }
    }
}
