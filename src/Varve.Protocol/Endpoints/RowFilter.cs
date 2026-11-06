// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The rows a row-restricted caller sees, applied to commits and diffs (ADR
/// 0237 points 4 and 5): an addition judged by the classification after it,
/// a retraction by the one before; and, for the feed and the diff, the
/// quads whose visibility changed by reclassification although no quad did,
/// so that the caller's fold equals its view. Built only for a caller whose
/// row scope restricts something; it reads two as-of views per commit, the
/// cost ADR 0237 states.
/// </summary>
internal sealed class RowFilter
{
    private readonly Exchange _exchange;
    private readonly IRowClassifications _classifications;

    private RowFilter(Exchange exchange, IRowClassifications classifications)
    {
        _exchange = exchange;
        _classifications = classifications;
    }

    /// <summary>The filter for <paramref name="exchange"/>'s caller; null when it reads every row.</summary>
    internal static RowFilter? For(Exchange exchange) =>
        !exchange.Scope.ReadableRows.IsAll && exchange.Options.RowClassifications is { } classifications && classifications.Classifier(exchange.Name) is not null
            ? new RowFilter(exchange, classifications)
            : null;

    /// <summary>
    /// A data commit's delta, already cut to the readable graphs, as the
    /// caller sees it; with <paramref name="view"/>, the delta of the caller's
    /// view, reclassified quads included.
    /// </summary>
    internal async ValueTask<QuadDelta> CommitAsync(Commit commit, QuadDelta graphs, bool view, CancellationToken cancellationToken)
    {
        Position after = commit.Position;
        Position before = new(after.Value - 1);
        return await BetweenAsync(before, after, graphs, view ? _classifications.ChangedAt(_exchange.Name, after) : [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The diff between two positions, already cut to the readable graphs, as the caller's views differ.</summary>
    internal async ValueTask<QuadDelta> DiffAsync(Position from, Position to, QuadDelta graphs, CancellationToken cancellationToken)
    {
        HashSet<TermHandle> changed = [];

        for (long p = from.Value + 1; p <= to.Value; p++)
        {
            foreach (TermHandle node in _classifications.ChangedAt(_exchange.Name, new Position(p)))
            {
                changed.Add(node);
            }
        }

        return await BetweenAsync(from, to, graphs, [.. changed], cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<QuadDelta> BetweenAsync(Position from, Position to, QuadDelta graphs, IReadOnlyList<TermHandle> changed, CancellationToken cancellationToken)
    {
        using DatasetView earlier = await _exchange.Dataset.AsOfAsync(from, cancellationToken).ConfigureAwait(false);
        using DatasetView later = await _exchange.Dataset.AsOfAsync(to, cancellationToken).ConfigureAwait(false);
        Side? seenBefore = Seen(earlier);
        Side? seenAfter = Seen(later);
        HashSet<Quad> asserted = [];
        HashSet<Quad> retracted = [];

        foreach (Quad quad in graphs.Asserted)
        {
            if (seenAfter is null || seenAfter.Rows.Allows(in quad))
            {
                asserted.Add(quad);
            }
        }

        foreach (Quad quad in graphs.Retracted)
        {
            if (seenBefore is null || seenBefore.Rows.Allows(in quad))
            {
                retracted.Add(quad);
            }
        }

        // A reclassified node's quads enter or leave the view though they
        // did not change: in its row, and wherever it is an object.
        if (changed.Count > 0 && seenBefore is not null && seenAfter is not null)
        {
            foreach (TermHandle node in changed)
            {
                Reclassified(seenAfter, seenBefore, node, asserted);
                Reclassified(seenBefore, seenAfter, node, retracted);
            }
        }

        return QuadDelta.Create([.. asserted], [.. retracted]);
    }

    // Quads of `node` this side shows and the other side, holding them, does not.
    private static void Reclassified(Side shows, Side other, TermHandle node, HashSet<Quad> into)
    {
        foreach (bool asSubject in new[] { true, false })
        {
            using IQuadCursor cursor = asSubject
                ? shows.Rows.Match(node, TermHandle.None, TermHandle.None, GraphPattern.Any)
                : shows.Rows.Match(TermHandle.None, TermHandle.None, node, GraphPattern.Any);

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;

                if (other.Graphs.Contains(in quad) && !other.Rows.Allows(in quad))
                {
                    into.Add(quad);
                }
            }
        }
    }

    private Side? Seen(DatasetView view)
    {
        if (_classifications.At(_exchange.Name, view) is not { } rows)
        {
            return null;
        }

        IQuadSource graphs = GraphScopedQuadSource.Wrap(view, _exchange.Scope.Readable);
        return new Side(graphs, new RowScopedQuadSource(graphs, rows, _exchange.Scope.ReadableRows));
    }

    // One state as the caller's graphs show it, and as its rows do.
    private sealed record Side(IQuadSource Graphs, RowScopedQuadSource Rows);
}
