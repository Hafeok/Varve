// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Sparql.Algebra;

/// <summary>An expression (SPARQL 1.2 §17).</summary>
public abstract record Expression : AlgebraNode
{
    private protected Expression()
    {
    }
}

/// <summary>A variable.</summary>
public sealed record VariableExpression(Variable Variable) : Expression;

/// <summary>A constant: an IRI, a literal, or a ground triple term.</summary>
public sealed record ConstantExpression(RdfTerm Term) : Expression;

/// <summary><c>!e</c>, <c>+e</c>, <c>-e</c>.</summary>
public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand) : Expression;

/// <summary>A binary operator applied to two expressions.</summary>
public sealed record BinaryExpression(BinaryOperator Operator, Expression Left, Expression Right) : Expression;

/// <summary>
/// A call of a built-in function or functional form, <c>BOUND</c>, <c>IF</c>,
/// <c>COALESCE</c>, <c>IN</c> and <c>NOT IN</c> included; for the last two the
/// tested expression comes first.
/// </summary>
public sealed record FunctionCall(BuiltInFunction Function, AlgebraList<Expression> Arguments) : Expression;

/// <summary>A call of a function named by IRI, an XSD constructor included.</summary>
public sealed record CustomFunctionCall(RdfTerm Function, AlgebraList<Expression> Arguments) : Expression;

/// <summary><c>EXISTS { }</c> and <c>NOT EXISTS { }</c>.</summary>
public sealed record ExistsExpression(QueryPattern Pattern, bool Negated) : Expression;

/// <summary>
/// An aggregate, kept where it was written above a <see cref="Group"/>
/// (<c>docs/spec/sparql-algebra.md</c> §4.6). <see cref="Argument"/> is
/// <see langword="null"/> for <c>COUNT(*)</c>; <see cref="Separator"/> is
/// <c>GROUP_CONCAT</c>'s, <see langword="null"/> when not given;
/// <see cref="CustomFunction"/> is set when <see cref="Function"/> is
/// <see cref="AggregateFunction.Custom"/>.
/// </summary>
public sealed record AggregateExpression(
    AggregateFunction Function,
    Expression? Argument,
    bool Distinct,
    string? Separator,
    RdfTerm? CustomFunction) : Expression;

/// <summary>The unary operators.</summary>
public enum UnaryOperator : byte
{
    /// <summary><c>!</c></summary>
    Not,

    /// <summary><c>+</c></summary>
    Plus,

    /// <summary><c>-</c></summary>
    Minus,
}

/// <summary>The binary operators, in SPARQL's precedence order from lowest.</summary>
public enum BinaryOperator : byte
{
    /// <summary><c>||</c></summary>
    Or,

    /// <summary><c>&amp;&amp;</c></summary>
    And,

    /// <summary><c>=</c></summary>
    Equal,

    /// <summary><c>!=</c></summary>
    NotEqual,

    /// <summary><c>&lt;</c></summary>
    Less,

    /// <summary><c>&gt;</c></summary>
    Greater,

    /// <summary><c>&lt;=</c></summary>
    LessOrEqual,

    /// <summary><c>&gt;=</c></summary>
    GreaterOrEqual,

    /// <summary><c>+</c></summary>
    Add,

    /// <summary><c>-</c></summary>
    Subtract,

    /// <summary><c>*</c></summary>
    Multiply,

    /// <summary><c>/</c></summary>
    Divide,
}

/// <summary>
/// The keyword functions of production <c>[141] BuiltInCall</c>, and the
/// functional forms that take an expression list. <c>URI</c> is
/// <see cref="Iri"/>; <c>isURI</c> is <see cref="IsIri"/>.
/// </summary>
public enum BuiltInFunction : byte
{
    /// <summary><c>STR</c></summary>
    Str,

    /// <summary><c>LANG</c></summary>
    Lang,

    /// <summary><c>LANGMATCHES</c></summary>
    LangMatches,

    /// <summary><c>LANGDIR</c>. SPARQL 1.2.</summary>
    LangDir,

    /// <summary><c>DATATYPE</c></summary>
    Datatype,

    /// <summary><c>BOUND</c>; the one argument is a <see cref="VariableExpression"/>.</summary>
    Bound,

    /// <summary><c>IRI</c> and <c>URI</c></summary>
    Iri,

    /// <summary><c>BNODE</c>, with zero or one argument.</summary>
    BNode,

    /// <summary><c>RAND</c></summary>
    Rand,

    /// <summary><c>ABS</c></summary>
    Abs,

    /// <summary><c>CEIL</c></summary>
    Ceil,

    /// <summary><c>FLOOR</c></summary>
    Floor,

