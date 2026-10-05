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
/// A checkpoint as the store holds it: a run over the blob's own bytes, read
/// in blocks where it lies (ADR 0041, ADR 0071), and the dictionary's
/// watermarks at its position.
/// </summary>
internal sealed class Checkpoint
{
    internal Checkpoint(LoadedRun loaded)
    {
        Loaded = loaded;
    }

    internal LoadedRun Loaded { get; }

    internal long Position => Loaded.Header.To;

    internal Run Run => Loaded.Run;

    internal long CanonicalCount => Loaded.CanonicalCount;

    internal long BlankCount => Loaded.BlankCount;

    internal byte[] HeaderHash => Loaded.Header.ToHash;

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
