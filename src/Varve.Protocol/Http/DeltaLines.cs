// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Buffers;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Protocol.Http;

/// <summary>
/// The store's side of <c>application/vnd.varve.delta</c> (ADR 0128): a
/// <see cref="Commit"/>'s position, kind, timestamp and handles mapped to
/// <see cref="DeltaWriter"/>, which owns the format and knows no store.
/// </summary>
internal static class DeltaLines
{
    /// <summary>A commit record, the empty line that ends it included.</summary>
    internal static void WriteCommit(IBufferWriter<byte> output, Commit commit, QuadDelta delta, TermNamer names) =>
        DeltaWriter.WriteCommit(output, Header(commit), delta, names);

    /// <summary>
    /// A commit record for a row-restricted caller: without the shapes
    /// bindings' report attachments, which it may not read (ADR 0237 point 6,
    /// ADR 0173 amended).
    /// </summary>
    internal static void WriteCommitWithoutReports(IBufferWriter<byte> output, Commit commit, QuadDelta delta, TermNamer names)
    {
        List<TermHandle> kept = [];

        foreach (TermHandle attachment in commit.Attachments.Span)
        {
            if (!(names(attachment, out RdfTerm? term) && term.Kind == RdfTermKind.TripleTerm && term.Predicate is { } predicate && predicate.Lexical.SequenceEqual(AffectedReport)))
            {
                kept.Add(attachment);
            }
        }

        DeltaWriter.WriteCommit(output, Header(commit) with { Attachments = kept.ToArray() }, delta, names);
    }

    private static ReadOnlySpan<byte> AffectedReport => "https://w3id.org/varve/ns#affectedReport"u8;

    /// <summary>A diff record, the empty line that ends it included.</summary>
    internal static void WriteDiff(IBufferWriter<byte> output, Position from, Position to, QuadDelta delta, TermNamer names) =>
        DeltaWriter.WriteDiff(output, new FeedPosition(from.Value), new FeedPosition(to.Value), delta, names);

    /// <summary>An error record: the last of a cut stream (ADR 0095).</summary>
    internal static void WriteError(IBufferWriter<byte> output, ProblemType problem) =>
        DeltaWriter.WriteError(output, FeedProblemType.Parse(problem.Value));

    /// <summary>One record as a server-sent event (<c>change-feed.md</c> §3).</summary>
    internal static void WriteEvent(IBufferWriter<byte> output, ReadOnlySpan<byte> eventName, Position? id, ReadOnlySpan<byte> record) =>
        DeltaWriter.WriteEvent(output, eventName, id is Position position ? new FeedPosition(position.Value) : null, record);

    /// <summary>The header a commit's record carries, in the format's own types.</summary>
    internal static FeedCommitHeader Header(Commit commit) => new(
        new FeedPosition(commit.Position.Value),
        commit.Kind switch
        {
            CommitKind.Data => FeedCommitKind.Data,
            CommitKind.Settings => FeedCommitKind.Settings,
            _ => FeedCommitKind.Erasure,
        },
        new FeedTimestamp(commit.Timestamp.Value),
        commit.Agent,
        commit.Cause,
        commit.GraphScope,
        commit.Attachments);
}
