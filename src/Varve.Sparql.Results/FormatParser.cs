// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;
using Varve.Rdf;

namespace Varve.Sparql.Results;

/// <summary>What one format's parser does for the reader.</summary>
internal abstract class FormatParser
{
    private readonly List<byte[]> _names = [];

    internal List<string> Variables { get; } = [];

    internal bool IsBoolean { get; set; }

    internal bool Boolean { get; set; }

    /// <summary>Reads to the end of the head. Throws <see cref="ResultsSyntaxException"/>.</summary>
    internal abstract void ReadHead();

    /// <summary>
    /// Reads one solution into the arena, setting <paramref name="bindings"/>[i]
    /// to the arena index of variable i's term; the caller has filled it with -1.
    /// False at the end. Throws <see cref="ResultsSyntaxException"/>.
    /// </summary>
    internal abstract bool ReadSolution(TermArena arena, int[] bindings);

    protected void AddVariable(ReadOnlySpan<byte> name, ResultsPosition position)
    {
        if (name.IsEmpty || !Utf8.IsValid(name))
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnexpectedStructure, position, "A variable name is empty or not UTF-8.");
        }

        if (IndexOf(name) >= 0)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnknownVariable, position, "The head declares a variable twice.");
        }

        _names.Add(name.ToArray());
        Variables.Add(Encoding.UTF8.GetString(name));
    }

    protected int IndexOf(ReadOnlySpan<byte> name)
    {
        for (int i = 0; i < _names.Count; i++)
        {
            if (name.SequenceEqual(_names[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The index of a bound variable, refusing an undeclared one and a repeat.</summary>
    protected int Bind(ReadOnlySpan<byte> name, int[] bindings, ResultsPosition position)
    {
        int index = IndexOf(name);
        if (index < 0)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnknownVariable, position, "A binding names a variable the head does not declare.");
        }

        if (bindings[index] >= 0)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.UnknownVariable, position, "A solution binds one variable twice.");
        }

        return index;
    }

    /// <summary>Refuses bytes that are not UTF-8, at the position given.</summary>
    protected static void RequireUtf8(TermArena arena, TermSpan span, ResultsPosition position)
    {
        if (span.IsPresent && !Utf8.IsValid(arena.Bytes(default, span)))
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidUtf8, position, "A term is not valid UTF-8.");
        }
    }

    /// <summary>
    /// Adds a literal, reading a base direction from its text form. An empty
    /// datatype span and an empty language is a simple literal.
    /// </summary>
    protected static int AddLiteral(TermArena arena, TermSpan lexical, TermSpan datatype, TermSpan language, ReadOnlySpan<byte> direction, ResultsPosition position)
    {
        TextDirection dir = TextDirection.None;
        if (!direction.IsEmpty)
        {
            if (direction.SequenceEqual("ltr"u8))
            {
                dir = TextDirection.LeftToRight;
            }
            else if (direction.SequenceEqual("rtl"u8))
            {
                dir = TextDirection.RightToLeft;
            }
            else
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A base direction is neither ltr nor rtl.");
            }

            if (!language.IsPresent)
            {
                throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A base direction without a language tag.");
            }
        }

        if (language.IsPresent && datatype.IsPresent)
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A literal with both a language tag and a datatype.");
        }

        if (language.IsPresent && !LanguageTag.IsWellFormed(arena.Bytes(default, language)))
        {
            throw new ResultsSyntaxException(SparqlResultsErrorKind.InvalidTerm, position, "A language tag is not well-formed.");
        }

        RequireUtf8(arena, lexical, position);
        RequireUtf8(arena, datatype, position);
        return arena.AddLiteral(lexical, datatype, language, dir);
    }
}
