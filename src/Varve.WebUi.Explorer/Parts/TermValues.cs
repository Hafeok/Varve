// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.WebUi.Components.Atoms;
using Varve.WebUi.Components.Parts;
using Varve.WebUi.Explorer.Atoms;

namespace Varve.WebUi.Explorer.Parts;

/// <summary>
/// An RDF term as a <c>TermChip</c> shows it, as a value, for the explorer's
/// components that draw terms from data (a results table): the same compact
/// IRIs, captions and blank node labels as the <c>Term</c> part, by
/// <see cref="TermText"/>.
/// </summary>
public static class TermValues
{
    /// <summary>
    /// <paramref name="term"/> as a chip's parameters. A triple term is its
    /// three terms written out, as a chip of its own kind: TermChip shows
    /// that text in its frame when it is given no child chips.
    /// </summary>
    public static TermValue Of(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                string iri = TermText.Text(term.Lexical);
                string compact = TermText.Compact(iri);
                return new TermValue(TermKind.Iri, compact, Full: compact == iri ? null : iri);
            case RdfTermKind.Literal:
                return new TermValue(TermKind.Literal, TermText.Text(term.Lexical), TermText.LiteralType(term));
            case RdfTermKind.BlankNode:
                return new TermValue(TermKind.BlankNode, "_:" + TermText.Text(term.Lexical));
            default:
                return new TermValue(TermKind.Triple, "<< " + Of(term.Subject!).Text + " " + Of(term.Predicate!).Text + " " + Of(term.Object!).Text + " >>");
        }
    }
}