    /// <summary><c>ROUND</c></summary>
    Round,

    /// <summary><c>CONCAT</c></summary>
    Concat,

    /// <summary><c>SUBSTR</c>, with two or three arguments.</summary>
    Substr,

    /// <summary><c>STRLEN</c></summary>
    StrLen,

    /// <summary><c>REPLACE</c>, with three or four arguments.</summary>
    Replace,

    /// <summary><c>UCASE</c></summary>
    UCase,

    /// <summary><c>LCASE</c></summary>
    LCase,

    /// <summary><c>ENCODE_FOR_URI</c></summary>
    EncodeForUri,

    /// <summary><c>CONTAINS</c></summary>
    Contains,

    /// <summary><c>STRSTARTS</c></summary>
    StrStarts,

    /// <summary><c>STRENDS</c></summary>
    StrEnds,

    /// <summary><c>STRBEFORE</c></summary>
    StrBefore,

    /// <summary><c>STRAFTER</c></summary>
    StrAfter,

    /// <summary><c>YEAR</c></summary>
    Year,

    /// <summary><c>MONTH</c></summary>
    Month,

    /// <summary><c>DAY</c></summary>
    Day,

    /// <summary><c>HOURS</c></summary>
    Hours,

    /// <summary><c>MINUTES</c></summary>
    Minutes,

    /// <summary><c>SECONDS</c></summary>
    Seconds,

    /// <summary><c>TIMEZONE</c></summary>
    Timezone,

    /// <summary><c>TZ</c></summary>
    Tz,

    /// <summary><c>NOW</c></summary>
    Now,

    /// <summary><c>UUID</c></summary>
    Uuid,

    /// <summary><c>STRUUID</c></summary>
    StrUuid,

    /// <summary><c>MD5</c></summary>
    Md5,

    /// <summary><c>SHA1</c></summary>
    Sha1,

    /// <summary><c>SHA256</c></summary>
    Sha256,

    /// <summary><c>SHA384</c></summary>
    Sha384,

    /// <summary><c>SHA512</c></summary>
    Sha512,

    /// <summary><c>COALESCE</c></summary>
    Coalesce,

    /// <summary><c>IF</c></summary>
    If,

    /// <summary><c>STRLANG</c></summary>
    StrLang,

    /// <summary><c>STRLANGDIR</c>. SPARQL 1.2.</summary>
    StrLangDir,

    /// <summary><c>STRDT</c></summary>
    StrDt,

    /// <summary><c>sameTerm</c></summary>
    SameTerm,

    /// <summary><c>isIRI</c> and <c>isURI</c></summary>
    IsIri,

    /// <summary><c>isBLANK</c></summary>
    IsBlank,

    /// <summary><c>isLITERAL</c></summary>
    IsLiteral,

    /// <summary><c>isNUMERIC</c></summary>
    IsNumeric,

    /// <summary><c>hasLANG</c>. SPARQL 1.2.</summary>
    HasLang,

    /// <summary><c>hasLANGDIR</c>. SPARQL 1.2.</summary>
    HasLangDir,

    /// <summary><c>REGEX</c>, with two or three arguments.</summary>
    Regex,

    /// <summary><c>isTRIPLE</c>. SPARQL 1.2.</summary>
    IsTriple,

    /// <summary><c>TRIPLE</c>, and <c>&lt;&lt;( s p o )&gt;&gt;</c> in an expression. SPARQL 1.2.</summary>
    Triple,

    /// <summary><c>SUBJECT</c>. SPARQL 1.2.</summary>
    Subject,

    /// <summary><c>PREDICATE</c>. SPARQL 1.2.</summary>
    Predicate,

    /// <summary><c>OBJECT</c>. SPARQL 1.2.</summary>
    Object,

    /// <summary><c>e IN (…)</c>: the tested expression first, then the list.</summary>
    In,

    /// <summary><c>e NOT IN (…)</c>: the tested expression first, then the list.</summary>
    NotIn,
}

/// <summary>The set functions of production <c>[147] Aggregate</c>, and a custom aggregate.</summary>
public enum AggregateFunction : byte
{
    /// <summary><c>COUNT</c></summary>
    Count,

    /// <summary><c>SUM</c></summary>
    Sum,

    /// <summary><c>MIN</c></summary>
    Min,

    /// <summary><c>MAX</c></summary>
    Max,

    /// <summary><c>AVG</c></summary>
    Avg,

    /// <summary><c>SAMPLE</c></summary>
    Sample,

    /// <summary><c>GROUP_CONCAT</c></summary>
    GroupConcat,

    /// <summary>A function named by IRI in aggregate position; <see cref="AggregateExpression.CustomFunction"/> names it.</summary>
    Custom,
}
