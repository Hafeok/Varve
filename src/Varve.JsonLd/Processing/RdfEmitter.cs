// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.JsonLd.Json;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.JsonLd.Processing;

/// <summary>
/// Deserializes an expanded document to RDF (JSON-LD 1.1 API §8.3–§8.6) by
/// walking the expanded tree and handing each quad to the caller as it is
/// found, which gives the same dataset as the specification's node map does,
/// as a set (ADR 0112). Blank nodes are relabelled <c>_:b0</c>, <c>_:b1</c>, …
/// (<c>BlankNodesAreRelabelled</c>).
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class RdfEmitter
{
    private static ReadOnlySpan<byte> RdfNs => "http://www.w3.org/1999/02/22-rdf-syntax-ns#"u8;
    private static ReadOnlySpan<byte> RdfType => "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8;
    private static ReadOnlySpan<byte> RdfFirst => "http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8;
    private static ReadOnlySpan<byte> RdfRest => "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8;
    private static ReadOnlySpan<byte> RdfNil => "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8;
    private static ReadOnlySpan<byte> RdfJson => "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON"u8;
    private static ReadOnlySpan<byte> RdfValue => "http://www.w3.org/1999/02/22-rdf-syntax-ns#value"u8;
    private static ReadOnlySpan<byte> RdfLanguage => "http://www.w3.org/1999/02/22-rdf-syntax-ns#language"u8;
    private static ReadOnlySpan<byte> RdfDirectionIri => "http://www.w3.org/1999/02/22-rdf-syntax-ns#direction"u8;
    private static ReadOnlySpan<byte> XsdStringIri => "http://www.w3.org/2001/XMLSchema#string"u8;
    private static ReadOnlySpan<byte> XsdBooleanIri => "http://www.w3.org/2001/XMLSchema#boolean"u8;
    private static ReadOnlySpan<byte> XsdIntegerIri => "http://www.w3.org/2001/XMLSchema#integer"u8;
    private static ReadOnlySpan<byte> XsdDoubleIri => "http://www.w3.org/2001/XMLSchema#double"u8;
    private static ReadOnlySpan<byte> I18n => "https://www.w3.org/ns/i18n#"u8;

    private readonly JsonTree _tree;
    private readonly ContextProcessor _contexts;
    private readonly TermArena _arena = new();
    private readonly NameTable _blankLabels = new();
    private int[] _blankNumbers = ArrayPool<int>.Shared.Rent(256);
    private int _nextBlank;
    private JsonLdQuadHandler _handler = static (in QuadView quad) => { };
    private RdfDirection _direction;
    private TextRange _rdfType, _rdfFirst, _rdfRest, _rdfNil;

    internal RdfEmitter(JsonTree tree, ContextProcessor contexts)
    {
        _tree = tree;
        _contexts = contexts;
    }

    internal long QuadCount { get; private set; }

    /// <summary>A term about to be emitted; <see cref="Kind"/> None is "not well-formed, skip".</summary>
    private readonly record struct Term(TermKind Kind, TextRange Text, TextRange Datatype, TextRange Language, TextDirection Direction, int Blank)
    {
        internal static Term None => new(TermKind.None, TextRange.None, TextRange.None, TextRange.None, TextDirection.None, 0);

        internal bool IsNone => Kind == TermKind.None;

        internal static Term Iri(TextRange text) => new(TermKind.Iri, text, TextRange.None, TextRange.None, TextDirection.None, 0);

        internal static Term BlankNode(int number) => new(TermKind.Blank, TextRange.None, TextRange.None, TextRange.None, TextDirection.None, number);
    }

    private enum TermKind : byte
    {
        None,
        Iri,
        Blank,
        Literal,
    }

    /// <summary>Emits the quads of an expanded document (an array of node objects).</summary>
    internal void Emit(int expanded, JsonLdQuadHandler handler, RdfDirection direction)
    {
        _handler = handler;
        _direction = direction;
        _blankLabels.Reset();
        _nextBlank = 0;
        QuadCount = 0;
        _rdfType = _tree.AddText(RdfType);
        _rdfFirst = _tree.AddText(RdfFirst);
        _rdfRest = _tree.AddText(RdfRest);
        _rdfNil = _tree.AddText(RdfNil);
        EmitNodes(expanded, Term.None);
    }

    private void EmitNodes(int array, Term graph)
    {
        for (int node = _tree.IsArray(array) ? _tree.First(array) : array; node >= 0; node = _tree.IsArray(array) ? _tree.Next(node) : -1)
        {
            if (_tree.IsObject(node))
            {
                EmitNode(node, graph);
            }
        }
    }

    /// <summary>Emits a node object's triples and returns its subject term, None when its @id is not well-formed.</summary>
    private Term EmitNode(int node, Term graph)
    {
        int id = _tree.Member(node, Keyword.Id);
        Term subject = id < 0 ? Term.BlankNode(_nextBlank++) : NodeTerm(id);

        for (int member = _tree.First(node); member >= 0; member = _tree.Next(member))
        {
            int property = _tree.NameOf(member);
            int value = _tree.ValueOf(member);

            switch (property)
            {
                case Keyword.Type:
                    for (int type = _tree.IsArray(value) ? _tree.First(value) : value; type >= 0; type = _tree.IsArray(value) ? _tree.Next(type) : -1)
                    {
                        Term typeTerm = _tree.IsString(type) ? NodeTerm(type) : Term.None;
                        Emit(subject, Term.Iri(_rdfType), typeTerm, graph);
                    }

                    break;

                case Keyword.Graph:
                    if (!subject.IsNone)
                    {
                        EmitNodes(value, subject);
                    }

                    break;

                case Keyword.Included:
                    EmitNodes(value, graph);
                    break;

                case Keyword.Reverse:
                    for (int reverse = _tree.First(value); reverse >= 0; reverse = _tree.Next(reverse))
                    {
                        Term predicate = PredicateTerm(_tree.NameOf(reverse));
                        int items = _tree.ValueOf(reverse);

                        for (int item = _tree.IsArray(items) ? _tree.First(items) : items; item >= 0; item = _tree.IsArray(items) ? _tree.Next(item) : -1)
                        {
                            Term other = ObjectTerm(item, graph);
                            Emit(other, predicate, subject, graph);
                        }
                    }

                    break;

                case Keyword.Id:
                case Keyword.Index:
                case Keyword.Context:
                    break;

                default:
                    if (NameTable.IsKeyword(property))
                    {
                        break;
                    }

                    Term predicateTerm = PredicateTerm(property);

                    for (int item = _tree.IsArray(value) ? _tree.First(value) : value; item >= 0; item = _tree.IsArray(value) ? _tree.Next(item) : -1)
                    {
                        Term obj = ObjectTerm(item, graph);
                        Emit(subject, predicateTerm, obj, graph);
                    }

                    break;
            }
        }

        return subject;
    }

    /// <summary>The term of a node object, list object or value object in object position; None when it cannot be a term.</summary>
    private Term ObjectTerm(int item, Term graph)
    {
        if (!_tree.IsObject(item))
        {
            return Term.None;
        }

        int list = _tree.Member(item, Keyword.List);

        if (list >= 0)
        {
            return ListTerm(list, graph);
        }

        if (_tree.HasMember(item, Keyword.Value))
        {
            return LiteralTerm(item, graph);
        }

        return EmitNode(item, graph);
    }

    /// <summary>§8.5 list to RDF: a chain of fresh blank nodes, <c>rdf:nil</c> for the empty list.</summary>
    private Term ListTerm(int items, Term graph)
    {
        if (_tree.Count(items) == 0)
        {
            return Term.Iri(_rdfNil);
        }

        Term head = Term.BlankNode(_nextBlank++);
        Term current = head;
        Term first = Term.Iri(_rdfFirst);
        Term rest = Term.Iri(_rdfRest);

        for (int item = _tree.First(items); item >= 0; item = _tree.Next(item))
        {
            Term obj = ObjectTerm(item, graph);
            Emit(current, first, obj, graph);
            Term next = _tree.Next(item) >= 0 ? Term.BlankNode(_nextBlank++) : Term.Iri(_rdfNil);
            Emit(current, rest, next, graph);
            current = next;
        }

        return head;
    }

    /// <summary>§8.6 object to RDF, the literal half.</summary>
    [DesignDecision(typeof(JsonLdOverUtf8Json.JsonLiteralsAreCanonical), Scope = ExceptionScope.HotPath)]
    private Term LiteralTerm(int valueObject, Term graph)
    {
        int value = _tree.Member(valueObject, Keyword.Value);
        int type = _tree.Member(valueObject, Keyword.Type);
        int language = _tree.Member(valueObject, Keyword.Language);
        int direction = _tree.Member(valueObject, Keyword.Direction);
        TextRange datatype = type >= 0 && _tree.IsString(type) ? _tree.Range(type) : TextRange.None;
        TextRange lexical;

        if (type >= 0 && _tree.StringEquals(type, "@json"u8))
        {
            lexical = Jcs.Canonicalise(_tree, value);
            datatype = _tree.AddText(RdfJson);
        }
        else if (_tree.IsBoolean(value))
        {
            lexical = _tree.AddText(_tree.Kind(value) == JsonKind.True ? "true"u8 : "false"u8);

            if (datatype.IsNone)
            {
                datatype = _tree.AddText(XsdBooleanIri);
            }
        }
        else if (_tree.IsNumber(value))
        {
            // §8.6 steps 7 and 8: a number with a non-zero fractional part,
            // or at or beyond 1e21, or typed xsd:double, is a double;
            // otherwise it is an integer, however it was spelt (-0.0 is "0").
            ReadOnlySpan<byte> raw = _tree.Bytes(value);
            bool parsed = XsdDouble.TryParse(raw, out XsdDouble number);
            bool isDouble = _tree.TextEquals(datatype, XsdDoubleIri)
                || (parsed && (Math.Abs(number.Value) >= 1e21 || number.Value != Math.Floor(number.Value)));

            if (isDouble)
            {
                lexical = CanonicalDouble(raw);

                if (datatype.IsNone)
                {
                    datatype = _tree.AddText(XsdDoubleIri);
                }
            }
            else
            {
                lexical = CanonicalInteger(raw);

                if (datatype.IsNone)
                {
                    datatype = _tree.AddText(XsdIntegerIri);
                }
            }
        }
        else if (_tree.IsString(value))
        {
            lexical = _tree.Range(value);
        }
        else
        {
            return Term.None;
        }

        TextRange languageTag = language >= 0 && _tree.IsString(language) ? _tree.Range(language) : TextRange.None;

        // §8.6: a statement with an ill-formed language tag is not
        // well-formed, and only well-formed statements are emitted (toRdf-wf05).
        if (!languageTag.IsNone && !LanguageTag.IsWellFormed(_tree.Bytes(languageTag)))
        {
            return Term.None;
        }
        TextDirection textDirection = direction < 0 ? TextDirection.None
            : _tree.StringEquals(direction, "ltr"u8) ? TextDirection.LeftToRight
            : _tree.StringEquals(direction, "rtl"u8) ? TextDirection.RightToLeft : TextDirection.None;

        if (textDirection != TextDirection.None)
        {
            switch (_direction)
            {
                case RdfDirection.I18nDatatype:
                {
                    // https://www.w3.org/ns/i18n#<lang>_<dir>, the language lowercased.
                    ReadOnlySpan<byte> dir = textDirection == TextDirection.LeftToRight ? "ltr"u8 : "rtl"u8;
                    Span<byte> buffer = _tree.ReserveText(I18n.Length + (languageTag.IsNone ? 0 : languageTag.Length) + 1 + dir.Length);
                    int at = 0;
                    I18n.CopyTo(buffer);
                    at += I18n.Length;

                    if (!languageTag.IsNone)
                    {
                        ReadOnlySpan<byte> lang = _tree.Bytes(languageTag);

                        for (int i = 0; i < lang.Length; i++)
                        {
                            byte b = lang[i];
                            buffer[at++] = b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b | 0x20) : b;
                        }
                    }

                    buffer[at++] = (byte)'_';
                    dir.CopyTo(buffer[at..]);
                    at += dir.Length;
                    TextRange i18n = _tree.CommitText(at);
                    return new Term(TermKind.Literal, lexical, i18n, TextRange.None, TextDirection.None, 0);
                }

                case RdfDirection.CompoundLiteral:
                {
                    Term compound = Term.BlankNode(_nextBlank++);
                    Emit(compound, Term.Iri(_tree.AddText(RdfValue)), new Term(TermKind.Literal, lexical, TextRange.None, TextRange.None, TextDirection.None, 0), graph);

                    if (!languageTag.IsNone)
                    {
                        Emit(compound, Term.Iri(_tree.AddText(RdfLanguage)), new Term(TermKind.Literal, _contexts.Lowercase(_tree.Bytes(languageTag)), TextRange.None, TextRange.None, TextDirection.None, 0), graph);
                    }

                    Emit(compound, Term.Iri(_tree.AddText(RdfDirectionIri)), new Term(TermKind.Literal, _tree.AddText(textDirection == TextDirection.LeftToRight ? "ltr"u8 : "rtl"u8), TextRange.None, TextRange.None, TextDirection.None, 0), graph);
                    return compound;
                }

                case RdfDirection.Native:
                    // RDF 1.2: a direction wants a language; without one the
                    // literal is a plain string (json-ld.md §5).
                    if (languageTag.IsNone)
                    {
                        textDirection = TextDirection.None;
                    }

                    break;

                default:
                    textDirection = TextDirection.None;
                    break;
            }
        }

        if (!languageTag.IsNone)
        {
            return new Term(TermKind.Literal, lexical, TextRange.None, languageTag, textDirection, 0);
        }

        if (datatype.IsNone)
        {
            datatype = _tree.AddText(XsdStringIri);
        }

        return new Term(TermKind.Literal, lexical, datatype, TextRange.None, TextDirection.None, 0);
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.NumbersAreXsdCanonical), Scope = ExceptionScope.HotPath)]
    private TextRange CanonicalDouble(ReadOnlySpan<byte> raw)
    {
        if (!XsdDouble.TryParse(raw, out XsdDouble xsd))
        {
            return _tree.AddText(raw);
        }

        Span<byte> buffer = _tree.ReserveText(64);

        if (!xsd.TryFormat(buffer, out int written))
        {
            return _tree.AddText(raw);
        }

        return _tree.CommitText(written);
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.NumbersAreXsdCanonical), Scope = ExceptionScope.HotPath)]
    private TextRange CanonicalInteger(ReadOnlySpan<byte> raw)
    {
        if (raw.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0 && XsdDouble.TryParse(raw, out XsdDouble spelt) && Math.Abs(spelt.Value) < 9e18)
        {
            // An integer spelt with a fraction or an exponent, 1.0 or 1e2.
            Span<byte> digits = _tree.ReserveText(32);
            ((long)spelt.Value).TryFormat(digits, out int count, default, provider: null);
            return _tree.CommitText(count);
        }

        if (XsdInteger.TryParse(raw, out XsdInteger value))
        {
            Span<byte> buffer = _tree.ReserveText(32);

            if (value.TryFormat(buffer, out int written))
            {
                return _tree.CommitText(written);
            }
        }

        // Beyond 64 bits: JSON already forbids leading zeros, so the digits
        // are canonical but for a negative zero.
        return raw.SequenceEqual("-0"u8) ? _tree.AddText("0"u8) : _tree.AddText(raw);
    }

    /// <summary>An @id or @type string as a term: a relabelled blank node, an IRI, or None for a relative IRI or a null.</summary>
    private Term NodeTerm(int stringNode)
    {
        if (!_tree.IsString(stringNode))
        {
            return Term.None;
        }

        ReadOnlySpan<byte> text = _tree.Bytes(stringNode);

        if (ContextProcessor.IsBlankNode(text))
        {
            return Term.BlankNode(BlankNumber(text));
        }

        return IriRef.IsAbsolute(text) ? Term.Iri(_tree.Range(stringNode)) : Term.None;
    }

    /// <summary>A property as a predicate: an IRI, or None for a blank node (generalized RDF is not produced) or a relative IRI.</summary>
    private Term PredicateTerm(int nameId)
    {
        ReadOnlySpan<byte> text = _tree.Names.Bytes(nameId);

        if (ContextProcessor.IsBlankNode(text) || !IriRef.IsAbsolute(text))
        {
            return Term.None;
        }

        return Term.Iri(_tree.AddText(text));
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.BlankNodesAreRelabelled), Scope = ExceptionScope.HotPath)]
    private int BlankNumber(ReadOnlySpan<byte> label)
    {
        int before = _blankLabels.Count;
        int id = _blankLabels.Intern(label);
        int slot = id - Keyword.Count;

        if (slot >= _blankNumbers.Length)
        {
            int[] bigger = ArrayPool<int>.Shared.Rent(Math.Max(_blankNumbers.Length * 2, slot + 1));
            _blankNumbers.AsSpan().CopyTo(bigger);
            ArrayPool<int>.Shared.Return(_blankNumbers);
            _blankNumbers = bigger;
        }

        if (_blankLabels.Count > before)
        {
            _blankNumbers[slot] = _nextBlank++;
        }

        return _blankNumbers[slot];
    }

    /// <summary>Hands one quad to the caller when all three terms are well-formed.</summary>
    [DesignDecision(typeof(JsonLdOverUtf8Json.TheQuadHandlerIsTheCallers), Scope = ExceptionScope.HotPath)]
    private void Emit(Term subject, Term predicate, Term obj, Term graph)
    {
        if (subject.IsNone || predicate.IsNone || obj.IsNone || subject.Kind == TermKind.Literal)
        {
            return;
        }

        _arena.Reset();
        int s = Add(subject);
        int p = Add(predicate);
        int o = Add(obj);
        int g = graph.IsNone ? -1 : Add(graph);
        QuadView quad = _arena.Quad(default, s, p, o, g);
        _handler(in quad);
        QuadCount++;
    }

    private int Add(Term term)
    {
        switch (term.Kind)
        {
            case TermKind.Iri:
                return _arena.AddIri(_arena.AppendScratch(_tree.Bytes(term.Text)));
            case TermKind.Blank:
            {
                Span<byte> label = _arena.ReserveScratch(12);
                label[0] = (byte)'b';
                term.Blank.TryFormat(label[1..], out int written, default, provider: null);
                return _arena.AddBlankNode(_arena.CommitScratch(1 + written));
            }

            default:
            {
                TermSpan lexical = _arena.AppendScratch(_tree.Bytes(term.Text));
                TermSpan datatype = term.Datatype.IsNone ? TermSpan.None : _arena.AppendScratch(_tree.Bytes(term.Datatype));
                TermSpan language = term.Language.IsNone ? TermSpan.None : _arena.AppendScratch(_tree.Bytes(term.Language));
                return _arena.AddLiteral(lexical, datatype, language, term.Direction);
            }
        }
    }
}
