// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Writing;

/// <summary>Expressions and updates.</summary>
internal ref partial struct Writer
{
    // --- expressions -------------------------------------------------------

    private void WriteExpression(Expression expression)
    {
        switch (expression)
        {
            case VariableExpression variable:
                WriteVariable(variable.Variable);
                break;
            case ConstantExpression constant:
                WriteTerm(constant.Term);
                break;
            case UnaryExpression unary:
                _out.Write((byte)'(');
                _out.Write(unary.Operator switch
                {
                    UnaryOperator.Not => "! "u8,
                    UnaryOperator.Plus => "+ "u8,
                    _ => "- "u8,
                });
                WriteExpression(unary.Operand);
                _out.Write((byte)')');
                break;
            case BinaryExpression binary:
                _out.Write((byte)'(');
                WriteExpression(binary.Left);
                _out.Write(binary.Operator switch
                {
                    BinaryOperator.Or => " || "u8,
                    BinaryOperator.And => " && "u8,
                    BinaryOperator.Equal => " = "u8,
                    BinaryOperator.NotEqual => " != "u8,
                    BinaryOperator.Less => " < "u8,
                    BinaryOperator.Greater => " > "u8,
                    BinaryOperator.LessOrEqual => " <= "u8,
                    BinaryOperator.GreaterOrEqual => " >= "u8,
                    BinaryOperator.Add => " + "u8,
                    BinaryOperator.Subtract => " - "u8,
                    BinaryOperator.Multiply => " * "u8,
                    _ => " / "u8,
                });
                WriteExpression(binary.Right);
                _out.Write((byte)')');
                break;
            case FunctionCall call:
                WriteFunctionCall(call);
                break;
            case CustomFunctionCall custom:
                WriteIri(custom.Function);
                WriteArguments(custom.Arguments);
                break;
            case ExistsExpression exists:
                _out.Write(exists.Negated ? "NOT EXISTS { "u8 : "EXISTS { "u8);
                WriteGroupBody(exists.Pattern);
                _out.Write(" }"u8);
                break;
            case AggregateExpression aggregate:
                WriteAggregate(aggregate);
                break;
            default:
                throw new InvalidOperationException("Unknown expression " + expression.GetType().Name + ".");
        }
    }

    private void WriteFunctionCall(FunctionCall call)
    {
        if (call.Function is BuiltInFunction.In or BuiltInFunction.NotIn)
        {
            _out.Write((byte)'(');
            WriteExpression(call.Arguments[0]);
            _out.Write(call.Function == BuiltInFunction.In ? " IN ("u8 : " NOT IN ("u8);

            for (int i = 1; i < call.Arguments.Count; i++)
            {
                if (i > 1)
                {
                    _out.Write(", "u8);
                }

                WriteExpression(call.Arguments[i]);
            }

            _out.Write("))"u8);
            return;
        }

        _out.Write(Name(call.Function));
        WriteArguments(call.Arguments);
    }

    private void WriteArguments(AlgebraList<Expression> arguments)
    {
        _out.Write((byte)'(');

        for (int i = 0; i < arguments.Count; i++)
        {
            if (i > 0)
            {
                _out.Write(", "u8);
            }

            WriteExpression(arguments[i]);
        }

        _out.Write((byte)')');
    }

    private void WriteAggregate(AggregateExpression aggregate)
    {
        if (aggregate.Function == AggregateFunction.Custom)
        {
            WriteIri(aggregate.CustomFunction!);
        }
        else
        {
            _out.Write(aggregate.Function switch
            {
                AggregateFunction.Count => "COUNT"u8,
                AggregateFunction.Sum => "SUM"u8,
                AggregateFunction.Min => "MIN"u8,
                AggregateFunction.Max => "MAX"u8,
                AggregateFunction.Avg => "AVG"u8,
                AggregateFunction.Sample => "SAMPLE"u8,
                _ => "GROUP_CONCAT"u8,
            });
        }

        _out.Write((byte)'(');

        if (aggregate.Distinct)
        {
            _out.Write("DISTINCT "u8);
        }

        if (aggregate.Argument is null)
        {
            _out.Write((byte)'*');
        }
        else
        {
            WriteExpression(aggregate.Argument);
        }

        if (aggregate.Separator is string separator)
        {
            _out.Write(" ; SEPARATOR = "u8);
            WriteString(System.Text.Encoding.UTF8.GetBytes(separator));
        }

        _out.Write((byte)')');
    }

    private static ReadOnlySpan<byte> Name(BuiltInFunction function) => function switch
    {
        BuiltInFunction.Str => "STR"u8,
        BuiltInFunction.Lang => "LANG"u8,
        BuiltInFunction.LangMatches => "LANGMATCHES"u8,
        BuiltInFunction.LangDir => "LANGDIR"u8,
        BuiltInFunction.Datatype => "DATATYPE"u8,
        BuiltInFunction.Bound => "BOUND"u8,
        BuiltInFunction.Iri => "IRI"u8,
        BuiltInFunction.BNode => "BNODE"u8,
        BuiltInFunction.Rand => "RAND"u8,
        BuiltInFunction.Abs => "ABS"u8,
        BuiltInFunction.Ceil => "CEIL"u8,
        BuiltInFunction.Floor => "FLOOR"u8,
        BuiltInFunction.Round => "ROUND"u8,
        BuiltInFunction.Concat => "CONCAT"u8,
        BuiltInFunction.Substr => "SUBSTR"u8,
        BuiltInFunction.StrLen => "STRLEN"u8,
        BuiltInFunction.Replace => "REPLACE"u8,
        BuiltInFunction.UCase => "UCASE"u8,
        BuiltInFunction.LCase => "LCASE"u8,
        BuiltInFunction.EncodeForUri => "ENCODE_FOR_URI"u8,
        BuiltInFunction.Contains => "CONTAINS"u8,
        BuiltInFunction.StrStarts => "STRSTARTS"u8,
        BuiltInFunction.StrEnds => "STRENDS"u8,
        BuiltInFunction.StrBefore => "STRBEFORE"u8,
        BuiltInFunction.StrAfter => "STRAFTER"u8,
        BuiltInFunction.Year => "YEAR"u8,
        BuiltInFunction.Month => "MONTH"u8,
        BuiltInFunction.Day => "DAY"u8,
        BuiltInFunction.Hours => "HOURS"u8,
        BuiltInFunction.Minutes => "MINUTES"u8,
        BuiltInFunction.Seconds => "SECONDS"u8,
        BuiltInFunction.Timezone => "TIMEZONE"u8,
        BuiltInFunction.Tz => "TZ"u8,
        BuiltInFunction.Now => "NOW"u8,
        BuiltInFunction.Uuid => "UUID"u8,
        BuiltInFunction.StrUuid => "STRUUID"u8,
        BuiltInFunction.Md5 => "MD5"u8,
        BuiltInFunction.Sha1 => "SHA1"u8,
        BuiltInFunction.Sha256 => "SHA256"u8,
        BuiltInFunction.Sha384 => "SHA384"u8,
        BuiltInFunction.Sha512 => "SHA512"u8,
        BuiltInFunction.Coalesce => "COALESCE"u8,
        BuiltInFunction.If => "IF"u8,
        BuiltInFunction.StrLang => "STRLANG"u8,
        BuiltInFunction.StrLangDir => "STRLANGDIR"u8,
        BuiltInFunction.StrDt => "STRDT"u8,
        BuiltInFunction.SameTerm => "sameTerm"u8,
        BuiltInFunction.IsIri => "isIRI"u8,
        BuiltInFunction.IsBlank => "isBLANK"u8,
        BuiltInFunction.IsLiteral => "isLITERAL"u8,
        BuiltInFunction.IsNumeric => "isNUMERIC"u8,
        BuiltInFunction.HasLang => "hasLANG"u8,
        BuiltInFunction.HasLangDir => "hasLANGDIR"u8,
        BuiltInFunction.Regex => "REGEX"u8,
        BuiltInFunction.IsTriple => "isTRIPLE"u8,
        BuiltInFunction.Triple => "TRIPLE"u8,
        BuiltInFunction.Subject => "SUBJECT"u8,
        BuiltInFunction.Predicate => "PREDICATE"u8,
        BuiltInFunction.Object => "OBJECT"u8,
        _ => throw new InvalidOperationException("Unknown function " + function + "."),
    };

    // --- updates -----------------------------------------------------------

    internal void Write(Update update)
    {
        WritePrologue(update.Prologue);

        for (int i = 0; i < update.Operations.Count; i++)
        {
            if (i > 0)
            {
                _out.Write(" ;\n"u8);
            }

            WriteOperation(update.Operations[i]);
        }

        _out.Write((byte)'\n');
    }

    private void WriteOperation(UpdateOperation operation)
    {
        switch (operation)
        {
            case InsertData insertData:
                _out.Write("INSERT DATA {"u8);
                WriteQuads(insertData.Quads);
                _out.Write((byte)'}');
                break;
            case DeleteData deleteData:
                _out.Write("DELETE DATA {"u8);
                WriteQuads(deleteData.Quads);
                _out.Write((byte)'}');
                break;
            case DeleteWhere deleteWhere:
                _out.Write("DELETE WHERE {"u8);
                WriteQuads(deleteWhere.Quads);
                _out.Write((byte)'}');
                break;
            case Modify modify:
                if (modify.With is not null)
                {
                    _out.Write("WITH "u8);
                    WriteIri(modify.With);
                    _out.Write((byte)'\n');
                }

                if (!modify.Delete.IsEmpty || modify.Insert.IsEmpty)
                {
                    _out.Write("DELETE {"u8);
                    WriteQuads(modify.Delete);
                    _out.Write("}\n"u8);
                }

                if (!modify.Insert.IsEmpty)
                {
                    _out.Write("INSERT {"u8);
                    WriteQuads(modify.Insert);
                    _out.Write("}\n"u8);
                }

                if (modify.Using is DatasetSpec @using)
                {
                    foreach (RdfTerm graph in @using.DefaultGraphs)
                    {
                        _out.Write("USING "u8);
                        WriteIri(graph);
                        _out.Write((byte)'\n');
                    }

                    foreach (RdfTerm graph in @using.NamedGraphs)
                    {
                        _out.Write("USING NAMED "u8);
                        WriteIri(graph);
                        _out.Write((byte)'\n');
                    }
                }

                _out.Write("WHERE {"u8);
                WriteGroupBody(modify.Where);
                _out.Write((byte)'}');
                break;
            case Load load:
                _out.Write(load.Silent ? "LOAD SILENT "u8 : "LOAD "u8);
                WriteIri(load.Source);

                if (load.Graph is not null)
                {
                    _out.Write(" INTO GRAPH "u8);
                    WriteIri(load.Graph);
                }

                break;
            case Clear clear:
                _out.Write(clear.Silent ? "CLEAR SILENT "u8 : "CLEAR "u8);
                WriteTarget(clear.Target);
                break;
            case Drop drop:
                _out.Write(drop.Silent ? "DROP SILENT "u8 : "DROP "u8);
                WriteTarget(drop.Target);
                break;
            case Create create:
                _out.Write(create.Silent ? "CREATE SILENT GRAPH "u8 : "CREATE GRAPH "u8);
                WriteIri(create.Graph);
                break;
            case Add add:
                _out.Write(add.Silent ? "ADD SILENT "u8 : "ADD "u8);
                WriteGraphOrDefault(add.From);
                _out.Write(" TO "u8);
                WriteGraphOrDefault(add.To);
                break;
            case Move move:
                _out.Write(move.Silent ? "MOVE SILENT "u8 : "MOVE "u8);
                WriteGraphOrDefault(move.From);
                _out.Write(" TO "u8);
                WriteGraphOrDefault(move.To);
                break;
            case Copy copy:
                _out.Write(copy.Silent ? "COPY SILENT "u8 : "COPY "u8);
                WriteGraphOrDefault(copy.From);
                _out.Write(" TO "u8);
                WriteGraphOrDefault(copy.To);
                break;
            default:
                throw new InvalidOperationException("Unknown operation " + operation.GetType().Name + ".");
        }
    }

    private void WriteTarget(GraphTarget target)
    {
        switch (target.Kind)
        {
            case GraphTargetKind.Default:
                _out.Write("DEFAULT"u8);
                break;
            case GraphTargetKind.Named:
                _out.Write("NAMED"u8);
                break;
            case GraphTargetKind.All:
                _out.Write("ALL"u8);
                break;
            default:
                _out.Write("GRAPH "u8);
                WriteIri(target.Graph!);
                break;
        }
    }

    private void WriteGraphOrDefault(GraphOrDefault graph)
    {
        if (graph.IsDefault)
        {
            _out.Write("DEFAULT"u8);
        }
        else
        {
            _out.Write("GRAPH "u8);
            WriteIri(graph.Graph!);
        }
    }

    /// <summary>Quads in order; consecutive quads of one named graph share a GRAPH block.</summary>
    private void WriteQuads(AlgebraList<QuadPattern> quads)
    {
        PatternTerm? open = null;

        foreach (QuadPattern quad in quads)
        {
            if (!ReferenceEquals(open, quad.Graph) && (open is null || quad.Graph is null || !open.Equals(quad.Graph)))
            {
                if (open is not null)
                {
                    _out.Write(" }"u8);
                }

                open = quad.Graph;

                if (open is not null)
                {
                    _out.Write(" GRAPH "u8);
                    WritePatternTerm(open);
                    _out.Write(" {"u8);
                }
            }

            _out.Write((byte)' ');
            WritePatternTerm(quad.Subject);
            _out.Write((byte)' ');
            WritePatternTerm(quad.Predicate);
            _out.Write((byte)' ');
            WritePatternTerm(quad.Object);
            _out.Write(" ."u8);
        }

        if (open is not null)
        {
            _out.Write(" }"u8);
        }

        _out.Write((byte)' ');
    }
}

