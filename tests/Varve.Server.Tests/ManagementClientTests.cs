// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Xunit;
using ClientDatasetStatus = Varve.Protocol.Client.Model.DatasetStatus;

namespace Varve.Server.Tests;

/// <summary>
/// The management plane's typed reads (ADR 0203 point 8) against the server's
/// own composition root: every read is a value with its facts, a refusal is a
/// problem with its request id, and what the client does not know is kept.
/// </summary>
public class ManagementClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> Anonymous() => new()
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:Datasets:notes:Storage"] = "Memory",
        ["Varve:Datasets:catalog:Storage"] = "Memory",
    };

    [Fact]
    public async Task The_list_names_each_dataset_with_its_state_storage_role_and_head_at_a_management_position()
    {
        await using RunningServer server = await RunningServer.StartAsync(Anonymous());
        VarveManagementClient manage = new(server.Client, server.Address, ClientLimits.Default, null, null);

        VarveResult<DatasetList> result = await manage.DatasetsAsync(Ct);

        Assert.Null(result.Problem);
        Assert.NotNull(result.Facts.ManagementPosition);
        DatasetEntry notes = Assert.Single(result.Value!.Datasets, entry => entry.Name == new DatasetName("notes"));
        Assert.Equal(DatasetState.Open, notes.State);
        Assert.Equal(StorageKind.Memory, notes.Storage);
        Assert.Equal(DatasetOrigin.Declared, notes.Origin);
        Assert.Equal(InstanceRole.Writer, notes.Role);
        Assert.NotNull(notes.Head);
        Assert.Contains("id", notes.Members.Keys);
        Assert.Equal(2, result.Value.Datasets.Count);
    }

    [Fact]
    public async Task A_dataset_answers_its_lifecycle_status_and_declaration()
    {
        await using RunningServer server = await RunningServer.StartAsync(Anonymous());
        VarveManagementClient manage = new(server.Client, server.Address, ClientLimits.Default, null, null);
        DatasetName notes = new("notes");

        VarveResult<DatasetLifecycle> lifecycle = await manage.LifecycleAsync(notes, Ct);
        Assert.Equal(DatasetState.Open, lifecycle.Value!.State);
        Assert.Equal(InstanceRole.Writer, lifecycle.Value.Role);
        Assert.Null(lifecycle.Value.Reason);

        VarveResult<ClientDatasetStatus> status = await manage.StatusAsync(notes, Ct);
        Assert.Null(status.Problem);
        Assert.Equal(status.Value!.Head, status.Facts.Position);
        Assert.Equal(new Durability("None"), status.Value.Durability); // a Memory dataset
        Assert.False(status.Value.Failed);
        Assert.Equal(new PositionLag(0), status.Value.Projection!.Lag);
        Assert.Empty(status.Value.Checkpoints);
        Assert.Contains("name", status.Value.Members.Keys);

        VarveResult<Declaration> declaration = await manage.DeclarationAsync(notes, Ct);
        string trig = Encoding.UTF8.GetString(declaration.Value!.Content.Span);
        Assert.Contains("varve:datasetName \"notes\"", trig, StringComparison.Ordinal);
        Assert.NotNull(declaration.Value.Version);
    }

    [Fact]
    public async Task The_system_plane_and_the_graphs_are_read_whole()
    {
        await using RunningServer server = await RunningServer.StartAsync(Anonymous());
        VarveManagementClient manage = new(server.Client, server.Address, ClientLimits.Default, null, null);

        VarveResult<SystemPlane> system = await manage.SystemAsync(Ct);
        Assert.Equal(InstanceRole.Writer, system.Value!.Role);
        Assert.Equal(new WriterEpoch(0), system.Value.Epoch);
        Assert.Equal(system.Value.Position, system.Value.Tail);
        Assert.Equal(new PositionLag(0), system.Value.Lag);
        Assert.Null(system.Value.Writer);

        VarveResult<Declaration> graphs = await manage.GraphsAsync(Ct);
        string trig = Encoding.UTF8.GetString(graphs.Value!.Content.Span);
        Assert.Contains("manage\\/dataset\\/notes", trig, StringComparison.Ordinal);
        Assert.Contains("manage\\/dataset\\/catalog", trig, StringComparison.Ordinal);

        VarveResult<Declaration> fleet = await manage.FleetAsync(Ct);
        Assert.Null(fleet.Problem);
    }

    [Fact]
    public async Task Placement_is_read_from_the_declarations_and_an_unplaced_dataset_is_absent()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous" });
        await RunningServer.DeclareAsync(server.Started!.Fleet, """
            @prefix varve: <https://w3id.org/varve/ns#> .
            @prefix n: <https://notes.example/manage/> .
            varve:manage\/dataset\/notes { n:notes a varve:Dataset ; varve:datasetName "notes" ; varve:storage varve:Memory ; varve:placedOn "varve-1" . }
            varve:manage\/dataset\/catalog { n:catalog a varve:Dataset ; varve:datasetName "catalog" ; varve:storage varve:Memory . }
            """);
        VarveManagementClient manage = new(server.Client, server.Address, ClientLimits.Default, null, null);

        VarveResult<PlacementList> placements = await manage.PlacementsAsync(Ct);

        Assert.Null(placements.Problem);
        KeyValuePair<DatasetName, InstanceName> notes = Assert.Single(placements.Value!.Placements);
        Assert.Equal(new DatasetName("notes"), notes.Key);
        Assert.Equal(new InstanceName("varve-1"), notes.Value);
    }

    [Fact]
    public async Task Grants_are_read_with_their_grantee_permission_and_scope_and_pinned_on_the_management_log()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous" });
        await RunningServer.DeclareAsync(server.Started!.Fleet, """
            @prefix varve: <https://w3id.org/varve/ns#> .
            @prefix n: <https://notes.example/manage/> .
            varve:manage\/dataset\/notes {
              n:notes a varve:Dataset ; varve:datasetName "notes" ; varve:storage varve:Memory .
              n:users a varve:Role ; varve:claimValue "Notes.User" .
              n:own a varve:GraphTemplate ; varve:template "https://notes.example/users/{sub}/" ; varve:match varve:Prefix .
              n:users-own a varve:Grant ; varve:grantee n:users ; varve:permission varve:write ; varve:graphTemplate n:own .
              n:users-shared a varve:Grant ; varve:grantee n:users ; varve:permission varve:read ; varve:graph <https://notes.example/shared> .
            }
            """);
        VarveManagementClient manage = new(server.Client, server.Address, ClientLimits.Default, null, null);

        VarveResult<GrantList> grants = await manage.GrantsAsync(new DatasetName("notes"), null, Ct);

        Assert.Null(grants.Problem);
        Assert.Equal(2, grants.Value!.Grants.Count);
        GrantEntry shared = Assert.Single(grants.Value.Grants, grant => grant.Permission == GrantRight.Read);
        Assert.Equal(GranteeKind.Role, shared.GranteeKind);
        Assert.Equal(new ClaimValue("Notes.User"), shared.Grantee);
        Assert.Equal(new Uri("https://notes.example/shared"), Assert.Single(shared.Scope.Graphs));
        GrantEntry own = Assert.Single(grants.Value.Grants, grant => grant.Permission == GrantRight.Write);
        Assert.Equal(new GraphTemplate("https://notes.example/users/{sub}/"), Assert.Single(own.Scope.Templates));

        // Before the declaration, at management position 0, there was no dataset to grant on.
        VarveResult<GrantList> before = await manage.GrantsAsync(new DatasetName("notes"), new FeedPosition(0), Ct);
        Assert.Null(before.Value);
        Assert.NotNull(before.Problem);
    }

    [Fact]
    public async Task An_unknown_dataset_is_a_problem_with_its_request_id_never_an_exception()
    {
        await using RunningServer server = await RunningServer.StartAsync(Anonymous());
        VarveManagementClient manage = new(server.Client, server.Address, ClientLimits.Default, null, null);

        VarveResult<ClientDatasetStatus> status = await manage.StatusAsync(new DatasetName("nowhere"), Ct);

        Assert.Null(status.Value);
        Assert.Equal(HttpStatusCode.NotFound, status.Facts.Status);
        Assert.Equal(new ProblemType("https://w3id.org/varve/problems/dataset-not-found"), status.Problem!.Type);
        Assert.NotNull(status.Facts.RequestId);

        VarveResult<Declaration> declaration = await manage.DeclarationAsync(new DatasetName("nowhere"), Ct);
        Assert.Null(declaration.Value);
        Assert.Equal(HttpStatusCode.NotFound, declaration.Facts.Status);
    }

    [Fact]
    public async Task Each_request_carries_the_management_token_and_a_stale_one_is_asked_for_again()
    {
        List<string?> sent = [];
        int asked = 0;
        DateTimeOffset now = DateTimeOffset.UnixEpoch.AddDays(1);
        Clock clock = new(now);
        BearerTokenProvider tokens = _ =>
        {
            asked++;
            return ValueTask.FromResult<BearerToken?>(new BearerToken("manage-" + asked, asked == 1 ? now : now.AddMinutes(5)));
        };
        using HttpClient http = new(new Recorder(sent));
        VarveManagementClient manage = new(http, new Uri("https://varve.example/"), ClientLimits.Default, tokens, clock);

        VarveResult<SystemPlane> system = await manage.SystemAsync(Ct);

        Assert.Equal(["Bearer manage-2"], sent);
        Assert.Equal(new FeedPosition(3), system.Value!.Position);
        Assert.Throws<ArgumentException>(() => new VarveManagementClient(http, new Uri("https://varve.example/"), ClientLimits.Default, tokens, null));
    }

    private sealed class Recorder(List<string?> sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"role\":\"follower\",\"epoch\":7,\"position\":3,\"tail\":5,\"lag\":2,\"bootstrap\":\"recorded\",\"writer\":\"https://varve-1.example/\",\"warm\":true}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
