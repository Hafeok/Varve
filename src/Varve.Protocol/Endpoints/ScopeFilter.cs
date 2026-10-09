// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Varve.Rdf;

namespace Varve.Protocol.Endpoints;

/// <summary>How a filter names the term behind a handle.</summary>
internal delegate bool TryName(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term);

/// <summary>
/// The readable scope applied to a delta (ADR 0107): the feed's and the
/// diff's. A graph is decided once per handle for the filter's lifetime —
/// a feed names few graphs and many quads — by naming it and asking the
/// scope; a handle the namer cannot name is hidden.
/// </summary>
internal sealed class ScopeFilter(GraphScope readable)
{
    private readonly Dictionary<TermHandle, bool> _decided = [];

    /// <summary>Whether the filter lets everything through.</summary>
    internal bool IsAll => readable.IsAll;

    /// <summary>The quads of <paramref name="delta"/> in readable graphs, or <paramref name="delta"/> itself when every graph is.</summary>
    internal QuadDelta Apply(QuadDelta delta, TryName names)
    {
        if (readable.IsAll)
        {
            return delta;
        }

        List<Quad> asserted = [];
        List<Quad> retracted = [];
        Keep(delta.Asserted, names, asserted);
        Keep(delta.Retracted, names, retracted);
        return asserted.Count == delta.Asserted.Length && retracted.Count == delta.Retracted.Length
            ? delta
            : QuadDelta.Create([.. asserted], [.. retracted]);
    }

    private void Keep(System.ReadOnlySpan<Quad> quads, TryName names, List<Quad> into)
    {
        foreach (Quad quad in quads)
        {
            if (Allows(quad.Graph, names))
            {
                into.Add(quad);
            }
        }
    }

    private bool Allows(TermHandle graph, TryName names)
    {
        if (graph.IsNone)
        {
            return readable.IncludesDefault;
        }

        if (_decided.TryGetValue(graph, out bool allowed))
        {
            return allowed;
        }

        allowed = names(graph, out RdfTerm? term) && readable.Allows(term);
        _decided[graph] = allowed;
        return allowed;
    }
}
