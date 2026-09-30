// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Results.Model;

namespace Varve.Sparql.Results;

/// <summary>
/// SPARQL Query Results XML Format, read by a non-validating reader of the XML
/// subset the format uses (<c>sparql-results.md</c> §3.1): elements,
/// attributes, character data with the predefined entities and character
/// references, CDATA, comments and processing instructions, and namespaces.
/// A document type declaration is refused.
/// </summary>
internal sealed class XmlResultsParser : FormatParser
{
    private static ReadOnlySpan<byte> ResultsNamespace => "http://www.w3.org/2005/sparql-results#"u8;

    private static ReadOnlySpan<byte> XmlNamespace => "http://www.w3.org/XML/1998/namespace"u8;

    private static ReadOnlySpan<byte> ItsNamespace => "http://www.w3.org/2005/11/its"u8;

    private readonly ByteCursor _cursor;
    private readonly Tag _tag = new();
    private readonly Namespaces _namespaces = new();
    private bool _inResults;
    private bool _done;

    internal XmlResultsParser(ReadOnlySequence<byte> input) => _cursor = new ByteCursor(input);

    internal override void ReadHead()
    {
        // An optional byte order mark, then the prologue.
        _cursor.TryConsume([0xEF, 0xBB, 0xBF]);
        ReadStart("sparql"u8);
        if (_tag.IsEmpty)
        {
            throw Structure("The sparql element is empty.");
        }

        ReadStart("head"u8);
        if (!_tag.IsEmpty)
        {
            while (ReadTag())
            {
                if (_tag.Is(ResultsNamespace, "variable"u8))
                {
                    int name = _tag.Attribute(default, "name"u8);
                    if (name < 0)
                    {
                        throw Structure("A variable element has no name attribute.");
                    }

                    AddVariable(_tag.AttributeValue(name), _tag.Position);
                    SkipElement();
                }
                else if (_tag.Is(ResultsNamespace, "link"u8))
                {
                    SkipElement();
                }
                else
                {
                    throw Structure("The head holds something other than variable and link elements.");
                }
            }

            ExpectEnd("head"u8);
        }
        else
        {
            PopEmpty();
        }

        if (!ReadTag())
        {
            // Neither results nor a boolean: a document with a head only.
            ExpectEnd("sparql"u8);
            EndOfDocument();
            _done = true;
            return;
        }

        if (_tag.Is(ResultsNamespace, "boolean"u8))
        {
            IsBoolean = true;
            ByteBuilder text = ReadSimpleText();
            ReadOnlySpan<byte> value = Trim(text.Span);
            Boolean = value.SequenceEqual("true"u8) || (!value.SequenceEqual("false"u8)
                ? throw Structure("A boolean is neither true nor false.")
                : false);
            ExpectEnd("boolean"u8);
            CloseDocument();
            _done = true;
        }
        else if (_tag.Is(ResultsNamespace, "results"u8))
        {
            if (_tag.IsEmpty)
            {
                PopEmpty();
                CloseDocument();
                _done = true;
            }
            else
            {
                _inResults = true;
            }
        }
        else
        {
            throw Structure("After the head comes a results element or a boolean.");
        }
    }

    internal override bool ReadSolution(TermArena arena, int[] bindings)
    {
        if (_done || !_inResults)
        {
            return false;
        }

        if (!ReadTag())
        {
            ExpectEnd("results"u8);
            CloseDocument();
            _done = true;
            return false;
        }

        if (!_tag.Is(ResultsNamespace, "result"u8))
        {
            throw Structure("The results element holds something other than result elements.");
        }

        if (_tag.IsEmpty)
        {
            PopEmpty();
            return true;
        }

        while (ReadTag())
        {
            if (!_tag.Is(ResultsNamespace, "binding"u8))
            {
                throw Structure("A result holds something other than binding elements.");
            }

            int name = _tag.Attribute(default, "name"u8);
            if (name < 0)
            {
                throw Structure("A binding element has no name attribute.");
            }

            int index = Bind(_tag.AttributeValue(name), bindings, _tag.Position);
            if (_tag.IsEmpty)
            {
                throw Structure("A binding element holds no term.");
            }

            if (!ReadTag())
            {
                throw Structure("A binding element holds no term.");
            }

            bindings[index] = ReadTerm(arena);
            if (ReadTag())
            {
                throw Structure("A binding element holds more than one term.");
            }

            ExpectEnd("binding"u8);
        }

        ExpectEnd("result"u8);
        return true;
    }

