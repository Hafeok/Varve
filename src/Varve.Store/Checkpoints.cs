// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A checkpoint as the store holds it: the runs whose merge is the state at
/// its position. Either a full run over the blob's own bytes, read in blocks
/// where it lies (ADR 0041, ADR 0071) — derived format 3's, kept and read
/// until dropped — or the extent sets from position 0 to it (ADR 0139), each
/// a window's exact delta, the last of them ending at the position.
/// </summary>
internal sealed class Checkpoint
{
    internal Checkpoint(LoadedRun loaded)
    {
        Loaded = loaded;
        Runs = [loaded.Run];
        Position = loaded.Header.To;
        HeaderHash = loaded.Header.ToHash;
        Graphs = GraphVersions.NameAt(Position);
    }

    internal Checkpoint(ExtentSet[] chain)
    {
        Chain = chain;
        Runs = new Run[chain.Length];

        for (int i = 0; i < chain.Length; i++)
        {
            Runs[i] = chain[i].Run;
        }

        Position = chain[^1].To;
        HeaderHash = chain[^1].ToHash;
        Graphs = chain[^1].Graphs;
    }

    /// <summary>The full run, for a checkpoint of derived format 3; null for extents.</summary>
    internal LoadedRun? Loaded { get; }

    /// <summary>The extent sets up to the position, oldest first; null for a full run.</summary>
    internal ExtentSet[]? Chain { get; }

    /// <summary>The newest set: the one this checkpoint owns, and the one it adds to the chain before it.</summary>
    internal ExtentSet? Set => Chain?[^1];

    internal long Position { get; }

    /// <summary>The runs, oldest first, whose merge is the state at the position.</summary>
    internal Run[] Runs { get; }

    internal byte[] HeaderHash { get; }

    /// <summary>The graph-version index at the position (ADR 0125).</summary>
    internal BlobName Graphs { get; }

    /// <summary>The blob this checkpoint owns: its full run, or its newest set.</summary>
    internal RunBlob Owned => Loaded?.Run.Blob ?? Set!.Run.Blob!;

    /// <summary>Takes a reference to every blob its runs read; false when one has been closed, and the caller looks again.</summary>
    internal bool TryAcquire()
    {
        for (int i = 0; i < Runs.Length; i++)
        {
            if (!Runs[i].Blob!.TryAcquire())
            {
                for (int j = 0; j < i; j++)
                {
                    Runs[j].Blob!.Release();
                }

                return false;
            }
        }

        return true;
    }

    internal void Release()
    {
        foreach (Run run in Runs)
        {
            run.Blob!.Release();
        }
    }

    internal const string Prefix = "checkpoints/";

    internal static BlobName Name(long position) => new(Prefix + position.ToString("D20", CultureInfo.InvariantCulture));

    internal static bool TryParseName(BlobName name, out long position)
    {
        position = 0;
        string value = name.Value;
        return value.Length == Prefix.Length + 20
            && value.StartsWith(Prefix, StringComparison.Ordinal)
            && long.TryParse(value.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out position);
    }
}

/// <summary>Composes a chain of exact deltas — consecutive commits — in one pass.</summary>
/// <remarks>
/// The specification's <c>;</c> is associative only over such chains
/// (specification 1.3, §6; ADR 0047), which is every use the store makes of it. A running net
/// state per quad composes a chain in time proportional to its total size.
/// </remarks>
internal sealed class DeltaChain
{
    private readonly System.Collections.Generic.Dictionary<Quad, bool> _net = [];

    internal void Add(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted)
    {
        foreach (Quad quad in asserted)
        {
            if (_net.TryGetValue(quad, out bool present) && !present)
            {
                _net.Remove(quad);
            }
            else
            {
                _net[quad] = true;
            }
        }

        foreach (Quad quad in retracted)
        {
            if (_net.TryGetValue(quad, out bool present) && present)
            {
                _net.Remove(quad);
            }
            else
            {
                _net[quad] = false;
            }
        }
    }

    internal QuadDelta ToDelta()
    {
        System.Collections.Generic.List<Quad> asserted = [];
        System.Collections.Generic.List<Quad> retracted = [];

        foreach (System.Collections.Generic.KeyValuePair<Quad, bool> entry in _net)
        {
            (entry.Value ? asserted : retracted).Add(entry.Key);
        }

        return QuadDelta.Create(CollectionsMarshal.AsSpan(asserted), CollectionsMarshal.AsSpan(retracted));
    }
}
