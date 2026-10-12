// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.CommandLine;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary><c>varve feed &lt;dataset&gt;</c>: the change feed in the format of <c>change-feed.md</c>, bounded or tailed.</summary>
internal static class Feed
{
    internal static Command Command(Target target, Io io)
    {
        Option<long> from = new("--from") { Description = "Start after this position.", DefaultValueFactory = _ => 0 };
        Option<long?> to = new("--to") { Description = "End at this position; the head by default, or never with --follow." };
        Option<string?> graph = new("--graph") { Description = "Only changes in this graph: its IRI, or `default`." };
        Option<bool> follow = new("--follow") { Description = "Keep tailing after the head." };
        Command command = new("feed", "Write a dataset's change feed, its commits in order, to standard output (ADRs 0097, 0118).");
        target.AddTo(command);
        command.Options.Add(from);
        command.Options.Add(to);
        command.Options.Add(graph);
        command.Options.Add(follow);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            long start = parsed.GetValue(from);
            long? end = parsed.GetValue(to);
            bool live = parsed.GetValue(follow);
            string? named = parsed.GetValue(graph);

            if (live && end is not null)
            {
                throw new Cli.CommandException("--to and --follow do not go together.");
            }

            await using Opened opened = await target.OpenAsync(parsed, io, cancellationToken).ConfigureAwait(false);

            if (opened is Remote remote)
            {
                using HttpResponseMessage response = await remote.Client.CommitsAsync(start, live ? null : end ?? await HeadAsync(remote, cancellationToken).ConfigureAwait(false), named, eventStream: false, cancellationToken).ConfigureAwait(false);
                return await Commands.CopyAsync(response, io, cancellationToken).ConfigureAwait(false);
            }

            return await LocalAsync((Local)opened, start, live ? null : end ?? ((Local)opened).Dataset.Head.Value, named, io, cancellationToken).ConfigureAwait(false);
        }, parsed.InvocationConfiguration.Error));
        return command;
    }

    private static async Task<long> HeadAsync(Remote remote, CancellationToken cancellationToken)
    {
        using HttpResponseMessage status = await remote.Client.StatusAsync(cancellationToken).ConfigureAwait(false);
        return Varve.Protocol.Client.SparqlHttpClient.PositionOf(status) ?? throw new Cli.CommandException("the server did not name the head; give --to.");
    }

    // The feed through the subscription (ADR 0042), each commit a record. The
    // graph filter is applied here, by naming each change's graph, so that a
    // graph the dataset has not met yet still matches when it arrives.
    private static async Task<int> LocalAsync(Local local, long from, long? to, string? named, Io io, CancellationToken cancellationToken)
    {
        Dataset dataset = local.Dataset;

        if (to is long end && end > dataset.Head.Value)
        {
            throw new Cli.CommandException("--to is after the head, " + dataset.Head.Value + ".");
        }

        if (to is long bounded && bounded <= from)
        {
            return 0;
        }

        StreamOutput output = new(io.Data);
        RdfTerm? graph = named is null or "default" ? null : RdfTerm.Iri(Encoding.UTF8.GetBytes(named));
        bool defaultOnly = named == "default";

        await foreach (Commit commit in dataset.Subscribe(new Position(from), SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))
        {
            if (to is long last && commit.Position.Value > last)
            {
                break;
            }

            if (named is null || commit.Kind != CommitKind.Data)
            {
                ChangeFeedWriter.WriteCommit(output, commit);
            }
            else
            {
                QuadDelta filtered = Filter(commit, graph, defaultOnly);

                if (!filtered.IsEmpty)
                {
                    ChangeFeedWriter.WriteCommit(output, commit, filtered);
                }
            }

            if (to is null || output.IsDue)
            {
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (to is long stop && commit.Position.Value >= stop)
            {
                break;
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static QuadDelta Filter(Commit commit, RdfTerm? graph, bool defaultOnly)
    {
        List<Quad> asserted = [];
        List<Quad> retracted = [];

        foreach (Quad quad in commit.Delta.Asserted)
        {
            if (Matches(commit, in quad, graph, defaultOnly))
            {
                asserted.Add(quad);
            }
        }

        foreach (Quad quad in commit.Delta.Retracted)
        {
            if (Matches(commit, in quad, graph, defaultOnly))
            {
                retracted.Add(quad);
            }
        }

        return QuadDelta.Create([.. asserted], [.. retracted]);
    }

    private static bool Matches(Commit commit, in Quad quad, RdfTerm? graph, bool defaultOnly)
    {
        if (defaultOnly)
        {
            return quad.Graph.IsNone;
        }

        return !quad.Graph.IsNone && commit.TryExternalise(quad.Graph, out RdfTerm? name) && RdfTerm.Comparer.Equals(name, graph);
    }
}
