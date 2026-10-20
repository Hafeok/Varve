// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Management;

/// <summary>A total order of terms, so that declarations and findings read in a stable order.</summary>
internal sealed class TermOrder : IComparer<RdfTerm>
{
    internal static readonly TermOrder Instance = new();

    public int Compare(RdfTerm? x, RdfTerm? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        int c = x.Kind.CompareTo(y.Kind);

        if (c == 0)
        {
            c = x.Lexical.SequenceCompareTo(y.Lexical);
        }

        if (c == 0 && x.Kind == RdfTermKind.Literal)
        {
            c = x.DatatypeIri.SequenceCompareTo(y.DatatypeIri);
            c = c != 0 ? c : x.Language.SequenceCompareTo(y.Language);
        }

        if (c == 0 && x.Kind == RdfTermKind.TripleTerm)
        {
            c = Compare(x.Subject, y.Subject);
            c = c != 0 ? c : Compare(x.Predicate, y.Predicate);
            c = c != 0 ? c : Compare(x.Object, y.Object);
        }

        return c;
    }
}
