// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using Varve.Protocol.Http;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// Writes <c>application/vnd.varve.delta; version=1</c>
/// (<c>docs/spec/change-feed.md</c> §2), the format <see cref="ChangeFeedReader"/>
/// reads: a commit record from a delivered commit, a diff record from a
/// delta. The feed endpoint writes with it, and so does a host that writes
/// the feed without a server — the CLI's <c>feed</c> on a local dataset
/// (ADR 0104).
/// </summary>
public static class ChangeFeedWriter
{
    /// <summary>A commit record, with the commit's own delta, the empty line that ends it included.</summary>
    public static void WriteCommit(IBufferWriter<byte> output, Commit commit)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(commit);
        DeltaLines.WriteCommit(output, commit, commit.Delta, commit.TryExternalise);
    }

    /// <summary>A commit record with <paramref name="delta"/> in place of the commit's own: a filtered feed (spec §8).</summary>
    public static void WriteCommit(IBufferWriter<byte> output, Commit commit, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(commit);
        DeltaLines.WriteCommit(output, commit, delta, commit.TryExternalise);
    }

    /// <summary>A diff record between two positions, the terms named by <paramref name="source"/>.</summary>
    public static void WriteDiff(IBufferWriter<byte> output, Position from, Position to, QuadDelta delta, IQuadSource source)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);
        DeltaLines.WriteDiff(output, from, to, delta, source.TryExternalise);
    }
}
