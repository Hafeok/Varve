// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The promotion request (ADR 0185 decision 4):
/// <c>PUT /manage/datasets/{d}/state</c> with
/// <c>{"promote":{"source":…,"position":…}}</c>, answered by the instance
/// that receives it, and refused as a takeover is, with its reason.
/// </summary>
public sealed class PromotionRouteTests
{
    [Fact]
    public async Task a_promotion_is_asked_with_its_source_and_position_and_answered_with_the_epoch_or_a_reason()
    {
        Dataset system = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        Plane plane = new(system);

        await using (system)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(new ProtocolTestHost.Datasets(), map: (app, o) => app.MapVarveManagement(o, plane), stopping: P.Ct))
        {
            // Promoted: the epoch it holds now, and the position it writes on from.
            plane.Answer = new PromotionAnswer(PromotionRefusal.None, new TakeoverEpoch(4), new Position(120));
            HttpResponseMessage promoted = await host.Client.SendAsync(Promote("orders", """{"promote":{"source":"https://acct.blob.core.windows.net/primary","position":120}}"""), P.Ct);
            Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
            Assert.Equal("no-store", promoted.Headers.CacheControl?.ToString());

            using (JsonDocument body = JsonDocument.Parse(await promoted.Content.ReadAsStringAsync(P.Ct)))
            {
                Assert.Equal(4, body.RootElement.GetProperty("epoch").GetInt64());
                Assert.Equal(120, body.RootElement.GetProperty("position").GetInt64());
            }

            Assert.Equal((new DatasetName("orders"), new PromotionAsk("https://acct.blob.core.windows.net/primary", new Position(120))), plane.Asked);

            // Each refusal is takeover-refused with its reason, and nothing else.
            foreach ((PromotionRefusal refusal, string reason) in new[]
            {
                (PromotionRefusal.NotInWriterSet, "not-in-writer-set"), (PromotionRefusal.Cold, "cold"), (PromotionRefusal.NotAReplica, "not-a-replica"),
                (PromotionRefusal.SourceMismatch, "source-mismatch"), (PromotionRefusal.PositionMismatch, "position-mismatch"), (PromotionRefusal.EpochMoved, "epoch-moved"),
            })
            {
                plane.Answer = new PromotionAnswer(refusal, new TakeoverEpoch(3), new Position(118));
                HttpResponseMessage refused = await host.Client.SendAsync(Promote("orders", """{"promote":{"source":"https://acct.blob.core.windows.net/primary","position":120}}"""), P.Ct);
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                using JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct));
                Assert.Equal(ProblemType.TakeoverRefused.Value, problem.RootElement.GetProperty("type").GetString());
                Assert.Equal(reason, problem.RootElement.GetProperty("reason").GetString());
                Assert.Equal(3, problem.RootElement.GetProperty("epoch").GetInt64());

                if (refusal == PromotionRefusal.PositionMismatch)
                {
                    Assert.Contains("is 118, not the 120 asked for", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
                }
            }

            // An instance with no object store serves no replica.
            plane.Answer = null;
            HttpResponseMessage none = await host.Client.SendAsync(Promote("orders", """{"promote":{"source":"s3://primary","position":0}}"""), P.Ct);
            Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
            Assert.Contains("\"reason\":\"not-a-replica\"", await none.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);

            // A body that is not a whole promotion is not one: no source, no position.
            plane.Asked = null;
            await host.Client.SendAsync(Promote("orders", """{"promote":{"position":120}}"""), P.Ct);
            await host.Client.SendAsync(Promote("orders", """{"promote":{"source":"s3://primary"}}"""), P.Ct);
            Assert.Null(plane.Asked);
        }
    }

    private static HttpRequestMessage Promote(string dataset, string body) =>
        new(HttpMethod.Put, new Uri("manage/datasets/" + dataset + "/state", UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Plane(Dataset system) : IManagementPlane
    {
        public PromotionAnswer? Answer { get; set; }

        public (DatasetName Dataset, PromotionAsk Ask)? Asked { get; set; }

        public Dataset System => system;

        public GrantTable Table
        {
            get
            {
                using DatasetView view = system.Pin();
                return GrantTable.Compile(DeclarationReader.Read(view), new ManagementPosition(system.Head.Value), []);
            }
        }

        public ManagementStatus Status => new(ManagementRole.Writer, 0, new ManagementPosition(system.Head.Value), new ManagementPosition(system.Head.Value), BootstrapState.Absent, null);

        public ICommitValidator Validator { get; } = new ManagementValidator();

        public ValueTask<PromotionAnswer?> PromoteAsync(DatasetName dataset, PromotionAsk ask, CancellationToken cancellationToken)
        {
            Asked = (dataset, ask);
            return new ValueTask<PromotionAnswer?>(Answer);
        }
    }
}
