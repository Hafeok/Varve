// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>The terminals of SPARQL 1.2 §19.7, plus the punctuation the grammar writes inline.</summary>
internal enum TokenKind : byte
{
    End,

    /// <summary><c>&lt;…&gt;</c>, escapes not yet processed.</summary>
    Iri,

    /// <summary><c>prefix:local</c> or <c>prefix:</c> or <c>:local</c>, escapes not yet processed.</summary>
    PrefixedName,

    /// <summary><c>_:label</c>.</summary>
    BlankNodeLabel,

    /// <summary><c>[ ]</c>.</summary>
    Anon,

    /// <summary><c>( )</c>.</summary>
    Nil,

    /// <summary><c>?name</c> or <c>$name</c>.</summary>
    Variable,

    /// <summary>A string literal of any of the four forms, escapes not yet processed.</summary>
    String,

    /// <summary><c>INTEGER</c>, signed or not.</summary>
    Integer,

    /// <summary><c>DECIMAL</c>, signed or not.</summary>
    Decimal,

    /// <summary><c>DOUBLE</c>, signed or not.</summary>
    Double,

    /// <summary><c>@tag</c> or <c>@tag--dir</c>.</summary>
    LangTag,

    /// <summary>A keyword, <c>a</c>, <c>true</c> or <c>false</c>: a run of name characters not followed by a colon.</summary>
    Word,

    LeftBrace,
    RightBrace,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Semicolon,
    Dot,
    DoubleCaret,
    Caret,
    Pipe,
    Slash,
    Star,
    Plus,
    Minus,
    Question,
    Bang,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessOrEqual,
    GreaterOrEqual,
    AndAnd,
    OrOr,
    Tilde,

    /// <summary><c>&lt;&lt;</c></summary>
    ReifiedOpen,

    /// <summary><c>&gt;&gt;</c></summary>
    ReifiedClose,

    /// <summary><c>&lt;&lt;(</c></summary>
    TripleTermOpen,

    /// <summary><c>)&gt;&gt;</c></summary>
    TripleTermClose,

    /// <summary><c>{|</c></summary>
    AnnotationOpen,

    /// <summary><c>|}</c></summary>
    AnnotationClose,
}

/// <summary>One terminal: its kind and where it is.</summary>
internal readonly struct Token
{
    internal Token(TokenKind kind, int start, int length, int line, int column)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }

    internal TokenKind Kind { get; }

    internal int Start { get; }

    internal int Length { get; }

    internal int Line { get; }

    internal int Column { get; }

    internal int End => Start + Length;

    internal SourceSpan Span => new(Start, End, Line, Column);
}
