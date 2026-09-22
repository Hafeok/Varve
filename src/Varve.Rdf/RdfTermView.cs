using System;

namespace Varve.Rdf;

/// <summary>
/// A term seen without owning it: spans over the input being parsed, or over
/// the arena's scratch, and no allocation at all.
/// </summary>
/// <remarks>
/// <para>
/// The non-allocating half of ADR 0024, and the reason a parse of a million
/// quads can allocate nothing. A view is valid for as long as the arena has
/// not been <see cref="TermArena.Reset"/> and the input it was taken over is
/// alive — in practice, for the duration of one callback. Call
/// <see cref="Materialise"/> to keep a term beyond that.
/// </para>
/// <para>
/// Nesting is by index, not by value, because a <c>ref struct</c> cannot
/// contain itself.
/// </para>
/// </remarks>
public readonly ref struct RdfTermView
{
    private readonly TermArena _arena;
    private readonly ReadOnlySpan<byte> _text;
    private readonly int _index;

    internal RdfTermView(TermArena arena, ReadOnlySpan<byte> text, int index)
    {
        _arena = arena;
        _text = text;
        _index = index;
    }

    /// <summary>Which kind of term this is.</summary>
    public RdfTermKind Kind => _arena.KindOf(_index);

    /// <summary>
    /// The IRI text, the blank node label without its <c>_:</c>, or the
    /// literal's lexical form, unescaped. Empty for a triple term.
    /// </summary>
    public ReadOnlySpan<byte> Lexical => _arena.Resolve(_text, _arena.LexicalOf(_index));

    /// <summary>Whether the literal carries an explicit datatype IRI.</summary>
    public bool HasDatatype => _arena.DatatypeOf(_index).IsPresent;

    /// <summary>The datatype IRI text, or empty.</summary>
    public ReadOnlySpan<byte> Datatype => _arena.Resolve(_text, _arena.DatatypeOf(_index));

    /// <summary>Whether the literal carries a language tag.</summary>
    public bool HasLanguage => _arena.LanguageOf(_index).IsPresent;

    /// <summary>The language tag, or empty.</summary>
    public ReadOnlySpan<byte> Language => _arena.Resolve(_text, _arena.LanguageOf(_index));

    /// <summary>The base direction of a language-tagged string.</summary>
    public TextDirection Direction => _arena.DirectionOf(_index);

    /// <summary>A triple term's subject.</summary>
    public RdfTermView Subject => new(_arena, _text, _arena.SubjectOf(_index));

    /// <summary>A triple term's predicate.</summary>
    public RdfTermView Predicate => new(_arena, _text, _arena.PredicateOf(_index));

    /// <summary>A triple term's object.</summary>
    public RdfTermView Object => new(_arena, _text, _arena.ObjectOf(_index));

    /// <summary>
    /// Copies this view into an owned term. The one place on the view path
    /// where allocation happens, and it happens because the caller asked.
    /// </summary>
    public RdfTerm Materialise()
    {
        switch (Kind)
        {
            case RdfTermKind.Iri:
                return RdfTerm.Iri(Lexical);

            case RdfTermKind.BlankNode:
                return RdfTerm.BlankNode(Lexical);

            case RdfTermKind.TripleTerm:
                return RdfTerm.TripleTerm(
                    Subject.Materialise(),
                    Predicate.Materialise(),
                    Object.Materialise());

            default:
                if (HasLanguage)
                {
                    return RdfTerm.Literal(Lexical, Language, Direction);
                }

                return HasDatatype
                    ? RdfTerm.Literal(Lexical, RdfTerm.Iri(Datatype))
                    : RdfTerm.Literal(Lexical);
        }
    }
}
