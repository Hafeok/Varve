// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Shacl.Validation;

/// <summary>
/// An RDF term during validation: the data source's handle when the source
/// holds the term, and otherwise the term itself — a shape constant or a
/// target the data does not mention, which matches nothing in the data and
/// was never added to it (ADR 0151, <c>ShapeConstantsLookedUpOnly</c>).
/// </summary>
internal readonly struct Node
{
    private Node(TermHandle handle, RdfTerm? term)
    {
        Handle = handle;
        Term = term;
    }

    /// <summary>The data source's handle, or <see cref="TermHandle.None"/> for a term it does not hold.</summary>
    internal TermHandle Handle { get; }

    /// <summary>The term, when the node is not held, or when it is already known.</summary>
    internal RdfTerm? Term { get; }

    internal bool IsHeld => !Handle.IsNone;

    internal static Node Held(TermHandle handle) => new(handle, null);

    internal static Node Absent(RdfTerm term) => new(TermHandle.None, term);

    internal static Node HeldAs(TermHandle handle, RdfTerm term) => new(handle, term);
}

/// <summary>
/// Equality of nodes under one data source: by the source's comparer when
/// both are held, by term when neither is, and never between a held node and
/// an absent one, since an absent term is one the source does not hold.
/// </summary>
internal sealed class NodeComparer(IEqualityComparer<TermHandle> handles) : IEqualityComparer<Node>
{
    public bool Equals(Node x, Node y)
    {
        if (x.IsHeld != y.IsHeld)
        {
            return false;
        }

        return x.IsHeld ? handles.Equals(x.Handle, y.Handle) : RdfTerm.Comparer.Equals(x.Term!, y.Term!);
    }

    public int GetHashCode(Node obj) => obj.IsHeld ? handles.GetHashCode(obj.Handle) : RdfTerm.Comparer.GetHashCode(obj.Term!) ^ 0x5bd1e995;
}
