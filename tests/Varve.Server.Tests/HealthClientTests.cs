// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Xunit;
using ClientDataset = Varve.Protocol.Client.Model.DatasetReadiness;
using ClientReadiness = Varve.Protocol.Client.Model.Readiness;

namespace Varve.Server.Tests;

/// <summary>
/// The client's typed read of readiness (ADR 0203) against the server's own
/// composition root: ready and not ready are both a readiness, the facts
/// carry the request id, and what the client does not know is kept.
/// </summary>
public class HealthClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_ready_server_answers_a_readiness_with_its_management_state_and_every_dataset()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:Datasets:d:Storage"] = "Memory" });
        VarveHealthClient health = new(server.Client, server.Address, ClientLimits.Default);

        Assert.True(await health.LiveAsync(Ct));
        VarveResult<ClientReadiness> result = await health.ReadyAsync(Ct);

        Assert.Null(result.Problem);
        Assert.Equal(HttpStatusCode.OK, result.Facts.Status);
        ClientReadiness readiness = Assert.IsType<ClientReadiness>(result.Value);
        Assert.True(readiness.IsReady);
        Assert.Equal(InstanceRole.Writer, readiness.Management?.Role);
        Assert.Equal(new WriterEpoch(0), readiness.Management?.Epoch);
        Assert.Equal(new PositionLag(0), readiness.Management?.Lag);
        // Anonymous mode has no bootstrap operators to record (ADR 0163 point 7).
        Assert.Equal(BootstrapState.Absent, readiness.Management?.Bootstrap);

        ClientDataset d = Assert.Single(readiness.Datasets);
        Assert.Equal(new DatasetName("d"), d.Name);
        Assert.Equal(ReadinessState.Ready, d.State);
        Assert.Equal(InstanceRole.Writer, d.Role);
        Assert.Equal(new PositionLag(0), d.Lag);
        Assert.Empty(d.Validation);

        // Every member of the entry is kept, those the model names included,
        // so a member a newer server adds reaches a caller.
        Assert.Equal(["lag", "role", "state"], Sorted(d.Members.Keys));
    }

    [Fact]
    public async Task A_draining_server_answers_a_readiness_beside_its_not_ready_problem_with_the_request_id()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:d:Storage"] = "Memory",
            ["Varve:Health:StopDelay"] = "00:00:03",
        });
        VarveHealthClient health = new(server.Client, server.Address, ClientLimits.Default);

        Task<int> stopping = server.StopAsync();
        VarveResult<ClientReadiness> result = await health.ReadyAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.Facts.Status);
        Problem problem = Assert.IsType<Problem>(result.Problem);
        Assert.Equal(ProblemType.NotReady, problem.Type);
        Assert.Equal("not-ready", problem.Type.Name);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, problem.Status);

        ClientReadiness readiness = Assert.IsType<ClientReadiness>(result.Value);
        Assert.False(readiness.IsReady);
        Assert.Equal("The server is draining.", readiness.Detail);
        Assert.Equal(InstanceRole.Writer, readiness.Management?.Role);
        Assert.Empty(readiness.Datasets);

        // The host writes this problem before any endpoint runs, so it names
        // its request only as instance; the facts take it from there.
        Assert.NotNull(problem.Instance);
        Assert.Equal(problem.Instance, result.Facts.RequestId);
        Assert.Equal(0, await stopping);
    }

    [Fact]
    public async Task A_problem_is_read_whole_and_its_unknown_members_are_kept()
    {
        Problem problem = Assert.IsType<Problem>(Problem.Parse(
            """{"type":"https://w3id.org/varve/problems/conflict","title":"Another commit came first.","status":409,"detail":"d moved on","instance":"a:1","headPosition":7,"later":{"x":1}}"""u8.ToArray(),
            HttpStatusCode.BadRequest));

        Assert.Equal(new ProblemType("https://w3id.org/varve/problems/conflict"), problem.Type);
        Assert.Equal("conflict", problem.Type.Name);
        Assert.Equal("Another commit came first.", problem.Title);
        Assert.Equal(HttpStatusCode.Conflict, problem.Status);
        Assert.Equal("d moved on", problem.Detail);
        Assert.Equal(new RequestId("a:1"), problem.Instance);
        Assert.Equal(["headPosition", "later"], Sorted(problem.Members.Keys));
        Assert.Equal(7, problem.Members["headPosition"].GetInt64());
    }

    [Fact]
    public void A_body_that_states_no_type_or_status_is_about_blank_at_the_response_status_and_one_that_is_not_an_object_is_none()
    {
        Problem problem = Assert.IsType<Problem>(Problem.Parse("""{"title":"t","status":"409","type":7}"""u8.ToArray(), HttpStatusCode.BadGateway));
        Assert.Equal(ProblemType.AboutBlank, problem.Type);
        Assert.Null(problem.Type.Name);
        Assert.Equal(HttpStatusCode.BadGateway, problem.Status);

        Assert.Null(Problem.Parse("[1]"u8.ToArray(), HttpStatusCode.BadGateway));
        Assert.Null(Problem.Parse("<html>"u8.ToArray(), HttpStatusCode.BadGateway));
    }

    private static List<string> Sorted(IEnumerable<string> keys)
    {
        List<string> sorted = [.. keys];
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }
}
