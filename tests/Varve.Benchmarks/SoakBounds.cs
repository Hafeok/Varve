// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store;

namespace Varve.Benchmarks;

/// <summary>
/// The soak's bounds on <c>derived/</c> files and open handles, from the
/// configuration it runs (ADR 0215 point 3): each term is what one rule of the
/// store can hold at once, so a count past the sum is something the rules do
/// not account for.
/// </summary>
[DesignDecision(typeof(TheSoakGateJudged.BoundsFromTheConfiguration), Scope = ExceptionScope.Boundary)]
internal static class SoakBounds
{
    /// <summary>Checkpoints the soak's policy keeps (<see cref="CheckpointPolicy.Keep"/>); each is one extent set.</summary>
    internal const int KeepCheckpoints = 12;

    /// <summary>The soak's memtable, in quads: the size of the smallest run.</summary>
    internal const long MemtableQuads = 20_000;

    /// <summary>A set's files: six order extents, its terms, its graphs and its commits (ADR 0139).</summary>
    internal const int FilesPerSet = 9;

    /// <summary>The catalog, the projection's state, the graph index, the lock, its owner and <c>.gitignore</c>.</summary>
    internal const int FixedFiles = 6;

    /// <summary>A temporary file for the state and one for the graph index, each renamed into place.</summary>
    internal const int TemporaryFiles = 2;

    /// <summary>
    /// The runtime's own handles after the dataset opens: the thread pool's
    /// threads and their events, timers. Measured at 7 on Linux; Windows
    /// counts a handle per thread and per event, so the allowance is wider.
    /// </summary>
    internal const int RuntimeHandles = 64;

    /// <summary>
    /// Extent sets on disk at once: the kept checkpoints' sets; one more
    /// written before the oldest is dropped; one merge's output before its
    /// inputs are deleted; and, where the platform will not delete a file a
    /// reader has open, the inputs of the latest change, at most a fan-out of
    /// them, until the views that hold them close.
    /// </summary>
    internal static int Sets => KeepCheckpoints + 2 + ExtentMergePolicy.Default.FanOut;

    /// <summary>
    /// Disk runs: a run is merged into the one before it once it is a quarter
    /// of its size, so they shrink by more than four from oldest to newest,
    /// from the live quads down to a memtable; plus one being written and one
    /// merge's output.
    /// </summary>
    internal static int Runs(long quads) => (int)Math.Ceiling(Math.Log(Math.Max(quads, MemtableQuads) / (double)MemtableQuads, 4)) + 2;

    /// <summary>
    /// The commit index's blobs: a segment is merged into the one before it
    /// once it is as long, so they halve from oldest to newest, down to the
    /// cache that is paged out at a time; plus one being written and one
    /// merge's output.
    /// </summary>
    internal static int CommitBlobs(long commits) =>
        (int)Math.Floor(Math.Log(Math.Max(commits, CommitCache) / (double)CommitCache, 2)) + 2;

    private const int CommitCache = 4096;

    /// <summary>The <c>derived/</c> files the configuration allows at <paramref name="commits"/> commits and <paramref name="quads"/> live quads.</summary>
    internal static int DerivedFiles(long commits, long quads) =>
        FixedFiles + TemporaryFiles + (FilesPerSet * Sets) + Runs(quads) + CommitBlobs(commits);

    /// <summary>
    /// Open handles: the process's own before the dataset opened, a handle on
    /// each <c>derived/</c> file at most, the log's append handle and a reader
    /// on each segment read, and the runtime's own since.
    /// </summary>
    internal static int Handles(int before, long commits, long quads, int segments) =>
        before + DerivedFiles(commits, quads) + 1 + segments + RuntimeHandles;
}
