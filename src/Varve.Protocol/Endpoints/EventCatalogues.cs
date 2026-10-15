// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Events;
using Varve.Events.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// Each dataset's catalogue history (ADR 0144, point 5), replayed from the
/// log the first time a request needs it and kept current by every request
/// that reads further: the declarations, versions and revisions at any
/// position, with no as-of read.
/// </summary>
internal sealed class EventCatalogues
{
    private readonly ConditionalWeakTable<Dataset, Entry> _entries = [];

    /// <summary>The history of <paramref name="dataset"/>, applied up to <paramref name="upTo"/> at least.</summary>
    internal async Task<EventCatalogueHistory> UpToAsync(Dataset dataset, Position upTo, CancellationToken cancellationToken)
    {
        Entry entry = _entries.GetValue(dataset, static _ => new Entry());

        if (entry.History.Position.Value >= upTo.Value)
        {
            return entry.History;
        }

        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (entry.History.Position.Value < upTo.Value)
            {
                await foreach (Commit commit in dataset.Subscribe(new Position(entry.History.Position.Value), SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))
                {
                    Apply(entry.History, commit);

                    if (commit.Position.Value >= upTo.Value)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            entry.Gate.Release();
        }

        return entry.History;
    }

    /// <summary>Applies one commit's changes to catalogue graphs; a position already applied is ignored by the history.</summary>
    internal static void Apply(EventCatalogueHistory history, Commit commit) =>
        history.Apply(new FeedPosition(commit.Position.Value), CatalogueChanges(commit));

    /// <summary>A commit's changes to graphs under the catalogue namespace, as terms.</summary>
    internal static List<FeedChange> CatalogueChanges(Commit commit)
    {
        List<FeedChange> changes = [];

        if (commit.Kind != CommitKind.Data)
        {
            return changes;
        }

        Dictionary<TermHandle, RdfTerm?> graphs = new(commit.Delta.Count.Value > 0 ? 4 : 0);
        Collect(commit, commit.Delta.Asserted, FeedChangeKind.Assert, graphs, changes);
        Collect(commit, commit.Delta.Retracted, FeedChangeKind.Retract, graphs, changes);
        return changes;
    }

    /// <summary>The commit's asserted <c>rdf:type</c>s: its candidate events (docs/spec/events.md §4.1).</summary>
    internal static List<TypeAssertion> TypeAssertions(Commit commit)
    {
        List<TypeAssertion> assertions = [];

        if (commit.Kind != CommitKind.Data)
        {
            return assertions;
        }

        foreach (Quad quad in commit.Delta.Asserted)
        {
            if (commit.TryExternalise(quad.Predicate, out RdfTerm? predicate)
                && predicate.Kind == RdfTermKind.Iri
                && predicate.Lexical.SequenceEqual(RdfType)
                && commit.TryExternalise(quad.Subject, out RdfTerm? node)
                && commit.TryExternalise(quad.Object, out RdfTerm? @class))
            {
                RdfTerm? graph = null;

                if (!quad.IsDefaultGraph && !commit.TryExternalise(quad.Graph, out graph))
                {
                    continue;
                }

                assertions.Add(new TypeAssertion(node, @class, graph));
            }
        }

        return assertions;
    }

    private static System.ReadOnlySpan<byte> RdfType => "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8;

    private static void Collect(Commit commit, System.ReadOnlySpan<Quad> quads, FeedChangeKind kind, Dictionary<TermHandle, RdfTerm?> graphs, List<FeedChange> changes)
    {
        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph)
            {
                continue;
            }

            if (!graphs.TryGetValue(quad.Graph, out RdfTerm? graph))
            {
                graph = commit.TryExternalise(quad.Graph, out RdfTerm? term) && EventCatalogue.IsUnderNamespace(term) ? term : null;
                graphs[quad.Graph] = graph;
            }

            if (graph is not null
                && commit.TryExternalise(quad.Subject, out RdfTerm? s)
                && commit.TryExternalise(quad.Predicate, out RdfTerm? p)
                && commit.TryExternalise(quad.Object, out RdfTerm? o))
            {
                changes.Add(new FeedChange(kind, s, p, o, graph));
            }
        }
    }

    private sealed class Entry
    {
        internal EventCatalogueHistory History { get; } = new();

        internal SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