/// <summary>Whether a lexical form is exactly a numeric terminal, so that the short form re-parses to the same term.</summary>
internal static class Lexical
{
    internal static bool IsInteger(ReadOnlySpan<byte> text)
    {
        int at = Sign(text);
        return at < text.Length && AllDigits(text[at..]);
    }

    internal static bool IsDecimal(ReadOnlySpan<byte> text)
    {
        int at = Sign(text);
        int dot = text[at..].IndexOf((byte)'.');

        if (dot < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> before = text.Slice(at, dot);
        ReadOnlySpan<byte> after = text[(at + dot + 1)..];
        return !after.IsEmpty && AllDigits(after) && (before.IsEmpty || AllDigits(before));
    }

    internal static bool IsDouble(ReadOnlySpan<byte> text)
    {
        int at = Sign(text);
        int e = text[at..].IndexOfAny((byte)'e', (byte)'E');

        if (e < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> mantissa = text.Slice(at, e);
        ReadOnlySpan<byte> exponent = text[(at + e + 1)..];

        if (!exponent.IsEmpty && exponent[0] is (byte)'+' or (byte)'-')
        {
            exponent = exponent[1..];
        }

        if (exponent.IsEmpty || !AllDigits(exponent))
        {
            return false;
        }

        int dot = mantissa.IndexOf((byte)'.');

        if (dot < 0)
        {
            return !mantissa.IsEmpty && AllDigits(mantissa);
        }

        ReadOnlySpan<byte> before = mantissa[..dot];
        ReadOnlySpan<byte> after = mantissa[(dot + 1)..];

        if (before.IsEmpty)
        {
            return !after.IsEmpty && AllDigits(after);
        }

        return AllDigits(before) && (after.IsEmpty || AllDigits(after));
    }

    private static int Sign(ReadOnlySpan<byte> text) => !text.IsEmpty && text[0] is (byte)'+' or (byte)'-' ? 1 : 0;

    private static bool AllDigits(ReadOnlySpan<byte> text)
    {
        foreach (byte b in text)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }
}