    /// <summary>Reads the term whose start tag was just read.</summary>
    private int ReadTerm(TermArena arena)
    {
        ResultsPosition position = _tag.Position;
        if (_tag.Is(ResultsNamespace, "uri"u8))
        {
            TermSpan text = ReadText(arena, "uri"u8);
            RequireUtf8(arena, text, position);
            return arena.AddIri(text);
        }

        if (_tag.Is(ResultsNamespace, "bnode"u8))
        {
            TermSpan text = ReadText(arena, "bnode"u8);
            RequireUtf8(arena, text, position);
            return arena.AddBlankNode(text);
        }

        if (_tag.Is(ResultsNamespace, "literal"u8))
        {
            TermSpan language = TermSpan.None;
            TermSpan datatype = TermSpan.None;
            int lang = _tag.Attribute(XmlNamespace, "lang"u8);
            if (lang >= 0)
            {
                language = arena.AppendScratch(_tag.AttributeValue(lang));
            }

            int type = _tag.Attribute(default, "datatype"u8);
            if (type >= 0)
            {
                datatype = arena.AppendScratch(_tag.AttributeValue(type));
            }

            int dirIndex = _tag.Attribute(ItsNamespace, "dir"u8);
            Span<byte> direction = stackalloc byte[3];
            int directionLength = 0;
            if (dirIndex >= 0)
            {
                ReadOnlySpan<byte> value = _tag.AttributeValue(dirIndex);
                if (value.Length > 3)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A base direction is neither ltr nor rtl.");
                }

                value.CopyTo(direction);
                directionLength = value.Length;
            }

            TermSpan lexical = ReadText(arena, "literal"u8);
            return AddLiteral(arena, lexical, datatype, language, direction[..directionLength], position);
        }

        if (_tag.Is(ResultsNamespace, "triple"u8))
        {
            if (_tag.IsEmpty)
            {
                throw Structure("A triple element is empty.");
            }

            int subject = -1, predicate = -1, @object = -1;
            while (ReadTag())
            {
                ReadOnlySpan<byte> part =
                    _tag.Is(ResultsNamespace, "subject"u8) ? "subject"u8
                    : _tag.Is(ResultsNamespace, "predicate"u8) ? "predicate"u8
                    : _tag.Is(ResultsNamespace, "object"u8) ? "object"u8
                    : throw Structure("A triple holds something other than subject, predicate and object.");
                if (_tag.IsEmpty || !ReadTag())
                {
                    throw Structure("A triple's component holds no term.");
                }

                int term = ReadTerm(arena);
                if (ReadTag())
                {
                    throw Structure("A triple's component holds more than one term.");
                }

                ExpectEnd(part);
                if (part[0] == (byte)'s')
                {
                    subject = term;
                }
                else if (part[0] == (byte)'p')
                {
                    predicate = term;
                }
                else
                {
                    @object = term;
                }
            }

            ExpectEnd("triple"u8);
            if (subject < 0 || predicate < 0 || @object < 0)
            {
                throw Structure("A triple lacks a subject, a predicate or an object.");
            }

            return arena.AddTripleTerm(subject, predicate, @object);
        }

        throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A binding holds an element that is not uri, bnode, literal or triple.");
    }

    // ------------------------------------------------------------------ tags

    private void ReadStart(ReadOnlySpan<byte> localName)
    {
        if (!ReadTag() || !_tag.Is(ResultsNamespace, localName))
        {
            throw Structure("Expected the " + System.Text.Encoding.UTF8.GetString(localName) + " element.");
        }
    }

    /// <summary>
    /// Skips white space, comments and processing instructions, and reads the
    /// next tag. True for a start tag (in <see cref="_tag"/>), false for an end
    /// tag, which is left unread for <see cref="ExpectEnd"/>.
    /// </summary>
    private bool ReadTag()
    {
        while (true)
        {
            SkipWhitespace();
            if (_cursor.Peek() != '<')
            {
                if (_cursor.AtEnd)
                {
                    throw End();
                }

                throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedStructure, _cursor.Position, "Text where an element was expected.");
            }

            if (SkipMarkup())
            {
                continue;
            }

            if (_cursor.PeekAt(1) == '/')
            {
                return false;
            }

            ReadStartTag();
            return true;
        }
    }

    /// <summary>Skips a comment, a processing instruction or an XML declaration; refuses a DOCTYPE.</summary>
    private bool SkipMarkup()
    {
        if (_cursor.TryConsume("<!--"u8))
        {
            ResultsPosition start = _cursor.Position;
            while (!_cursor.TryConsume("-->"u8))
            {
                if (_cursor.Next() < 0)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, start, "An unterminated comment.");
                }
            }

            return true;
        }

        if (_cursor.PeekAt(1) == '?')
        {
            ResultsPosition start = _cursor.Position;
            _cursor.Next();
            _cursor.Next();
            while (!_cursor.TryConsume("?>"u8))
            {
                if (_cursor.Next() < 0)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, start, "An unterminated processing instruction.");
                }
            }

            return true;
        }

        if (_cursor.PeekAt(1) == '!' && _cursor.PeekAt(2) == 'D')
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.DocumentTypeDeclaration, _cursor.Position, "A document type declaration is refused.");
        }

        return false;
    }

    private void ReadStartTag()
    {
        ResultsPosition position = _cursor.Position;
        _cursor.Next(); // '<'
        _tag.Clear(position);
        ReadName(_tag.Buffer, out int nameStart, out int nameLength);
        _tag.SetName(nameStart, nameLength);

        while (true)
        {
            bool space = SkipWhitespace();
            int c = _cursor.Peek();
            if (c == '>')
            {
                _cursor.Next();
                break;
            }

            if (c == '/')
            {
                _cursor.Next();
                if (_cursor.Next() != '>')
                {
                    throw Malformed("Expected '>' after '/'.");
                }

                _tag.IsEmpty = true;
                break;
            }

            if (c < 0)
            {
                throw End();
            }

            if (!space)
            {
                throw Malformed("Attributes must be separated by white space.");
            }

            ReadName(_tag.Buffer, out int attrStart, out int attrLength);
            SkipWhitespace();
            if (_cursor.Next() != '=')
            {
                throw Malformed("Expected '=' after an attribute name.");
            }

            SkipWhitespace();
            int quote = _cursor.Next();
            if (quote != '"' && quote != '\'')
            {
                throw Malformed("An attribute value must be quoted.");
            }

            int valueStart = _tag.Buffer.Length;
            while (true)
            {
                int b = _cursor.Peek();
                if (b < 0)
                {
                    throw End();
                }

                if (b == quote)
                {
                    _cursor.Next();
                    break;
                }

                if (b == '<')
                {
                    throw Malformed("'<' in an attribute value.");
                }

                if (b == '&')
                {
                    ReadReference(_tag.Buffer);
                }
                else
                {
                    _tag.Buffer.Append((byte)_cursor.Next());
                }
            }

            _tag.AddAttribute(attrStart, attrLength, valueStart, _tag.Buffer.Length - valueStart);
        }

        _namespaces.Push(_tag);
        _tag.Resolve(_namespaces, position);
    }

    private void ExpectEnd(ReadOnlySpan<byte> localName)
    {
        SkipWhitespaceAndMarkup();
        ResultsPosition position = _cursor.Position;
        if (!_cursor.TryConsume("</"u8))
        {
            throw _cursor.AtEnd ? End() : Structure("Expected the end of " + System.Text.Encoding.UTF8.GetString(localName) + ".");
        }

        ByteBuilder name = _tag.Scratch;
        name.Clear();
        ReadName(name, out _, out _);
        SkipWhitespace();
        if (_cursor.Next() != '>')
        {
            throw Malformed("Expected '>' to close an end tag.");
        }

        if (!_namespaces.PopMatches(name.Span, localName, ResultsNamespace))
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "An end tag does not match its start tag.");
        }
    }

    private void PopEmpty() => _namespaces.PopEmpty();

    /// <summary>Skips everything inside the element whose start tag was just read.</summary>
    private void SkipElement()
    {
        if (_tag.IsEmpty)
        {
            PopEmpty();
            return;
        }

        int depth = 1;
        while (depth > 0)
        {
            int c = _cursor.Peek();
            if (c < 0)
            {
                throw End();
            }

            if (c != '<')
            {
                if (c == '&')
                {
                    ReadReference(_tag.Scratch);
                }
                else
                {
                    _cursor.Next();
                }

                continue;
            }

            if (SkipMarkup() || SkipCData(null))
            {
                continue;
            }

            if (_cursor.PeekAt(1) == '/')
            {
                ByteBuilder name = _tag.Scratch;
                name.Clear();
                _cursor.Next();
                _cursor.Next();
                ReadName(name, out _, out _);
                SkipWhitespace();
                if (_cursor.Next() != '>')
                {
                    throw Malformed("Expected '>' to close an end tag.");
                }

                if (!_namespaces.PopMatchesAny(name.Span))
                {
                    throw Malformed("An end tag does not match its start tag.");
                }

                depth--;
            }
            else
            {
                ReadStartTag();
                if (_tag.IsEmpty)
                {
                    PopEmpty();
                }
                else
                {
                    depth++;
                }
            }
        }
    }

    // ------------------------------------------------------------------ text

    /// <summary>Reads the character data of an element into the arena and consumes its end tag.</summary>
    private TermSpan ReadText(TermArena arena, ReadOnlySpan<byte> localName)
    {
        if (_tag.IsEmpty)
        {
            PopEmpty();
            return arena.AppendScratch(default);
        }

        ByteBuilder text = _tag.Scratch;
        text.Clear();
        ReadCharacterData(text);
        TermSpan span = arena.AppendScratch(text.Span);
        ExpectEnd(localName);
        return span;
    }

    private ByteBuilder ReadSimpleText()
    {
        ByteBuilder text = _tag.Scratch;
        text.Clear();
        if (_tag.IsEmpty)
        {
            throw Structure("An empty element where text was expected.");
        }

        ReadCharacterData(text);
        return text;
    }

    private void ReadCharacterData(ByteBuilder text)
    {
        while (true)
        {
            int c = _cursor.Peek();
            if (c < 0)
            {
                throw End();
            }

            if (c == '&')
            {
                ReadReference(text);
                continue;
            }

            if (c == '<')
            {
                if (SkipCData(text))
                {
                    continue;
                }

                if (_cursor.PeekAt(1) == '!' && _cursor.PeekAt(2) == '-')
                {
                    SkipMarkup();
                    continue;
                }

                if (_cursor.PeekAt(1) == '?')
                {
                    SkipMarkup();
                    continue;
                }

                if (_cursor.PeekAt(1) == '/')
                {
                    return;
                }

                throw Structure("An element inside text.");
            }

            if (c == ']' && _cursor.PeekAt(1) == ']' && _cursor.PeekAt(2) == '>')
            {
                throw Malformed("']]>' in character data.");
            }

            _cursor.Next();
            if (c == '\r')
            {
                // XML 1.0 §2.11: CR LF and a lone CR are read as LF.
                if (_cursor.Peek() == '\n')
                {
                    _cursor.Next();
                }

                text.Append((byte)'\n');
            }
            else
            {
                text.Append((byte)c);
            }
        }
    }

    private bool SkipCData(ByteBuilder? text)
    {
        if (!_cursor.TryConsume("<![CDATA["u8))
        {
            return false;
        }

        ResultsPosition start = _cursor.Position;
        while (!_cursor.TryConsume("]]>"u8))
        {
            int b = _cursor.Next();
            if (b < 0)
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedEnd, start, "An unterminated CDATA section.");
            }

            text?.Append((byte)b);
        }

        return true;
    }

    private void ReadReference(ByteBuilder into)
    {
        ResultsPosition position = _cursor.Position;
        _cursor.Next(); // '&'
        if (_cursor.Peek() == '#')
        {
            _cursor.Next();
            bool hex = false;
            if (_cursor.Peek() == 'x')
            {
                hex = true;
                _cursor.Next();
            }

            int value = 0;
            int digits = 0;
            while (true)
            {
                int c = _cursor.Next();
                if (c == ';')
                {
                    break;
                }

                int digit = c is >= '0' and <= '9' ? c - '0'
                    : hex && c is >= 'a' and <= 'f' ? c - 'a' + 10
                    : hex && c is >= 'A' and <= 'F' ? c - 'A' + 10
                    : -1;
                if (digit < 0 || value > 0x10FFFF)
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "A malformed character reference.");
                }

                value = (value * (hex ? 16 : 10)) + digit;
                digits++;
            }

            bool isChar = value is 0x9 or 0xA or 0xD or (>= 0x20 and <= 0xD7FF) or (>= 0xE000 and <= 0xFFFD) or (>= 0x10000 and <= 0x10FFFF);
            if (digits == 0 || !isChar)
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "A character reference to something that is not an XML character.");
            }

            into.AppendCodePoint(value);
            return;
        }

        Span<byte> name = stackalloc byte[5];
        int length = 0;
        while (true)
        {
            int c = _cursor.Next();
            if (c == ';')
            {
                break;
            }

            if (c < 0 || length == name.Length)
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "An unknown entity reference.");
            }

            name[length++] = (byte)c;
        }

        ReadOnlySpan<byte> entity = name[..length];
        byte replacement = entity.SequenceEqual("lt"u8) ? (byte)'<'
            : entity.SequenceEqual("gt"u8) ? (byte)'>'
            : entity.SequenceEqual("amp"u8) ? (byte)'&'
            : entity.SequenceEqual("quot"u8) ? (byte)'"'
            : entity.SequenceEqual("apos"u8) ? (byte)'\''
            : (byte)0;
        if (replacement == 0)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "An unknown entity reference; only the five predefined entities exist without a DTD.");
        }

        into.Append(replacement);
    }

    // ------------------------------------------------------------------ lexical

    private void ReadName(ByteBuilder into, out int start, out int length)
    {
        start = into.Length;
        while (true)
        {
            int c = _cursor.Peek();
            if (c < 0 || c is ' ' or '\t' or '\r' or '\n' or '>' or '/' or '=' or '"' or '\'' or '<')
            {
                break;
            }

            into.Append((byte)_cursor.Next());
        }

        length = into.Length - start;
        if (length == 0)
        {
            throw _cursor.AtEnd ? End() : Malformed("Expected a name.");
        }
    }

    private bool SkipWhitespace()
    {
        bool any = false;
        while (_cursor.Peek() is ' ' or '\t' or '\r' or '\n')
        {
            _cursor.Next();
            any = true;
        }

        return any;
    }

    private void SkipWhitespaceAndMarkup()
    {
        while (true)
        {
            SkipWhitespace();
            if (_cursor.Peek() != '<' || !SkipMarkup())
            {
                return;
            }
        }
    }

    private void CloseDocument()
    {
        ExpectEnd("sparql"u8);
        EndOfDocument();
    }

    private void EndOfDocument()
    {
        SkipWhitespaceAndMarkup();
        if (!_cursor.AtEnd)
        {
            throw Structure("Content after the sparql element.");
        }
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        int start = 0, end = value.Length;
        while (start < end && value[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        while (end > start && value[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            end--;
        }

        return value[start..end];
    }

    private ResultsSyntaxException End() =>
        new(SparqlResultsErrorKind.UnexpectedEnd, _cursor.Position, "The document ended inside an element.");

    private ResultsSyntaxException Malformed(string message) =>
        new(SparqlResultsErrorKind.Malformed, _cursor.Position, message);

    private ResultsSyntaxException Structure(string message) =>
        new(SparqlResultsErrorKind.UnexpectedStructure, _cursor.Position, message);

    /// <summary>The start tag just read: its name, attributes, and their resolved namespaces.</summary>
    private sealed class Tag
    {
        private readonly List<(int NameStart, int NameLength, int ValueStart, int ValueLength, int Namespace)> _attributes = [];
        private int _nameStart;
        private int _nameLength;
        private int _localStart;
        private int _namespaceStart;
        private int _namespaceLength;

        internal ByteBuilder Buffer { get; } = new();

        internal ByteBuilder Scratch { get; } = new();

        internal ByteBuilder Resolved { get; } = new();

        internal bool IsEmpty { get; set; }

        internal ResultsPosition Position { get; private set; }

        internal int AttributeCount => _attributes.Count;

        internal ReadOnlySpan<byte> QualifiedName => Buffer.Slice(_nameStart, _nameLength);

        internal void Clear(ResultsPosition position)
        {
            Buffer.Clear();
            Resolved.Clear();
            _attributes.Clear();
            IsEmpty = false;
            Position = position;
        }

        internal void SetName(int start, int length)
        {
            _nameStart = start;
            _nameLength = length;
        }

        internal void AddAttribute(int nameStart, int nameLength, int valueStart, int valueLength) =>
            _attributes.Add((nameStart, nameLength, valueStart, valueLength, -1));

        internal ReadOnlySpan<byte> AttributeName(int index) => Buffer.Slice(_attributes[index].NameStart, _attributes[index].NameLength);

        internal ReadOnlySpan<byte> AttributeValue(int index) => Buffer.Slice(_attributes[index].ValueStart, _attributes[index].ValueLength);

        internal bool Is(ReadOnlySpan<byte> @namespace, ReadOnlySpan<byte> localName) =>
            Buffer.Slice(_localStart, _nameStart + _nameLength - _localStart).SequenceEqual(localName)
            && Resolved.Slice(_namespaceStart, _namespaceLength).SequenceEqual(@namespace);

        /// <summary>The index of an attribute by namespace and local name; an empty namespace is no namespace.</summary>
        internal int Attribute(ReadOnlySpan<byte> @namespace, ReadOnlySpan<byte> localName)
        {
            for (int i = 0; i < _attributes.Count; i++)
            {
                ReadOnlySpan<byte> name = AttributeName(i);
                if (IsNamespaceDeclaration(name))
                {
                    continue;
                }

                int colon = name.IndexOf((byte)':');
                ReadOnlySpan<byte> local = colon < 0 ? name : name[(colon + 1)..];
                if (!local.SequenceEqual(localName))
                {
                    continue;
                }

                int ns = _attributes[i].Namespace;
                ReadOnlySpan<byte> resolved = ns < 0 ? default : Resolved.Slice(ns & 0xFFFF, ns >> 16);
                if (resolved.SequenceEqual(@namespace))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Resolves the element's and the attributes' prefixes against the namespaces in scope.</summary>
        internal void Resolve(Namespaces namespaces, ResultsPosition position)
        {
            ReadOnlySpan<byte> name = QualifiedName;
            int colon = name.IndexOf((byte)':');
            ReadOnlySpan<byte> prefix = colon < 0 ? default : name[..colon];
            _localStart = _nameStart + colon + 1;
            if (!namespaces.TryResolve(prefix, colon >= 0, out ReadOnlySpan<byte> uri))
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "An undeclared namespace prefix.");
            }

            _namespaceStart = Resolved.Length;
            Resolved.Append(uri);
            _namespaceLength = uri.Length;

            for (int i = 0; i < _attributes.Count; i++)
            {
                ReadOnlySpan<byte> attribute = AttributeName(i);
                int attributeColon = attribute.IndexOf((byte)':');
                if (attributeColon < 0 || IsNamespaceDeclaration(attribute))
                {
                    continue;
                }

                ReadOnlySpan<byte> attributePrefix = attribute[..attributeColon];
                ReadOnlySpan<byte> attributeUri;
                if (attributePrefix.SequenceEqual("xml"u8))
                {
                    attributeUri = XmlNamespace;
                }
                else if (!namespaces.TryResolve(attributePrefix, true, out attributeUri))
                {
                    throw new ResultsSyntaxException(SparqlResultsErrorKind.Malformed, position, "An undeclared namespace prefix on an attribute.");
                }

                int start = Resolved.Length;
                Resolved.Append(attributeUri);
                (int ns, int nl, int vs, int vl, _) = _attributes[i];
                _attributes[i] = (ns, nl, vs, vl, start | (attributeUri.Length << 16));
            }
        }

        internal static bool IsNamespaceDeclaration(ReadOnlySpan<byte> name) =>
            name.SequenceEqual("xmlns"u8) || name.StartsWith("xmlns:"u8);
    }

    /// <summary>
    /// The namespace declarations in scope, and the stack of open elements they
    /// belong to — the element's qualified name, so an end tag can be matched.
    /// </summary>
    private sealed class Namespaces
    {
        private readonly ByteBuilder _bytes = new();
        private readonly List<(int PrefixStart, int PrefixLength, int UriStart, int UriLength)> _declarations = [];
        private readonly List<(int Declarations, int Bytes, int NameStart, int NameLength, int NamespaceStart, int NamespaceLength)> _open = [];

        /// <summary>Opens an element: records its declarations and its name.</summary>
        internal void Push(Tag tag)
        {
            int declarations = _declarations.Count;
            int bytes = _bytes.Length;
            for (int i = 0; i < tag.AttributeCount; i++)
            {
                ReadOnlySpan<byte> name = tag.AttributeName(i);
                if (!Tag.IsNamespaceDeclaration(name))
                {
                    continue;
                }

                ReadOnlySpan<byte> prefix = name.Length > 5 ? name[6..] : default;
                int prefixStart = _bytes.Length;
                _bytes.Append(prefix);
                int uriStart = _bytes.Length;
                _bytes.Append(tag.AttributeValue(i));
                _declarations.Add((prefixStart, prefix.Length, uriStart, _bytes.Length - uriStart));
            }

            int nameStart = _bytes.Length;
            _bytes.Append(tag.QualifiedName);
            _open.Add((declarations, bytes, nameStart, tag.QualifiedName.Length, 0, 0));
        }

        internal bool TryResolve(ReadOnlySpan<byte> prefix, bool prefixed, out ReadOnlySpan<byte> uri)
        {
            for (int i = _declarations.Count - 1; i >= 0; i--)
            {
                (int ps, int pl, int us, int ul) = _declarations[i];
                if (_bytes.Slice(ps, pl).SequenceEqual(prefix))
                {
                    uri = _bytes.Slice(us, ul);
                    return true;
                }
            }

            uri = default;
            return !prefixed;
        }

        internal void PopEmpty() => Pop();

        /// <summary>Closes the innermost element if the end tag names it and it is the expected one.</summary>
        internal bool PopMatches(ReadOnlySpan<byte> endName, ReadOnlySpan<byte> localName, ReadOnlySpan<byte> @namespace)
        {
            if (_open.Count == 0)
            {
                return false;
            }

            (_, _, int nameStart, int nameLength, _, _) = _open[^1];
            ReadOnlySpan<byte> openName = _bytes.Slice(nameStart, nameLength);
            if (!openName.SequenceEqual(endName))
            {
                return false;
            }

            int colon = openName.IndexOf((byte)':');
            ReadOnlySpan<byte> prefix = colon < 0 ? default : openName[..colon];
            ReadOnlySpan<byte> local = colon < 0 ? openName : openName[(colon + 1)..];
            bool matches = local.SequenceEqual(localName)
                && TryResolve(prefix, colon >= 0, out ReadOnlySpan<byte> uri)
                && uri.SequenceEqual(@namespace);
            Pop();
            return matches;
        }

        internal bool PopMatchesAny(ReadOnlySpan<byte> endName)
        {
            if (_open.Count == 0)
            {
                return false;
            }

            (_, _, int nameStart, int nameLength, _, _) = _open[^1];
            bool matches = _bytes.Slice(nameStart, nameLength).SequenceEqual(endName);
            Pop();
            return matches;
        }

        private void Pop()
        {
            (int declarations, int bytes, _, _, _, _) = _open[^1];
            _open.RemoveAt(_open.Count - 1);
            _declarations.RemoveRange(declarations, _declarations.Count - declarations);
            _bytes.Truncate(bytes);
        }
    }
}
