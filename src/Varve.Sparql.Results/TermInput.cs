// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;

namespace Varve.Sparql.Results;

/// <summary>
/// A term to write, whether the caller holds it as an owned <see cref="RdfTerm"/>
/// or as a reader's <see cref="RdfTermView"/>: one shape for the format
/// writers, so that each writes a term once and neither path allocates.
/// </summary>
internal readonly ref struct TermInput
{
    private readonly RdfTerm? _term;
    private readonly RdfTermView _view;

    internal TermInput(RdfTerm term) => _term = term;

    internal TermInput(RdfTermView view) => _view = view;

    internal RdfTermKind Kind => _term?.Kind ?? _view.Kind;

    /// <summary>The IRI, the label without <c>_:</c>, or the lexical form.</summary>
    internal ReadOnlySpan<byte> Lexical => _term is not null ? _term.Lexical : _view.Lexical;

    internal ReadOnlySpan<byte> Language => _term is not null ? _term.Language : (_view.HasLanguage ? _view.Language : default);

    internal TextDirection Direction => _term?.Direction ?? _view.Direction;

    /// <summary>
    /// The datatype IRI a format writes: empty for a language-tagged literal
    /// and for <c>xsd:string</c>, which every format leaves implicit.
    /// </summary>
    internal ReadOnlySpan<byte> WrittenDatatype
    {
        get
        {
            if (Kind != RdfTermKind.Literal || !Language.IsEmpty)
            {
                return default;
            }

            ReadOnlySpan<byte> datatype = _term is not null
                ? (_term.Datatype is null ? default : _term.Datatype.Lexical)
                : (_view.HasDatatype ? _view.Datatype : default);

            return datatype.SequenceEqual(RdfVocabulary.XsdString) ? default : datatype;
        }
    }

    internal TermInput Subject => _term is not null ? new TermInput(_term.Subject!) : new TermInput(_view.Subject);

    internal TermInput Predicate => _term is not null ? new TermInput(_term.Predicate!) : new TermInput(_view.Predicate);

    internal TermInput Object => _term is not null ? new TermInput(_term.Object!) : new TermInput(_view.Object);
}
