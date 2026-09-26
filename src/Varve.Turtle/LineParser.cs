// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Iri;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>What one line of input turned out to be.</summary>
internal enum LineStatus : byte
{
    /// <summary>Whitespace, a comment, or both. No quad and no error.</summary>
    Empty,

    /// <summary>A well-formed statement.</summary>
    Quad,

    /// <summary>A rejected line.</summary>
    Error,
}

/// <summary>
/// One line of N-Triples or N-Quads, parsed into term slots on an arena.
/// </summary>
/// <remarks>
/// The line is the unit of both parsing and recovery (<c>docs/spec/n-triples.md</c>
/// §3), so this type never looks beyond the span it was given and never
/// allocates: a term without escapes is a range of the line, and a term with
/// escapes is decoded into the arena's scratch.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal ref partial struct LineParser
{
    private readonly ReadOnlySpan<byte> _line;
    private readonly TermArena _arena;
    private readonly RdfSyntax _syntax;
    private readonly bool _validateIris;
    private int _at;

    internal LineParser(ReadOnlySpan<byte> line, TermArena arena, RdfSyntax syntax, bool validateIris)
    {
        _line = line;
        _arena = arena;
        _syntax = syntax;
        _validateIris = validateIris;
        _at = 0;
        Error = ParseErrorKind.None;
        IriError = IriErrorKind.None;
        ErrorOffset = 0;
    }

    /// <summary>Why the line was rejected.</summary>
    internal ParseErrorKind Error { get; private set; }

    /// <summary>What the IRI validator said, when it was the IRI validator that objected.</summary>
    internal IriErrorKind IriError { get; private set; }

    /// <summary>The byte within the line the parser gave up at.</summary>
    internal int ErrorOffset { get; private set; }

    /// <summary>
    /// Reads the line. On <see cref="LineStatus.Quad"/> the four outputs are
    /// arena slot indices, with <paramref name="graph"/> negative for the
    /// default graph.
    /// </summary>
    internal LineStatus Parse(out int subject, out int predicate, out int obj, out int graph)
    {
        subject = -1;
        predicate = -1;
        obj = -1;
        graph = -1;

        SkipWhitespace();

        if (AtEndOfContent())
        {
            return LineStatus.Empty;
        }

        if (!TrySubject(out subject))
        {
            return LineStatus.Error;
        }

        SkipWhitespace();

        if (!TryPredicate(out predicate))
        {
            return LineStatus.Error;
        }

        SkipWhitespace();

        if (!TryObject(out obj))
        {
            return LineStatus.Error;
        }

        SkipWhitespace();

        if (!TryGraph(out graph))
        {
            return LineStatus.Error;
        }

        SkipWhitespace();

        if (_at >= _line.Length)
        {
            Fail(ParseErrorKind.UnexpectedEnd, _at);
            return LineStatus.Error;
        }

        if (_line[_at] != (byte)'.')
        {
            Fail(ParseErrorKind.ExpectedDot, _at);
            return LineStatus.Error;
        }

        _at++;
        SkipWhitespace();

        if (!AtEndOfContent())
        {
            Fail(ParseErrorKind.TrailingContent, _at);
            return LineStatus.Error;
        }

        return LineStatus.Quad;
    }

    private bool TrySubject(out int slot)
    {
        slot = -1;

        if (_line[_at] == (byte)'<' && !StartsWithTripleTerm())
        {
            return TryIriSlot(out slot);
        }

        if (StartsWithBlankNode())
        {
            return TryBlankNodeSlot(out slot);
        }

        return Fail(ParseErrorKind.ExpectedSubject, _at);
    }

    private bool TryPredicate(out int slot)
    {
        slot = -1;

        if (_at >= _line.Length)
        {
            return Fail(ParseErrorKind.UnexpectedEnd, _at);
        }

        return _line[_at] == (byte)'<' && !StartsWithTripleTerm()
            ? TryIriSlot(out slot)
            : Fail(ParseErrorKind.ExpectedPredicate, _at);
    }

    private bool TryObject(out int slot)
    {
        slot = -1;

        if (_at >= _line.Length)
        {
            return Fail(ParseErrorKind.UnexpectedEnd, _at);
        }

        byte b = _line[_at];

        if (StartsWithTripleTerm())
        {
            return TryTripleTermSlot(out slot);
        }

        if (b == (byte)'<')
        {
            return TryIriSlot(out slot);
        }

        if (StartsWithBlankNode())
        {
            return TryBlankNodeSlot(out slot);
        }

        return b == (byte)'"'
            ? TryLiteralSlot(out slot)
            : Fail(ParseErrorKind.ExpectedObject, _at);
    }

    private bool TryGraph(out int slot)
    {
        slot = -1;

        if (_at >= _line.Length || _line[_at] == (byte)'.')
        {
            return true;
        }

        bool isTerm = _line[_at] == (byte)'<' || StartsWithBlankNode();

        if (!isTerm)
        {
            return Fail(ParseErrorKind.ExpectedDot, _at);
        }

        if (_syntax == RdfSyntax.NTriples)
        {
            return Fail(ParseErrorKind.GraphLabelNotAllowed, _at);
        }

        if (StartsWithTripleTerm())
        {
            return Fail(ParseErrorKind.ExpectedGraphLabel, _at);
        }

        return _line[_at] == (byte)'<' ? TryIriSlot(out slot) : TryBlankNodeSlot(out slot);
    }

    private void SkipWhitespace()
    {
        while (_at < _line.Length && NTriplesChars.IsWhitespace(_line[_at]))
        {
            _at++;
        }
    }

    /// <summary>True at the end of the line, or at a comment, which runs to it.</summary>
    private readonly bool AtEndOfContent() => _at >= _line.Length || _line[_at] == (byte)'#';

    private readonly bool StartsWithBlankNode() =>
        _at + 1 < _line.Length && _line[_at] == (byte)'_' && _line[_at + 1] == (byte)':';

    private readonly bool StartsWithTripleTerm() =>
        _at + 2 < _line.Length
        && _line[_at] == (byte)'<'
        && _line[_at + 1] == (byte)'<'
        && _line[_at + 2] == (byte)'(';

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
}
