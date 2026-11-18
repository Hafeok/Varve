// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// A commit's header read without its delta (#310, for ADR 0246's index):
/// over generated histories, some commits spanning several records and
/// segments, each carrying an agent, a cause and attachments or none, the
/// header-only read of every position names the same metadata as the whole
/// commit read through the feed, before and after a reopen.
/// </summary>
public sealed class MetadataReadTests
{
    [Fact]
    public async Task The_header_alone_names_what_the_whole_commit_names_at_every_position()
    {
        Gen<(int Quads, int Attachments, bool Agent)[]> histories = Gen.Select(Gen.Int[0, 40], Gen.Int[0, 3], Gen.Bool).Array[1, 25];

        await histories.SampleAsync(async history =>
        {
            MemoryStorage storage = new();
            DatasetOptions options = T.Options(maxRecordBytes: 256, segmentBytes: 2048);

            await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct))
            {
                for (int i = 0; i < history.Length; i++)
                {
                    (int quads, int attachments, bool agent) = history[i];
                    CommitRequestBuilder request = new()
                    {
                        Metadata = new CommitMetadata
                        {
                            Agent = agent ? RequestTerm.FromTerm(T.Iri("agent")) : RequestTerm.None,
                            Cause = RequestTerm.FromTerm(T.Literal("cause-" + i)),
                            Attachments = [.. Enumerable.Range(0, attachments).Select(a => RequestTerm.FromTerm(RdfTerm.Literal(Encoding.UTF8.GetBytes("{\"n\":" + i + "," + a + "}"), RdfTerm.Iri("https://w3id.org/varve/ns#test"u8))))],
                        },
                    };

                    // At least one quad, so every commit is delivered by the feed.
                    for (int q = 0; q <= quads; q++)
                    {
                        request.Assert(T.Iri("s" + i + "-" + q), T.Iri("p"), T.Literal(new string('x', 40) + q));
                    }

                    Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, T.Ct)).Outcome);
                }

                await AgreeAsync(dataset);
            }

            await using Dataset reopened = await Dataset.OpenAsync(storage, options, T.Ct);
            await AgreeAsync(reopened);
        }, iter: 30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task A_position_that_is_not_a_closed_commit_is_refused(long beyond)
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), T.Id, T.Options(), T.Ct);
        await dataset.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await dataset.ReadMetadataAsync(new Position(beyond), T.Ct));
    }

    private static async Task AgreeAsync(Dataset dataset)
    {
        await foreach (Commit commit in dataset.Subscribe(new Position(0), SubscriptionFilter.All, T.Ct))
        {
            CommitHeaderTerms header = await dataset.ReadMetadataAsync(commit.Position, T.Ct);

            Assert.Equal(commit.Position, header.Position);
            Assert.Equal(commit.Kind, header.Kind);
            Assert.Equal(commit.Timestamp, header.Timestamp);
            Assert.Equal(Term(commit, commit.Agent), header.Agent);
            Assert.Equal(Term(commit, commit.Cause), header.Cause);
            Assert.Equal(Term(commit, commit.GraphScope), header.GraphScope);

            List<RdfTerm> attachments = [];

            foreach (TermHandle handle in commit.Attachments.Span)
            {
                attachments.Add(Term(commit, handle)!);
            }

            Assert.Equal(attachments, header.Attachments);

            if (commit.Position == dataset.Head)
            {
                break;
            }
        }
    }

    private static RdfTerm? Term(Commit commit, TermHandle handle) =>
        handle.IsNone ? null : commit.TryExternalise(handle, out RdfTerm? term) ? term : throw new InvalidOperationException("The commit does not carry its own metadata.");
}
