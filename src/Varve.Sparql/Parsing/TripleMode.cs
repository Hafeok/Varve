// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Parsing;

/// <summary>What a triples block may contain, by where it is.</summary>
internal readonly struct TripleMode
{
    private TripleMode(bool allowPaths, bool allowVariables, bool allowBlankNodes, bool template)
    {
        AllowPaths = allowPaths;
        AllowVariables = allowVariables;
        AllowBlankNodes = allowBlankNodes;
        IsTemplate = template;
    }

    /// <summary>Property paths in the predicate position: a WHERE clause.</summary>
    internal bool AllowPaths { get; }

    /// <summary>Variables anywhere: everything but <c>INSERT DATA</c> and <c>DELETE DATA</c>.</summary>
    internal bool AllowVariables { get; }

    /// <summary>Blank nodes, written or generated: everything but the three DELETE forms.</summary>
    internal bool AllowBlankNodes { get; }

    /// <summary>A template rather than a pattern: blank node labels are not scoped to a basic graph pattern.</summary>
    internal bool IsTemplate { get; }

    /// <summary>A WHERE clause.</summary>
    internal static TripleMode Pattern => new(true, true, true, false);

    /// <summary>A CONSTRUCT or update template.</summary>
    internal static TripleMode Template(bool allowVariables, bool allowBlankNodes) => new(false, allowVariables, allowBlankNodes, true);

    /// <summary>The same rules, in data: <c>INSERT DATA</c> and <c>DELETE DATA</c> share a label scope per operation.</summary>
    internal static TripleMode Data(bool allowBlankNodes) => new(false, false, allowBlankNodes, false);
}
