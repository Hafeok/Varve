// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Sparql.Results;

/// <summary>
/// Reads a SPARQL result document — XML, JSON, CSV or TSV — as a head and then
/// one solution at a time, over UTF-8 (<c>sparql-results.md</c> §2).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReadHead"/> reads the variables, or the boolean of an <c>ASK</c>
/// result; <see cref="Read"/> then advances through the solutions, each
/// available as <see cref="Current"/>, a view valid until the next
/// <see cref="Read"/>. A term that must outlive it is materialised
/// (<see cref="RdfTermView.Materialise"/>, ADR 0024).
/// </para>
/// <para>
/// The first error stops the reader: both methods return false and
/// <see cref="Error"/> says what and where. There is no recovery, because a
/// malformed result document has no useful remainder.
/// </para>
/// </remarks>
public sealed class SparqlResultsReader
{
    private readonly FormatParser _parser;
    private readonly TermArena _arena = new();
    private int[] _bindings = [];
    private bool _headRead;
    private bool _failed;
    private bool _positioned;

    /// <summary>A reader over a document held as a sequence of segments.</summary>
    public SparqlResultsReader(ReadOnlySequence<byte> utf8, SparqlResultsFormat format)
    {
        Format = format;
        _parser = format switch
        {
            SparqlResultsFormat.Xml => new XmlResultsParser(utf8),
            SparqlResultsFormat.Json => new JsonResultsParser(utf8),
            SparqlResultsFormat.Csv => new CsvResultsParser(utf8),
            SparqlResultsFormat.Tsv => new TsvResultsParser(utf8),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>A reader over a document held in one block.</summary>
    public SparqlResultsReader(ReadOnlyMemory<byte> utf8, SparqlResultsFormat format)
        : this(new ReadOnlySequence<byte>(utf8), format)
    {
    }

    /// <summary>The format being read.</summary>
    public SparqlResultsFormat Format { get; }

    /// <summary>The variables the head declares, in its order. Empty until <see cref="ReadHead"/>.</summary>
    public IReadOnlyList<string> Variables => _parser.Variables;

    /// <summary>Whether the document is a boolean result — the answer to an <c>ASK</c>.</summary>
    public bool IsBoolean => _parser.IsBoolean;

    /// <summary>The answer of a boolean result.</summary>
    public bool Boolean => _parser.Boolean;

    /// <summary>The first error, or none.</summary>
    public SparqlResultsError Error { get; private set; }

    /// <summary>The solution <see cref="Read"/> last advanced to.</summary>
    /// <exception cref="InvalidOperationException">No solution is current.</exception>
    public SolutionView Current => _positioned
        ? new SolutionView(_arena, _bindings)
        : throw new InvalidOperationException("No solution is current: call Read, and use the view only while it returns true.");

    /// <summary>Reads the head. False if the document is malformed; <see cref="Error"/> says how.</summary>
    public bool ReadHead()
    {
        if (_headRead)
        {
            return !_failed;
        }

        _headRead = true;
        try
        {
            _parser.ReadHead();
            _bindings = new int[_parser.Variables.Count];
            return true;
        }
        catch (ResultsSyntaxException error)
        {
            Fail(error);
            return false;
        }
    }

    /// <summary>
    /// Advances to the next solution: true when there is one, false at the end
    /// of the document and on an error. Reads the head first if it has not been.
    /// </summary>
    public bool Read()
    {
        _positioned = false;
        if (!ReadHead() || _failed)
        {
            return false;
        }

        _arena.Reset();
        Array.Fill(_bindings, -1);
        try
        {
            _positioned = _parser.ReadSolution(_arena, _bindings);
            return _positioned;
        }
        catch (ResultsSyntaxException error)
        {
            Fail(error);
            return false;
        }
    }

    private void Fail(ResultsSyntaxException error)
    {
        _failed = true;
        _positioned = false;
        Error = error.ToError();
    }
}

/// <summary>
/// One solution of a result document: for each variable of the head, its term
/// or nothing. Valid until the reader's next <see cref="SparqlResultsReader.Read"/>.
/// </summary>
public readonly ref struct SolutionView
{
    private readonly TermArena _arena;
    private readonly ReadOnlySpan<int> _bindings;

    internal SolutionView(TermArena arena, ReadOnlySpan<int> bindings)
    {
        _arena = arena;
        _bindings = bindings;
    }

    /// <summary>The number of variables, bound or not.</summary>
    public int Count => _bindings.Length;

    /// <summary>The term bound to the <paramref name="variable"/>-th variable, or false when it is unbound.</summary>
    public bool TryGet(int variable, out RdfTermView term)
    {
        int index = _bindings[variable];
        if (index < 0)
        {
            term = default;
            return false;
        }

        term = _arena.View(default, index);
        return true;
    }
}
