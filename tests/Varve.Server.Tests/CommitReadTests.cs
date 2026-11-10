// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The client's typed commit reads (ADR 0203): a range read whole, one commit
/// with its changes, and a commit that is not there as a problem.
/// </summary>
public class CommitReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_range_and_one_commit_are_read_whole_with_their_changes()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:Datasets:d:Storage"] = "Memory" });
        SparqlHttpClient client = new(server.Client, new Uri(server.Address, "datasets/d/"), ClientLimits.Default);

        foreach (string update in (string[])["INSERT DATA { <http://ex/a> <http://ex/p> 1 . <http://ex/a> <http://ex/q> 2 }", "DELETE DATA { <http://ex/a> <http://ex/q> 2 }"])
        {
            using HttpResponseMessage written = await client.UpdateAsync(update, (long?)null, Ct);
            Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        }

        VarveResult<CommitRange> range = await client.ReadCommitsAsync(new FeedPosition(0), new FeedPosition(2), Ct);
        Assert.Null(range.Problem);
        List<FeedRecord> data = [.. range.Value!.Commits.Where(commit => commit.CommitKind == FeedCommitKind.Data)];
        Assert.Equal(2, data.Count);
        Assert.Equal(2, data[0].Changes.Count(change => change.Kind == FeedChangeKind.Assert));
        Assert.Equal(FeedChangeKind.Retract, Assert.Single(data[1].Changes).Kind);
        Assert.Null(range.Value.Next);

        VarveResult<FeedRecord> one = await client.ReadCommitAsync(data[1].Position, Ct);
        Assert.Equal(data[1].Position, one.Value!.Position);
        Assert.Single(one.Value.Changes);

        VarveResult<FeedRecord> missing = await client.ReadCommitAsync(new FeedPosition(9999), Ct);
        Assert.Null(missing.Value);
        Assert.NotNull(missing.Problem);
        Assert.NotEqual(HttpStatusCode.OK, missing.Facts.Status);
    }
}
