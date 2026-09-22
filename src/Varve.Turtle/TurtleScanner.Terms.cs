using System;
using Varve.Iri;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>Subjects, verbs, objects and the terms they are made of.</summary>
internal ref partial struct TurtleScanner
{
    private static ReadOnlySpan<byte> XsdInteger => "http://www.w3.org/2001/XMLSchema#integer"u8;

    private static ReadOnlySpan<byte> XsdDecimal => "http://www.w3.org/2001/XMLSchema#decimal"u8;

    private static ReadOnlySpan<byte> XsdDouble => "http://www.w3.org/2001/XMLSchema#double"u8;

    private static ReadOnlySpan<byte> XsdBoolean => "http://www.w3.org/2001/XMLSchema#boolean"u8;

    private static ReadOnlySpan<byte> RdfFirst => "http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8;

    private static ReadOnlySpan<byte> RdfRest => "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8;

    private static ReadOnlySpan<byte> RdfNil => "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8;

    /// <summary>[7g] <c>iri | BlankNode</c>.</summary>
    private bool TryLabelOrSubject(out int slot)
    {
        slot = -1;

        if (AtEnd)
        {
            return Truncated();
        }

        if (Peek == (byte)'_' || IsAnon())
        {
            return TryBlankNode(out slot);
        }

        if (!TryIriTerm(out TermSpan span, ParseErrorKind.ExpectedSubject))
        {
            return false;
        }

        slot = _state.Arena.AddIri(span);
        return true;
    }

    /// <summary>[10] <c>iri | BlankNode | collection</c>.</summary>
    private bool TrySubject(out int slot)
    {
        slot = -1;

        if (AtEnd)
        {
            return Truncated();
        }

        return Peek switch
        {
            (byte)'(' => TryCollection(out slot),
            (byte)'[' => TryBlankNodeOrPropertyList(out slot),
            (byte)'_' => TryBlankNode(out slot),
            _ => TryIriSlot(out slot, ParseErrorKind.ExpectedSubject),
        };
    }

    /// <summary>[12] <c>iri | BlankNode | collection | blankNodePropertyList | literal</c>.</summary>
    private bool TryObject(out int slot)
    {
        slot = -1;

        if (AtEnd)
        {
            return Truncated();
        }

        byte b = Peek;

        if (b is (byte)'(')
        {
            return TryCollection(out slot);
        }

        if (b is (byte)'[')
        {
            return TryBlankNodeOrPropertyList(out slot);
        }

        if (b is (byte)'_')
        {
            return TryBlankNode(out slot);
        }

        if (b is (byte)'"' or (byte)'\'')
        {
            return TryLiteral(out slot);
        }

        if (b == (byte)'.' && Consumed + 1 >= _text.Length && MayGrow)
        {
            // A leading dot is a decimal when a digit follows and nothing at
            // all otherwise, and the digit may be in the next chunk.
            return Truncated();
        }

        if (b is (byte)'+' or (byte)'-' || NTriplesChars.IsAsciiDigit(b)
            || (b == (byte)'.' && Consumed + 1 < _text.Length && NTriplesChars.IsAsciiDigit(_text[Consumed + 1])))
        {
            return TryNumber(out slot);
        }

        if (MayGrow && (MightBeKeyword("true"u8) || MightBeKeyword("false"u8)))
        {
            return Truncated();
        }

        if (IsKeyword("true"u8) || IsKeyword("false"u8))
        {
            return TryBoolean(out slot);
        }

        return TryIriSlot(out slot, ParseErrorKind.ExpectedObject);
    }

    private bool TryIriSlot(out int slot, ParseErrorKind expected = ParseErrorKind.ExpectedIri)
    {
        slot = -1;

        if (!TryIriTerm(out TermSpan span, expected))
        {
            return false;
        }

        slot = _state.Arena.AddIri(span);
        return true;
    }

    /// <summary>[135s] <c>IRIREF | PrefixedName</c>.</summary>
    private bool TryIriTerm(out TermSpan span, ParseErrorKind expected = ParseErrorKind.ExpectedIri)
    {
        span = TermSpan.None;

        if (AtEnd)
        {
            return Truncated();
        }

        return Peek == (byte)'<'
            ? TryIriRef(out span, resolve: true)
            : TryPrefixedName(out span, expected);
    }

    /// <summary>
    /// [18] IRIREF, resolved against the in-scope base when it is relative.
    /// </summary>
    private bool TryIriRef(out TermSpan span, bool resolve)
    {
        span = TermSpan.None;
        int open = Consumed;
        int i = Consumed + 1;
        bool escaped = false;

        while (true)
        {
            if (i >= _text.Length)
            {
                return Truncated();
            }

            byte b = _text[i];

            if (b == (byte)'>')
            {
                break;
            }

            if (b == (byte)'\\')
            {
                escaped = true;

                if (!EscapeDecoder.TryMeasure(
                    _text, i, EscapeDecoder.Allowed.UcharOnly, out int length,
                    out ParseErrorKind error, out int at, out bool truncated))
                {
                    return truncated ? Truncated() : Fail(error, at);
                }

                i += length;
                continue;
            }

            if (NTriplesChars.IsForbiddenInIri(b))
            {
                return Fail(ParseErrorKind.InvalidIriCharacter, i);
            }

            i++;
        }

        TermSpan raw;

        if (escaped)
        {
            if (!EscapeDecoder.TryDecode(
                _text, open + 1, i, EscapeDecoder.Allowed.UcharOnly, _state.Arena,
                out raw, out ParseErrorKind error, out int at))
            {
                return Fail(error, at);
            }
        }
        else
        {
            raw = TermSpan.FromText(open + 1, i - (open + 1));
        }

        Consumed = i + 1;

        return resolve ? TryResolve(raw, open, out span) : Validate(raw, open, out span);
    }

    /// <summary>
    /// Resolves a reference against the base (Turtle §6.3, RFC 3986 §5.2), or
    /// takes it as it stands when it is already absolute.
    /// </summary>
    private bool TryResolve(TermSpan raw, int at, out TermSpan span)
    {
        span = TermSpan.None;
        ReadOnlySpan<byte> reference = _state.Arena.Bytes(_text, raw);

        // Whether to resolve is a question about the scheme alone. Asking
        // IsAbsolute would fold validity into it, and a caller who turned
        // validation off would then see a malformed absolute IRI treated as
        // relative and rejected for having no base.
        if (_validateIris ? IriRef.IsAbsolute(reference) : IriRef.StartsWithScheme(reference))
        {
            return Validate(raw, at, out span);
        }

        if (!_state.HasBase)
        {
            return Fail(ParseErrorKind.RelativeIri, at);
        }

        // The base is a byte[] on the state and the reference may live in the
        // arena's scratch, which resolving into would move. So the result is
        // built in scratch and the inputs are read before it is reserved.
        int needed = IriRef.ResolveLength(_state.Base, reference);

        if (needed <= 0)
        {
            return Fail(ParseErrorKind.InvalidIri, at);
        }

        Span<byte> destination = _state.Arena.ReserveScratch(needed);
        reference = _state.Arena.Bytes(_text, raw);

        if (!IriRef.TryResolve(_state.Base, reference, destination, out int written))
        {
            return Fail(ParseErrorKind.InvalidIri, at);
        }

        TermSpan resolved = _state.Arena.CommitScratch(written);
        return Validate(resolved, at, out span);
    }

    private bool Validate(TermSpan candidate, int at, out TermSpan span)
    {
        span = TermSpan.None;

        if (_validateIris)
        {
            ReadOnlySpan<byte> text = _state.Arena.Bytes(_text, candidate);

            if (!IriRef.TryValidate(text, out IriComponents components, out IriError error))
            {
                return FailIri(error.Kind, at);
            }

            if (!components.HasScheme)
            {
                return Fail(ParseErrorKind.RelativeIri, at);
            }
        }

        span = candidate;
        return true;
    }

    /// <summary>[136s] <c>PNAME_LN | PNAME_NS</c>, expanded by concatenation.</summary>
    /// <remarks>
    /// This is where a term that is not any recognised form ends up, because a
    /// prefixed name is the one production with no distinguishing first
    /// character. <paramref name="expected"/> is therefore the grammatical
    /// position the caller was filling, so that "a subject was expected here"
    /// is reported rather than a guess that an IRI was meant.
    /// </remarks>
    private bool TryPrefixedName(out TermSpan span, ParseErrorKind expected)
    {
        span = TermSpan.None;
        int start = Consumed;

        while (Consumed < _text.Length && _text[Consumed] != (byte)':')
        {
            byte b = _text[Consumed];

            if (b is 0x20 or 0x09 or 0x0A or 0x0D or (byte)'.' or (byte)';' or (byte)','
                or (byte)']' or (byte)')' or (byte)'}' or (byte)'<' or (byte)'"' or (byte)'#')
            {
                return Fail(expected, start);
            }

            Consumed++;
        }

        if (Consumed >= _text.Length && MayGrow)
        {
            Consumed = start;
            return Truncated();
        }

        if (AtEnd)
        {
            return Truncated();
        }

        int nameLength = Consumed - start;

        if (nameLength > 0 && !TurtleChars.IsPrefixName(_text.Slice(start, nameLength)))
        {
            return Fail(ParseErrorKind.InvalidPrefix, start);
        }

        if (!_state.TryResolvePrefix(_text.Slice(start, nameLength), out ReadOnlySpan<byte> namespaceIri))
        {
            return Fail(ParseErrorKind.UndeclaredPrefix, start);
        }

        Consumed++;

        if (!TryLocalName(out int localStart, out int localEnd, out bool escaped))
        {
            return false;
        }

        // Expansion is concatenation, not resolution (Turtle §6.3). The
        // namespace lives on the state and the local part in the text, so both
        // are copied into one scratch run.
        ReadOnlySpan<byte> local = _text[localStart..localEnd];
        Span<byte> destination = _state.Arena.ReserveScratch(namespaceIri.Length + local.Length);
        namespaceIri.CopyTo(destination);
        int written = namespaceIri.Length;

        if (escaped)
        {
            written += Unescape(local, destination[written..]);
        }
        else
        {
            local.CopyTo(destination[written..]);
            written += local.Length;
        }

        TermSpan expanded = _state.Arena.CommitScratch(written);
        return Validate(expanded, start, out span);
    }

    private static int Unescape(ReadOnlySpan<byte> local, Span<byte> destination)
    {
        int written = 0;

        for (int i = 0; i < local.Length; i++)
        {
            if (local[i] == (byte)'\\' && i + 1 < local.Length)
            {
                destination[written++] = local[++i];
                continue;
            }

            destination[written++] = local[i];
        }

        return written;
    }

    /// <summary>[168s] PN_LOCAL. May contain <c>.</c>, may not end with one.</summary>
    private bool TryLocalName(out int start, out int end, out bool escaped)
    {
        start = Consumed;
        end = Consumed;
        escaped = false;

        if (AtEnd)
        {
            return true;
        }

        // The first character has its own class: PN_CHARS_U | ':' | [0-9] | PLX.
        if (!TurtleChars.TryRune(_text, Consumed, out int first, out int width))
        {
            return true;
        }

        bool firstOk = first is ':' or (>= '0' and <= '9')
            || TurtleChars.IsPnCharsU(first)
            || first == '%' || first == '\\';

        if (!firstOk)
        {
            return true;
        }

        int at = Consumed;
        int lastGood = Consumed;

        while (at < _text.Length)
        {
            byte b = _text[at];

            if (b == (byte)'\\')
            {
                if (at + 1 >= _text.Length)
                {
                    return MayGrow ? Truncated() : Fail(ParseErrorKind.InvalidEscape, at);
                }

                if (!EscapeDecoder.IsLocalEscape(_text[at + 1]))
                {
                    return Fail(ParseErrorKind.InvalidEscape, at);
                }

                escaped = true;
                at += 2;
                lastGood = at;
                continue;
            }

            if (b == (byte)'%')
            {
                if (at + 2 >= _text.Length)
                {
                    return MayGrow
                        ? Truncated()
                        : Fail(ParseErrorKind.InvalidPercentEncoding, at);
                }

                if (!NTriplesChars.IsHex(_text[at + 1]) || !NTriplesChars.IsHex(_text[at + 2]))
                {
                    return Fail(ParseErrorKind.InvalidPercentEncoding, at);
                }

                at += 3;
                lastGood = at;
                continue;
            }

            if (!TurtleChars.TryRune(_text, at, out int c, out int length, out bool incomplete))
            {
                if (incomplete && MayGrow)
                {
                    return Truncated();
                }

                break;
            }

            if (c == '.')
            {
                at += length;
                continue;
            }

            if (c != ':' && !TurtleChars.IsPnChars(c))
            {
                break;
            }

            at += length;
            lastGood = at;
        }

        if (at >= _text.Length && MayGrow)
        {
            // The name ran to the end of the buffer, so the next byte may
            // extend it. Ending it here would silently produce a shorter IRI.
            return Truncated();
        }

        end = lastGood;
        Consumed = lastGood;
        return true;
    }

    /// <summary>[137s] <c>BLANK_NODE_LABEL | ANON</c>, plus [14] the property list.</summary>
    private bool TryBlankNodeOrPropertyList(out int slot)
    {
        slot = -1;

        if (IsAnon(out bool incomplete))
        {
            return TryBlankNode(out slot);
        }

        if (incomplete && MayGrow)
        {
            return Truncated();
        }

        // [14] blankNodePropertyList. The node is fresh and its triples are
        // emitted before the statement that uses it finishes — which is why
        // ADR 0030 buffers a statement.
        Consumed++;
        slot = FreshBlankNode();

        if (!PredicateObjectList(slot, Graph))
        {
            return false;
        }

        SkipIgnorable();

        if (AtEnd)
        {
            return Truncated();
        }

        if (Peek != (byte)']')
        {
            return Fail(ParseErrorKind.UnterminatedBlankNodeList, Consumed);
        }

        Consumed++;
        return true;
    }

    /// <summary>[162s] <c>'[' WS* ']'</c> — an anonymous node, not a property list.</summary>
    private readonly bool IsAnon() => IsAnon(out _);

    /// <summary>
    /// Whether <c>[</c> here opens an ANON — <c>[</c>, whitespace, <c>]</c> —
    /// rather than a blank node property list.
    /// </summary>
    /// <remarks>
    /// <paramref name="incomplete"/> is set when the whitespace runs to the end
    /// of the buffer: the <c>]</c> that would settle it may be in the next
    /// chunk, and answering "not an ANON" would start parsing a property list
    /// that does not exist.
    /// </remarks>
    private readonly bool IsAnon(out bool incomplete)
    {
        incomplete = false;

        if (Consumed >= _text.Length || _text[Consumed] != (byte)'[')
        {
            return false;
        }

        int i = Consumed + 1;

        while (i < _text.Length && _text[i] is 0x20 or 0x09 or 0x0A or 0x0D)
        {
            i++;
        }

        if (i >= _text.Length)
        {
            incomplete = true;
            return false;
        }

        return _text[i] == (byte)']';
    }

    private bool TryBlankNode(out int slot)
    {
        slot = -1;

        if (IsAnon())
        {
            while (_text[Consumed] != (byte)']')
            {
                Consumed++;
            }

            Consumed++;
            slot = FreshBlankNode();
            return true;
        }

        if (Consumed + 1 >= _text.Length)
        {
            return Truncated();
        }

        if (_text[Consumed + 1] != (byte)':')
        {
            return Fail(ParseErrorKind.InvalidBlankNodeLabel, Consumed);
        }

        int start = Consumed + 2;

        if (!TurtleChars.TryRune(_text, start, out int first, out int width))
        {
            return Truncated();
        }

        if (!TurtleChars.IsPnCharsU(first) && first is not (>= '0' and <= '9'))
        {
            return Fail(ParseErrorKind.InvalidBlankNodeLabel, start);
        }

        int at = start + width;
        int end = at;

        while (true)
        {
            if (!TurtleChars.TryRune(_text, at, out int c, out int length, out bool incomplete))
            {
                if (incomplete && MayGrow)
                {
                    return Truncated();
                }

                break;
            }

            if (c == '.')
            {
                at += length;
                continue;
            }

            if (!TurtleChars.IsPnChars(c))
            {
                break;
            }

            at += length;
            end = at;
        }

        if (at >= _text.Length && MayGrow)
        {
            // The label ran to the end of the buffer and the next byte may
            // extend it; ending it here would name a different blank node.
            return Truncated();
        }

        Consumed = end;

        // A document's label is its own (`turtle.md` §4), except for the one
        // case the naming cannot honour: a generated-form label the parser has
        // already handed out.
        ReadOnlySpan<byte> label = _text[start..end];
        int generated = BlankNodeNaming.GeneratedIndex(label);

        if (generated >= 0)
        {
            slot = GeneratedLabel(_state.Names.Claim(generated));
            return true;
        }

        Span<byte> destination = _state.Arena.ReserveScratch(label.Length);
        label.CopyTo(destination);
        slot = _state.Arena.AddBlankNode(_state.Arena.CommitScratch(label.Length));
        return true;
    }

    private readonly int FreshBlankNode() => GeneratedLabel(_state.Names.Mint());

    private readonly int GeneratedLabel(int n)
    {
        Span<byte> destination = _state.Arena.ReserveScratch(12);
        destination[0] = (byte)'g';
        bool formatted = n.TryFormat(destination[1..], out int digits, provider: null);
        int length = formatted ? digits + 1 : 1;
        return _state.Arena.AddBlankNode(_state.Arena.CommitScratch(length));
    }

    /// <summary>[15] <c>'(' object* ')'</c>, which produces an rdf:first/rdf:rest chain.</summary>
    private bool TryCollection(out int slot)
    {
        slot = -1;
        Consumed++;

        int head = -1;
        int tail = -1;

        while (true)
        {
            SkipIgnorable();

            if (AtEnd)
            {
                return Truncated();
            }

            if (Peek == (byte)')')
            {
                Consumed++;

                if (head < 0)
                {
                    // An empty collection is rdf:nil, with no triples at all.
                    slot = _state.Arena.AddIri(_state.Arena.AppendScratch(RdfNil));
                    return true;
                }

                _state.Add(tail, Iri(RdfRest), Iri(RdfNil), Graph);
                slot = head;
                return true;
            }

            if (!TryObject(out int element))
            {
                return false;
            }

            int cell = FreshBlankNode();

            if (head < 0)
            {
                head = cell;
            }
            else
            {
                _state.Add(tail, Iri(RdfRest), cell, Graph);
            }

            _state.Add(cell, Iri(RdfFirst), element, Graph);
            tail = cell;
        }
    }

    private readonly int Iri(ReadOnlySpan<byte> text) =>
        _state.Arena.AddIri(_state.Arena.AppendScratch(text));

    private readonly bool IsKeyword(ReadOnlySpan<byte> keyword)
    {
        if (!Looks(keyword))
        {
            return false;
        }

        int after = Consumed + keyword.Length;
        return after >= _text.Length || !TurtleChars.IsPnChars(_text[after]);
    }

    /// <summary>
    /// Whether the buffer ends part-way through <paramref name="keyword"/>, or
    /// exactly at its end with the byte that would settle it still to come:
    /// <c>true</c> at a chunk boundary may yet turn out to be <c>truer:x</c>.
    /// </summary>
    private readonly bool MightBeKeyword(ReadOnlySpan<byte> keyword) =>
        MightBe(keyword) || (Looks(keyword) && Consumed + keyword.Length >= _text.Length);

    private bool TryBoolean(out int slot)
    {
        bool value = IsKeyword("true"u8);
        Consumed += value ? 4 : 5;

        TermSpan lexical = _state.Arena.AppendScratch(value ? "true"u8 : "false"u8);
        TermSpan datatype = _state.Arena.AppendScratch(XsdBoolean);
        slot = _state.Arena.AddLiteral(lexical, datatype, TermSpan.None, TextDirection.None);
        return true;
    }

    /// <summary>[16] INTEGER, DECIMAL or DOUBLE, whichever the shape turns out to be.</summary>
    private bool TryNumber(out int slot)
    {
        slot = -1;
        int start = Consumed;

        if (Peek is (byte)'+' or (byte)'-')
        {
            Consumed++;
        }

        int digitsBefore = TakeDigits();
        bool isDecimal = false;
        bool isDouble = false;

        if (!AtEnd && Peek == (byte)'.')
        {
            if (Consumed + 1 >= _text.Length)
            {
                // Whether this dot belongs to the number or ends the statement
                // is the next byte's answer. Mid-stream, guessing "statement"
                // would emit "1" for a "1.5" cut in half — a wrong quad, not an
                // error. At the document's end there is no next byte, and the
                // dot can only be the statement's.
                if (MayGrow)
                {
                    return Truncated();
                }
            }
            else if (NTriplesChars.IsAsciiDigit(_text[Consumed + 1]))
            {
                isDecimal = true;
                Consumed++;
                TakeDigits();
            }
        }

        if (!AtEnd && (Peek | 0x20) == 'e')
        {
            int mark = Consumed;
            Consumed++;

            if (!AtEnd && Peek is (byte)'+' or (byte)'-')
            {
                Consumed++;
            }

            if (TakeDigits() == 0)
            {
                if (AtEnd && MayGrow)
                {
                    // "1.5e" at a boundary: the exponent's digits may be in the
                    // next chunk. Backtracking here yields the decimal "1.5"
                    // and leaves an "e" that nothing can parse.
                    return Truncated();
                }

                Consumed = mark;
            }
            else
            {
                isDouble = true;
            }
        }

        if (AtEnd && MayGrow)
        {
            // A number is delimited by what follows it, and nothing follows
            // yet.
            return Truncated();
        }

        if (digitsBefore == 0 && !isDecimal)
        {
            return Fail(ParseErrorKind.InvalidNumber, start);
        }

        ReadOnlySpan<byte> datatype = isDouble ? XsdDouble : isDecimal ? XsdDecimal : XsdInteger;

        slot = _state.Arena.AddLiteral(
            TermSpan.FromText(start, Consumed - start),
            _state.Arena.AppendScratch(datatype),
            TermSpan.None,
            TextDirection.None);

        return true;
    }

    private int TakeDigits()
    {
        int taken = 0;

        while (!AtEnd && NTriplesChars.IsAsciiDigit(Peek))
        {
            Consumed++;
            taken++;
        }

        return taken;
    }
}
