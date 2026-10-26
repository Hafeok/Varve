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
using Varve.Protocol.Model;
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Managed> _managed = new(StringComparer.Ordinal);

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

    // ---- after the move (ADR 0167) ----

    /// <summary>
    /// The history of <paramref name="name"/>'s event declarations after they
    /// moved to the management plane, applied up to system position
    /// <paramref name="upTo"/> at least, as far as the system dataset this
    /// instance holds reaches; none when they had not moved by then. Its
    /// <see cref="Moved.Applied"/> says how far it reached.
    /// </summary>
    internal async Task<Moved?> MovedAsync(IManagementPlane plane, Dataset dataset, DatasetName name, long upTo, CancellationToken cancellationToken)
    {
        Dataset system = plane.System;
        Managed managed = _managed.AddOrUpdate(name.Value, _ => new Managed(system), (_, known) => ReferenceEquals(known.System, system) ? known : new Managed(system));

        if (managed.Applied < upTo && managed.Applied < system.Head.Value)
        {
            await managed.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await ReplayAsync(managed, dataset, name, Math.Min(upTo, system.Head.Value), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                managed.Gate.Release();
            }
        }

        return managed.MovedAt is long at && at <= upTo ? new Moved(managed.History!, at, managed.Applied) : null;
    }

    /// <summary>
    /// The catalogue that renders <paramref name="commit"/>: from the system
    /// dataset when the management position it records is at or after the
    /// move's, and from the dataset's own history otherwise (ADR 0167's
    /// boundary). None when the commit records a position this instance has
    /// not applied, after one advance: it cannot say which declarations were
    /// in force.
    /// </summary>
    internal async Task<EventCatalogueSnapshot?> AtCommitAsync(ProtocolOptions options, Dataset dataset, DatasetName name, EventCatalogueHistory history, Commit commit, CancellationToken cancellationToken)
    {
        if (options.Plane is { } plane && Recorded(commit) is long recorded)
        {
            Moved? moved = await MovedAsync(plane, dataset, name, recorded, cancellationToken).ConfigureAwait(false);

            if (moved is null && plane.System.Head.Value < recorded)
            {
                // The commit was authorised at a position this instance has not applied yet.
                if (options.Management is { } management)
                {
                    await management.AdvanceAsync(cancellationToken).ConfigureAwait(false);
                }

                if (plane.System.Head.Value < recorded)
                {
                    return null;
                }

                moved = await MovedAsync(plane, dataset, name, recorded, cancellationToken).ConfigureAwait(false);
            }

            if (moved is { } after)
            {
                return after.History.At(new FeedPosition(recorded));
            }
        }

        return history.At(new FeedPosition(commit.Position.Value));
    }

    /// <summary>The management position a commit records (ADR 0165), when it records one.</summary>
    internal static long? Recorded(Commit commit)
    {
        List<RdfTerm> attachments = [];

        foreach (TermHandle handle in commit.Attachments.Span)
        {
            if (commit.TryExternalise(handle, out RdfTerm? term))
            {
                attachments.Add(term);
            }
        }

        return ManagementOrder.PositionOf(attachments)?.Value;
    }

    /// <summary>The management position the commit at <paramref name="position"/> records, when it records one.</summary>
    internal static async Task<long?> RecordedAtAsync(Dataset dataset, Position position, CancellationToken cancellationToken)
    {
        if (position.Value == 0)
        {
            return null;
        }

        await foreach (Commit commit in dataset.Subscribe(new Position(position.Value - 1), SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))
        {
            return Recorded(commit);
        }

        return null;
    }

    private async Task ReplayAsync(Managed managed, Dataset dataset, DatasetName name, long upTo, CancellationToken cancellationToken)
    {
        if (managed.Applied >= upTo)
        {
            return;
        }

        RdfTerm graph = Varve.Management.ManagementVocabulary.DatasetGraph(name.Value);

        await foreach (Commit commit in managed.System.Subscribe(new Position(managed.Applied), SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))
        {
            if (commit.Position.Value > upTo)
            {
                break;
            }

            bool touched = Collect(commit, graph, managed.Graph);

            if (touched && managed.MovedAt is null && IsMoved(managed.Graph))
            {
                // The switch: the catalogue at the move, which the freeze made final.
                EventCatalogueHistory before = await UpToAsync(dataset, dataset.Head, cancellationToken).ConfigureAwait(false);
                managed.History = EventCatalogueHistory.ContinueFrom(before.Current);
                managed.MovedAt = commit.Position.Value;
            }

            if (touched && managed.History is { } history)
            {
                List<FeedChange> now = ManagedEventDeclarations.ToCatalogue(managed.Graph, []);
                history.Apply(new FeedPosition(commit.Position.Value), ManagedEventDeclarations.Changes(managed.Catalogue, now));
                managed.Catalogue = now;
            }

            managed.Applied = commit.Position.Value;

            if (commit.Position.Value >= upTo)
            {
                break;
            }
        }
    }

    // Whether the dataset's graph says varve:eventDeclarations varve:Moved.
    private static bool IsMoved(HashSet<(RdfTerm, RdfTerm, RdfTerm)> graph)
    {
        foreach ((RdfTerm _, RdfTerm p, RdfTerm o) in graph)
        {
            if (p.Equals(Varve.Management.ManagementVocabulary.EventDeclarationsProperty) && o.Equals(Varve.Management.ManagementVocabulary.MovedState))
            {
                return true;
            }
        }

        return false;
    }

    // A system commit's changes to one dataset's graph, applied to its triples; whether it had any.
    private static bool Collect(Commit commit, RdfTerm graph, HashSet<(RdfTerm, RdfTerm, RdfTerm)> triples)
    {
        if (commit.Kind != CommitKind.Data)
        {
            return false;
        }

        Dictionary<TermHandle, bool> ours = [];
        bool asserted = Apply(commit, commit.Delta.Asserted, graph, triples, ours, assert: true);
        bool retracted = Apply(commit, commit.Delta.Retracted, graph, triples, ours, assert: false);
        return asserted || retracted;
    }

    private static bool Apply(Commit commit, System.ReadOnlySpan<Quad> quads, RdfTerm graph, HashSet<(RdfTerm, RdfTerm, RdfTerm)> triples, Dictionary<TermHandle, bool> ours, bool assert)
    {
        bool touched = false;

        foreach (Quad quad in quads)
        {
            if (quad.IsDefaultGraph)
            {
                continue;
            }

            if (!ours.TryGetValue(quad.Graph, out bool mine))
            {
                mine = commit.TryExternalise(quad.Graph, out RdfTerm? named) && named.Equals(graph);
                ours[quad.Graph] = mine;
            }

            if (mine
                && commit.TryExternalise(quad.Subject, out RdfTerm? s)
                && commit.TryExternalise(quad.Predicate, out RdfTerm? p)
                && commit.TryExternalise(quad.Object, out RdfTerm? o))
            {
                _ = assert ? triples.Add((s, p, o)) : triples.Remove((s, p, o));
                touched = true;
            }
        }

        return touched;
    }

    /// <summary>A dataset's event declarations after the move: the history, the move's position, and how far it is applied.</summary>
    internal sealed record Moved(EventCatalogueHistory History, long MovedAt, long Applied);

    private sealed class Entry
    {
        internal EventCatalogueHistory History { get; } = new();

        internal SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed class Managed(Dataset system)
    {
        internal Dataset System { get; } = system;

        internal SemaphoreSlim Gate { get; } = new(1, 1);

        internal HashSet<(RdfTerm, RdfTerm, RdfTerm)> Graph { get; } = [];

        internal List<FeedChange> Catalogue { get; set; } = [];

        internal long Applied { get; set; }

        internal long? MovedAt { get; set; }

        internal EventCatalogueHistory? History { get; set; }
    }
}
