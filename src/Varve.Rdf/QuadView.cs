// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A quad seen without owning it: four <see cref="RdfTermView"/>s over one
/// arena, valid for as long as they are.
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly ref struct QuadView
{
    private readonly TermArena _arena;
    private readonly ReadOnlySpan<byte> _text;
    private readonly int _subject;
    private readonly int _predicate;
    private readonly int _object;
    private readonly int _graph;

    internal QuadView(
        TermArena arena,
        ReadOnlySpan<byte> text,
        int subject,
        int predicate,
        int @object,
        int graph)
    {
        _arena = arena;
        _text = text;
        _subject = subject;
        _predicate = predicate;
        _object = @object;
        _graph = graph;
    }

    /// <summary>The subject.</summary>
    public RdfTermView Subject => new(_arena, _text, _subject);

    /// <summary>The predicate.</summary>
    public RdfTermView Predicate => new(_arena, _text, _predicate);

    /// <summary>The object.</summary>
    public RdfTermView Object => new(_arena, _text, _object);

    /// <summary>Whether the quad names a graph. False means the default graph.</summary>
    public bool HasGraph => _graph >= 0;

    /// <summary>
    /// The graph name. Reading it when <see cref="HasGraph"/> is false throws,
    /// because there is no term to return and a default-graph sentinel would
    /// be a term that does not exist.
    /// </summary>
    public RdfTermView Graph =>
        _graph >= 0
            ? new RdfTermView(_arena, _text, _graph)
            : throw new InvalidOperationException("The quad is in the default graph and has no graph term.");
}
