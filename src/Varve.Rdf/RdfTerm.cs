// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Rdf;

/// <summary>
/// An owned RDF term, with value equality.
/// </summary>
/// <remarks>
/// <para>
/// The allocating half of ADR 0024. Produced only when someone asks for one —
/// by <see cref="RdfTermView.Materialise"/> or
/// <see cref="IQuadSource.TryExternalise"/> — and never on a streaming parse
/// path, where allocation per quad is a defect.
/// </para>
/// <para>
/// Equality is **term equality**, not value equality:
/// <c>"1"^^xsd:integer</c> and <c>"01"^^xsd:integer</c> are different terms
/// here and will be equal values once <c>Varve.Xsd</c> exists at milestone 3b.
/// Language tags compare case-insensitively and everything else compares
/// exactly, which is the asymmetry RDF 1.1 Concepts §3.3 defines.
/// </para>
/// </remarks>
public sealed class RdfTerm : IEquatable<RdfTerm>
{
    private readonly byte[] _lexical;
    private readonly byte[] _language;
    private int _hash;

    private RdfTerm(
        RdfTermKind kind,
        byte[] lexical,
        RdfTerm? datatype,
        byte[] language,
        TextDirection direction,
        RdfTerm? subject,
        RdfTerm? predicate,
        RdfTerm? @object)
    {
        Kind = kind;
        _lexical = lexical;
        Datatype = datatype;
        _language = language;
        Direction = direction;
        Subject = subject;
        Predicate = predicate;
        Object = @object;
    }

    /// <summary>Which kind of term this is.</summary>
    public RdfTermKind Kind { get; }

    /// <summary>
    /// The IRI text, the blank node label without its <c>_:</c>, or the
    /// literal's lexical form. Empty for a triple term.
    /// </summary>
    public ReadOnlySpan<byte> Lexical => _lexical;

    /// <summary>
    /// The literal's datatype, or null. Null on a language-tagged literal,
    /// whose datatype is <c>rdf:langString</c>, and on a plain one, whose
    /// datatype is <c>xsd:string</c>: the absence is a shorthand and not a
    /// third state (Concepts §3.3).
    /// </summary>
    public RdfTerm? Datatype { get; }

    /// <summary>The language tag, or empty.</summary>
    public ReadOnlySpan<byte> Language => _language;

    /// <summary>The base direction of a language-tagged string.</summary>
    public TextDirection Direction { get; }

    /// <summary>
    /// The datatype IRI this literal actually has, whether it was written or
    /// implied: <c>xsd:string</c>, <c>rdf:langString</c>,
    /// <c>rdf:dirLangString</c>, or the explicit one. Empty for a term that is
    /// not a literal.
    /// </summary>
    public ReadOnlySpan<byte> DatatypeIri
    {
        get
        {
            if (Kind != RdfTermKind.Literal)
            {
                return default;
            }

            if (Datatype is not null)
            {
                return Datatype._lexical;
            }

            if (_language.Length == 0)
            {
                return RdfVocabulary.XsdString;
            }

            return Direction == TextDirection.None
                ? RdfVocabulary.RdfLangString
                : RdfVocabulary.RdfDirLangString;
        }
    }

    /// <summary>A triple term's subject, or null.</summary>
    public RdfTerm? Subject { get; }

    /// <summary>A triple term's predicate, or null.</summary>
    public RdfTerm? Predicate { get; }

    /// <summary>A triple term's object, or null.</summary>
    public RdfTerm? Object { get; }

    /// <summary>An IRI term.</summary>
    public static RdfTerm Iri(ReadOnlySpan<byte> utf8) =>
        new(RdfTermKind.Iri, utf8.ToArray(), null, [], TextDirection.None, null, null, null);

    /// <summary>A blank node, from its label without the <c>_:</c> prefix.</summary>
    public static RdfTerm BlankNode(ReadOnlySpan<byte> label) =>
        new(RdfTermKind.BlankNode, label.ToArray(), null, [], TextDirection.None, null, null, null);

    /// <summary>A literal with datatype <c>xsd:string</c>.</summary>
    public static RdfTerm Literal(ReadOnlySpan<byte> lexical) =>
        new(RdfTermKind.Literal, lexical.ToArray(), null, [], TextDirection.None, null, null, null);

