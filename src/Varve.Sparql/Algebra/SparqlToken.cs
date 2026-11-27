// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Sparql.Algebra;

/// <summary>
/// One token of <see cref="Varve.Sparql.SparqlTokenizer"/>: its kind, and where it starts
/// and ends in the input, in the input's units (bytes over UTF-8, UTF-16 code
/// units over UTF-16).
/// </summary>
[DesignDecision(typeof(SyntaxModelSurfaces.SourceCoordinatesAreOffsetsLinesAndColumns), Scope = ExceptionScope.Boundary)]
public readonly record struct SparqlToken(SparqlTokenKind Kind, long Start, long End);

/// <summary>The kinds of <see cref="SparqlToken"/> (<c>docs/spec/sparql-grammar.md</c> §10).</summary>
public enum SparqlTokenKind : byte
{
    /// <summary><c>&lt;…&gt;</c>.</summary>
    Iri,

    /// <summary><c>prefix:local</c>, <c>prefix:</c> or <c>:local</c>.</summary>
    PrefixedName,

    /// <summary><c>_:label</c>.</summary>
    BlankNodeLabel,

    /// <summary><c>[ ]</c>.</summary>
    Anon,

    /// <summary><c>( )</c>.</summary>
    Nil,

    /// <summary><c>?name</c> or <c>$name</c>.</summary>
    Variable,

    /// <summary>A string literal of any of the four forms.</summary>
    String,

    /// <summary>An integer, signed or not.</summary>
    Integer,

    /// <summary>A decimal, signed or not.</summary>
    Decimal,

    /// <summary>A double, signed or not.</summary>
    Double,

    /// <summary><c>@tag</c> or <c>@tag--dir</c>.</summary>
    LangTag,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A keyword of the grammar, ignoring ASCII case; <c>a</c> in lower case only.</summary>
    Keyword,

    /// <summary>Any other word.</summary>
    Word,

    /// <summary><c># …</c> to the end of its line, the break not included.</summary>
    Comment,

    /// <summary><c>{</c>.</summary>
    OpenBrace,

    /// <summary><c>}</c>.</summary>
    CloseBrace,

    /// <summary><c>(</c>.</summary>
    OpenParen,

    /// <summary><c>)</c>.</summary>
    CloseParen,

    /// <summary><c>[</c>.</summary>
    OpenBracket,

    /// <summary><c>]</c>.</summary>
    CloseBracket,

    /// <summary><c>,</c> <c>;</c> <c>.</c> or <c>^^</c>.</summary>
    Punctuation,

    /// <summary>Every other single- or double-character terminal: <c>^ | / * + - ? ! = != &lt; &gt; &lt;= &gt;= &amp;&amp; || ~</c>.</summary>
    Operator,

    /// <summary><c>&lt;&lt;</c>.</summary>
    ReifiedOpen,

    /// <summary><c>&gt;&gt;</c>.</summary>
    ReifiedClose,

    /// <summary><c>&lt;&lt;(</c>.</summary>
    TripleTermOpen,

    /// <summary><c>)&gt;&gt;</c>.</summary>
    TripleTermClose,

    /// <summary><c>{|</c>.</summary>
    AnnotationOpen,

    /// <summary><c>|}</c>.</summary>
    AnnotationClose,
}
