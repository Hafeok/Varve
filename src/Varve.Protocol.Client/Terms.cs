// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Protocol.Client;

/// <summary>A term a reader lends for one callback, copied so it outlives it.</summary>
internal static class Terms
{
    internal static RdfTerm Own(in RdfTermView view) => view.Kind switch
    {
        RdfTermKind.Iri => RdfTerm.Iri(view.Lexical),
        RdfTermKind.BlankNode => RdfTerm.BlankNode(view.Lexical),
        RdfTermKind.TripleTerm => RdfTerm.TripleTerm(Own(view.Subject), Own(view.Predicate), Own(view.Object)),
        _ when view.HasLanguage => RdfTerm.Literal(view.Lexical, view.Language, view.Direction),
        _ when view.HasDatatype => RdfTerm.Literal(view.Lexical, RdfTerm.Iri(view.Datatype)),
        _ => RdfTerm.Literal(view.Lexical),
    };
}