    /// <summary>
    /// A literal with an explicit datatype. A datatype of <c>xsd:string</c> is
    /// folded away, so that the shorthand and the spelt-out form are the same
    /// term rather than two that merely denote the same thing.
    /// </summary>
    public static RdfTerm Literal(ReadOnlySpan<byte> lexical, RdfTerm datatype)
    {
        ArgumentNullException.ThrowIfNull(datatype);

        ReadOnlySpan<byte> iri = datatype.Kind == RdfTermKind.Iri ? datatype._lexical : default;

        if (iri.SequenceEqual(RdfVocabulary.RdfLangString) || iri.SequenceEqual(RdfVocabulary.RdfDirLangString))
        {
            throw new ArgumentException(
                "rdf:langString and rdf:dirLangString are the datatypes of a language-tagged literal and "
                + "cannot be given explicitly: a literal with either and no language tag is ill-formed "
                + "(RDF 1.1 Concepts §3.3). Use the language overload.",
                nameof(datatype));
        }

        RdfTerm? kept = iri.SequenceEqual(RdfVocabulary.XsdString) ? null : datatype;

        return new RdfTerm(RdfTermKind.Literal, lexical.ToArray(), kept, [], TextDirection.None, null, null, null);
    }

    /// <summary>
    /// A language-tagged literal, optionally with a base direction. The tag
    /// must be well-formed per BCP 47 §2.1, which is what RDF 1.2 N-Triples
    /// requires of one in a document — a model that can hold a tag no syntax
    /// can express is a model whose writer produces documents its own reader
    /// rejects.
    /// </summary>
    public static RdfTerm Literal(
        ReadOnlySpan<byte> lexical,
        ReadOnlySpan<byte> language,
        TextDirection direction = TextDirection.None)
    {
        if (!LanguageTag.IsWellFormed(language))
        {
            throw new ArgumentException(
                "Not a well-formed BCP 47 language tag. A literal with no language is built by the "
                + "single-argument overload.",
                nameof(language));
        }

        return new RdfTerm(
            RdfTermKind.Literal, lexical.ToArray(), null, language.ToArray(), direction, null, null, null);
    }

    /// <summary>A triple term. RDF 1.2.</summary>
    public static RdfTerm TripleTerm(RdfTerm subject, RdfTerm predicate, RdfTerm @object)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(@object);
        return new RdfTerm(RdfTermKind.TripleTerm, [], null, [], TextDirection.None, subject, predicate, @object);
    }

    /// <inheritdoc />
    public bool Equals(RdfTerm? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Kind != other.Kind || Direction != other.Direction)
        {
            return false;
        }

        if (Kind == RdfTermKind.TripleTerm)
        {
            return Subject!.Equals(other.Subject)
                && Predicate!.Equals(other.Predicate)
                && Object!.Equals(other.Object);
        }

        if (!_lexical.AsSpan().SequenceEqual(other._lexical))
        {
            return false;
        }

        // Concepts §3.3: language tags match ignoring case. Everything else is
        // compared exactly.
        if (!LanguageEquals(_language, other._language))
        {
            return false;
        }

        return Datatype is null ? other.Datatype is null : Datatype.Equals(other.Datatype);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RdfTerm);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (_hash != 0)
        {
            return _hash;
        }

        HashCode hash = default;
        hash.Add(Kind);
        hash.Add(Direction);

        if (Kind == RdfTermKind.TripleTerm)
        {
            hash.Add(Subject);
            hash.Add(Predicate);
            hash.Add(Object);
        }
        else
        {
            hash.AddBytes(_lexical);

            foreach (byte b in _language)
            {
                hash.Add(b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b);
            }

            hash.Add(Datatype);
        }

        int computed = hash.ToHashCode();
        _hash = computed == 0 ? 1 : computed;
        return _hash;
    }

    private static bool LanguageEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            byte a = left[i] is >= (byte)'A' and <= (byte)'Z' ? (byte)(left[i] + 32) : left[i];
            byte b = right[i] is >= (byte)'A' and <= (byte)'Z' ? (byte)(right[i] + 32) : right[i];

            if (a != b)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The comparer an interning table should use.</summary>
    public static IEqualityComparer<RdfTerm> Comparer { get; } = EqualityComparer<RdfTerm>.Default;
}
