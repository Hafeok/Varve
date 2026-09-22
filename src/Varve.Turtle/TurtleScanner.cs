// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Iri;

namespace Varve.Turtle;

/// <summary>What one attempt at a statement came to.</summary>
internal enum StatementStatus : byte
{
    /// <summary>A directive or a set of triples, complete and well-formed.</summary>
    Complete,

    /// <summary>Nothing left but whitespace and comments.</summary>
    EndOfInput,

    /// <summary>
    /// The input ran out mid-statement. In a streaming parse that means "read
    /// more"; at the end of the document it is an error.
    /// </summary>
    Incomplete,

    /// <summary>Rejected. See <see cref="TurtleScanner.Error"/>.</summary>
    Error,
}

/// <summary>
/// Turtle and TriG over one span, a statement at a time.
/// </summary>
/// <remarks>
/// A <c>ref struct</c> over the caller's buffer, with everything that outlives a
/// statement in <see cref="TurtleState"/>. Terms go into the state's arena and
/// quads accumulate there until the terminating <c>.</c>, because ADR 0030
/// makes a statement all-or-nothing.
/// </remarks>
internal ref partial struct TurtleScanner
{
    private readonly ReadOnlySpan<byte> _text;
    private readonly TurtleState _state;
    private readonly bool _validateIris;
    private readonly PrefixHandler? _onPrefix;
    private readonly BaseHandler? _onBase;

    internal TurtleScanner(
        ReadOnlySpan<byte> text, TurtleState state, in TurtleOptions options, bool mayGrow)
    {
        _text = text;
        _state = state;
        Syntax = options.Syntax;
        _validateIris = options.ValidateIris;
        _onPrefix = options.OnPrefix;
        _onBase = options.OnBase;
        MayGrow = mayGrow;
        Consumed = 0;
        IsTruncated = false;
        Error = ParseErrorKind.None;
        IriError = IriErrorKind.None;
        ErrorOffset = 0;
        StatementStart = 0;
        Graph = -1;
    }

    /// <summary>Which syntax this scanner is reading.</summary>
    internal RdfSyntax Syntax { get; }

    /// <summary>
    /// Whether the cursor sits at the end of a buffer that more input may yet
    /// extend.
    /// </summary>
    /// <remarks>
    /// Turtle has several tokens whose end is settled by the byte after them —
    /// a number, a language tag, a keyword. Mid-stream such a token must wait;
    /// on the document's last buffer it must be decided, because nothing more
    /// is coming. Every one of those places asks this rather than assuming one
    /// answer, which is what keeps <c>&lt;s&gt; &lt;p&gt; 1.</c> a complete
    /// document and <c>1.</c> at a chunk boundary an unfinished decimal.
    /// </remarks>
    private bool MayGrow { get; }

    /// <summary>Why the last statement was rejected.</summary>
    internal ParseErrorKind Error { get; private set; }

    /// <summary>What the IRI validator said, when it was the IRI validator that objected.</summary>
    internal IriErrorKind IriError { get; private set; }

    /// <summary>The byte within the span the scanner gave up at.</summary>
    internal int ErrorOffset { get; private set; }

    /// <summary>Where the statement just attempted began.</summary>
    internal int StatementStart { get; private set; }

    /// <summary>How far the scanner has consumed. Also its cursor.</summary>
    internal int Consumed { get; private set; }

    /// <summary>
    /// The graph the current statement's quads belong to, as an arena slot, or
    /// -1 for the default graph. TriG's <c>GRAPH</c> blocks set it.
    /// </summary>
    internal int Graph { get; private set; }

    /// <summary>
    /// Reads one statement. On <see cref="StatementStatus.Complete"/> the
    /// state's pending quads are the statement's, ready to emit.
    /// </summary>
    internal StatementStatus Next()
    {
        Error = ParseErrorKind.None;
        IriError = IriErrorKind.None;
        IsTruncated = false;
        Graph = -1;

        SkipIgnorable();
        StatementStart = Consumed;

        if (IsTruncated)
        {
            // Only SkipIgnorable sets this before a statement has begun, and
            // only for a comment it declined to consume.
            return StatementStatus.Incomplete;
        }

        if (Consumed >= _text.Length)
        {
            return StatementStatus.EndOfInput;
        }

        _state.BeginStatement();

        return Syntax == RdfSyntax.TriG ? TriGBlock() : TurtleStatement();
    }

    /// <summary>
    /// Steps over whitespace and comments. A comment runs to the end of a line
    /// and is whitespace (Turtle §6.2); a <c>#</c> inside an IRI or a string is
    /// content and is never seen here, because this only runs between terms.
    /// </summary>
    private void SkipIgnorable()
    {
        while (Consumed < _text.Length)
        {
            byte b = _text[Consumed];

            if (b is 0x20 or 0x09 or 0x0A or 0x0D)
            {
                Consumed++;
                continue;
            }

            if (b != (byte)'#')
            {
                return;
            }

            int scan = Consumed + 1;

            while (scan < _text.Length && _text[scan] is not (0x0A or 0x0D))
            {
                scan++;
            }

            if (scan >= _text.Length && MayGrow)
            {
                // The comment has no newline in this buffer, so the rest of it
                // — and whatever follows it on the line — is in the next one.
                // Consuming it here would resume the parse in the middle of a
                // comment's text and read it as Turtle.
                Consumed = _text.Length;
                Truncated();
                return;
            }

            Consumed = scan;
        }
    }

    private readonly bool AtEnd => Consumed >= _text.Length;

    private readonly byte Peek => _text[Consumed];

    private readonly bool Looks(ReadOnlySpan<byte> token) =>
        _text[Consumed..].StartsWith(token);

    /// <summary>
    /// Whether what is left is too short to be <paramref name="token"/> and
    /// matches it as far as it goes.
    /// </summary>
    /// <remarks>
    /// Every keyword decision in the scanner is made on a buffer that may end
    /// mid-token, and <see cref="Looks"/> answers "no" to both "this is not the
    /// keyword" and "I cannot see enough of it yet". Told apart, the second is
    /// truncation and the statement waits for the next chunk; conflated, a
    /// one-byte read of <c>@prefix</c> is an unknown directive.
    /// </remarks>
    private readonly bool MightBe(ReadOnlySpan<byte> token)
    {
        int have = _text.Length - Consumed;
        return have < token.Length && _text[Consumed..].SequenceEqual(token[..have]);
    }

    /// <summary><see cref="MightBe"/> for the case-insensitive keywords.</summary>
    private readonly bool MightBeIgnoringCase(ReadOnlySpan<byte> token)
    {
        int have = _text.Length - Consumed;

        if (have >= token.Length)
        {
            return false;
        }

        for (int i = 0; i < have; i++)
        {
            if ((_text[Consumed + i] | 0x20) != (token[i] | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Case-insensitive, for SPARQL-style <c>PREFIX</c> and <c>BASE</c>.</summary>
    private readonly bool LooksIgnoringCase(ReadOnlySpan<byte> token)
    {
        if (_text.Length - Consumed < token.Length)
        {
            return false;
        }

        for (int i = 0; i < token.Length; i++)
        {
            if ((_text[Consumed + i] | 0x20) != (token[i] | 0x20))
            {
                return false;
            }
        }

        return true;
    }

    private bool Fail(ParseErrorKind kind, int at)
    {
        if (Error == ParseErrorKind.None)
        {
            Error = kind;
            ErrorOffset = at;
        }

        return false;
    }

    private bool FailIri(IriErrorKind iri, int at)
    {
        IriError = iri;
        return Fail(ParseErrorKind.InvalidIri, at);
    }

    /// <summary>
    /// Runs out of input mid-statement. Distinct from an error: a streaming
    /// parse answers it by reading more, and only the end of the document turns
    /// it into <see cref="ParseErrorKind.UnexpectedEnd"/>.
    /// </summary>
    private bool Truncated()
    {
        Error = ParseErrorKind.None;
        IriError = IriErrorKind.None;
        IsTruncated = true;
        return false;
    }

    /// <summary>
    /// Whether the last attempt ran out of input rather than finding a fault.
    /// A streaming parse answers that by reading more; the end of the document
    /// turns it into <see cref="ParseErrorKind.UnexpectedEnd"/>.
    /// </summary>
    internal bool IsTruncated { get; private set; }

    /// <summary>
    /// Skips to just past the next <c>.</c> at nesting depth zero, outside a
    /// String and an IRIREF — ADR 0030's recovery rule. Returns false when
    /// there is no such point, which ends the parse.
    /// </summary>
    internal bool Resynchronise()
    {
        int depth = 0;

        while (Consumed < _text.Length)
        {
            byte b = _text[Consumed];

            switch (b)
            {
                case (byte)'#':
                    while (Consumed < _text.Length && _text[Consumed] is not (0x0A or 0x0D))
                    {
                        Consumed++;
                    }

                    continue;

                case (byte)'<':
                    SkipQuoted((byte)'>');
                    continue;

                case (byte)'"':
                case (byte)'\'':
                    SkipString(b);
                    continue;

                case (byte)'[':
                case (byte)'(':
                case (byte)'{':
                    depth++;
                    break;

                case (byte)']':
                case (byte)')':
                case (byte)'}':
                    if (depth > 0)
                    {
                        depth--;
                    }

                    break;

                case (byte)'.':
                    if (depth == 0)
                    {
                        Consumed++;
                        return true;
                    }

                    break;

                default:
                    break;
            }

            Consumed++;
        }

        return false;
    }

    private void SkipQuoted(byte terminator)
    {
        Consumed++;

        while (Consumed < _text.Length && _text[Consumed] != terminator)
        {
            Consumed += _text[Consumed] == (byte)'\\' ? 2 : 1;
        }

        if (Consumed < _text.Length)
        {
            Consumed++;
        }
    }

    private void SkipString(byte quote)
    {
        bool longForm = Consumed + 2 < _text.Length && _text[Consumed + 1] == quote && _text[Consumed + 2] == quote;
        int width = longForm ? 3 : 1;
        Consumed += width;

        while (Consumed < _text.Length)
        {
            if (_text[Consumed] == (byte)'\\')
            {
                Consumed += 2;
                continue;
            }

            if (_text[Consumed] == quote
                && (!longForm || (Consumed + 2 < _text.Length && _text[Consumed + 1] == quote && _text[Consumed + 2] == quote)))
            {
                Consumed += width;
                return;
            }

            Consumed++;
        }
    }
}
