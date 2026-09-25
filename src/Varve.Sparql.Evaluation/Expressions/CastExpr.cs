// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Expressions;

/// <summary>
/// An XPath constructor function (SPARQL 1.1 §17.5): <c>xsd:integer(?x)</c> and
/// its relatives, by §17.5's table of allowed casts. A string is parsed after
/// XPath's whitespace collapsing; numerics convert by value; booleans are
/// <c>1</c> and <c>0</c>; an IRI casts only to <c>xsd:string</c>. Anything the
/// table does not allow is an error.
/// </summary>
internal sealed class CastExpr(XsdDatatype target, Expr argument) : Expr
{
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        Value value = argument.Eval(exec, row, graph);
        if (value.IsError)
        {
            return Value.Error;
        }

        RdfTerm term = Semantics.AsTerm(exec, value)!;
        if (term.Kind == RdfTermKind.Iri)
        {
            return target == XsdDatatype.String ? Value.Of(RdfTerm.Literal(term.Lexical)) : Value.Error;
        }

        if (term.Kind != RdfTermKind.Literal || !term.Language.IsEmpty)
        {
            return Value.Error;
        }

        XsdDatatype source = Terms.DatatypeOf(term);
        if (source == XsdDatatype.String)
        {
            return FromString(term.Lexical);
        }

        if (target == XsdDatatype.String)
        {
            return ToString(term, source);
        }

        if (XsdDatatypes.IsNumeric(source))
        {
            return XsdNumeric.TryParse(term.Lexical, source, out XsdNumeric number) ? FromNumeric(number) : Value.Error;
        }

        if (source == XsdDatatype.Boolean)
        {
            if (!XsdBoolean.TryParse(term.Lexical, out XsdBoolean flag))
            {
                return Value.Error;
            }

            return target == XsdDatatype.Boolean ? Value.Of(flag.Value) : FromNumeric(XsdNumeric.FromInteger(new XsdInteger(flag.Value ? 1 : 0)));
        }

        if (source is XsdDatatype.DateTime or XsdDatatype.DateTimeStamp && target is XsdDatatype.DateTime or XsdDatatype.DateTimeStamp)
        {
            return XsdDateTime.TryParse(term.Lexical, out XsdDateTime moment) && (target == XsdDatatype.DateTime || moment.HasTimezone)
                ? Value.Of(RdfTerm.Literal(term.Lexical, Terms.Datatype(target)))
                : Value.Error;
        }

        return Value.Error;
    }

    private Value FromString(ReadOnlySpan<byte> lexical)
    {
        if (target == XsdDatatype.String)
        {
            return Value.Of(RdfTerm.Literal(lexical));
        }

        // XPath casts from xs:string apply the target's whitespace facet, which
        // for every non-string type here is collapse: leading and trailing
        // white space goes.
        ReadOnlySpan<byte> collapsed = lexical.Trim(" \t\r\n"u8);
        if (XsdDatatypes.IsNumeric(target))
        {
            return XsdNumeric.TryParse(collapsed, target, out _)
                ? Value.Of(Terms.Typed(collapsed, target))
                : Value.Error;
        }

        switch (target)
        {
            case XsdDatatype.Boolean:
                return XsdBoolean.TryParse(collapsed, out XsdBoolean flag) ? Value.Of(flag.Value) : Value.Error;
            case XsdDatatype.DateTime:
            case XsdDatatype.DateTimeStamp:
                return XsdDateTime.TryParse(collapsed, out XsdDateTime moment) && (target == XsdDatatype.DateTime || moment.HasTimezone)
                    ? Value.Of(Terms.Typed(collapsed, target))
                    : Value.Error;
            default:
                return Valid(collapsed, target) ? Value.Of(Terms.Typed(collapsed, target)) : Value.Error;
        }
    }

    private static bool Valid(ReadOnlySpan<byte> lexical, XsdDatatype datatype) => datatype switch
    {
        XsdDatatype.Date => XsdDate.TryParse(lexical, out _),
        XsdDatatype.Time => XsdTime.TryParse(lexical, out _),
        XsdDatatype.GYear => XsdGYear.TryParse(lexical, out _),
        XsdDatatype.GYearMonth => XsdGYearMonth.TryParse(lexical, out _),
        XsdDatatype.GMonthDay => XsdGMonthDay.TryParse(lexical, out _),
        XsdDatatype.GDay => XsdGDay.TryParse(lexical, out _),
        XsdDatatype.GMonth => XsdGMonth.TryParse(lexical, out _),
        XsdDatatype.Duration => XsdDuration.TryParse(lexical, out _),
        XsdDatatype.DayTimeDuration => XsdDayTimeDuration.TryParse(lexical, out _),
        XsdDatatype.YearMonthDuration => XsdYearMonthDuration.TryParse(lexical, out _),
        _ => false,
    };

    private Value FromNumeric(XsdNumeric number)
    {
        if (target == XsdDatatype.Boolean)
        {
            double value = number.AsDouble.Value;
            return Value.Of(!double.IsNaN(value) && value != 0);
        }

        if (XsdDatatypes.IsIntegerType(target))
        {
            XsdInteger integer;
            switch (number.Kind)
            {
                case XsdNumericKind.Integer:
                    integer = number.AsInteger;
                    break;
                case XsdNumericKind.Decimal:
                    if (!number.AsDecimal.TryToInteger(out integer))
                    {
                        return Value.Error;
                    }

                    break;
                default:
                    double truncated = Math.Truncate(number.AsDouble.Value);
                    if (double.IsNaN(truncated) || truncated is < long.MinValue or >= 9.2233720368547758E18)
                    {
                        return Value.Error;
                    }

                    integer = new XsdInteger((long)truncated);
                    break;
            }

            (long minimum, long maximum) = XsdInteger.Range(target);
            return integer.Value < minimum || integer.Value > maximum
                ? Value.Error
                : target == XsdDatatype.Integer ? Value.Of(XsdNumeric.FromInteger(integer)) : Value.Of(Integer(integer, target));
        }

        switch (target)
        {
            case XsdDatatype.Decimal:
                if (number.Kind is XsdNumericKind.Integer or XsdNumericKind.Decimal)
                {
                    return Value.Of(XsdNumeric.FromDecimal(number.AsDecimal));
                }

                return DecimalOf(number.AsDouble.Value) is { } converted ? Value.Of(XsdNumeric.FromDecimal(converted)) : Value.Error;
            case XsdDatatype.Float:
                return Value.Of(XsdNumeric.FromFloat(new XsdFloat((float)number.AsDouble.Value)));
            case XsdDatatype.Double:
                return Value.Of(XsdNumeric.FromDouble(number.AsDouble));
            default:
                return Value.Error;
        }
    }

    private static RdfTerm Integer(XsdInteger value, XsdDatatype datatype)
    {
        Span<byte> buffer = stackalloc byte[24];
        value.TryFormat(buffer, out int written);
        return RdfTerm.Literal(buffer[..written], Terms.Datatype(datatype));
    }

    /// <summary>A double as a decimal, exactly as far as eighteen fractional digits reach; null for NaN and the infinities.</summary>
    private static XsdDecimal? DecimalOf(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 1e20)
        {
            return null;
        }

        string text = ((decimal)value).ToString("0.##################", CultureInfo.InvariantCulture);
        return XsdDecimal.TryParse(Encoding.ASCII.GetBytes(text), out XsdDecimal result) ? result : null;
    }

    /// <summary>
    /// A typed literal cast to <c>xsd:string</c>: XPath's casting of the value
    /// to a string, which writes a whole decimal without a fraction and a
    /// double between 10⁻⁶ and 10⁶ in plain notation (F&amp;O §19.1.2.2).
    /// </summary>
    private static Value ToString(RdfTerm term, XsdDatatype source)
    {
        if (XsdDatatypes.IsNumeric(source) && XsdNumeric.TryParse(term.Lexical, source, out XsdNumeric number))
        {
            string text = number.Kind switch
            {
                XsdNumericKind.Integer => number.AsInteger.Value.ToString(CultureInfo.InvariantCulture),
                XsdNumericKind.Decimal => PlainDecimal(number.AsDecimal),
                XsdNumericKind.Float => Floating(number.AsFloat.Value.ToString("R", CultureInfo.InvariantCulture), number.AsFloat.Value),
                _ => Floating(number.AsDouble.Value.ToString("R", CultureInfo.InvariantCulture), number.AsDouble.Value),
            };
            return Value.Of(RdfTerm.Literal(Encoding.ASCII.GetBytes(text)));
        }

        if (source == XsdDatatype.Boolean && XsdBoolean.TryParse(term.Lexical, out XsdBoolean flag))
        {
            return Value.Of(RdfTerm.Literal(flag.Value ? "true"u8 : "false"u8));
        }

        return Value.Of(RdfTerm.Literal(term.Lexical));
    }

    private static string PlainDecimal(XsdDecimal value)
    {
        string canonical = value.ToString();
        return canonical.EndsWith(".0", StringComparison.Ordinal) ? canonical[..^2] : canonical;
    }

    private static string Floating(string shortest, double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "INF" : "-INF";
        }

        // The shortest round-trip digits, placed as XPath places them.
        bool negative = shortest.StartsWith('-');
        string body = negative ? shortest[1..] : shortest;
        int e = body.IndexOfAny(['E', 'e']);
        int exponent = e < 0 ? 0 : int.Parse(body[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = e < 0 ? body : body[..e];
        int point = mantissa.IndexOf('.');
        string digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        int position = (point < 0 ? mantissa.Length : point) + exponent;
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0')
        {
            lead++;
            position--;
        }

        digits = digits[lead..].TrimEnd('0');
        if (digits.Length == 0)
        {
            return negative ? "-0" : "0";
        }

        string sign = negative ? "-" : string.Empty;
        double magnitude = Math.Abs(value);
        if (magnitude >= 1e-6 && magnitude < 1e6)
        {
            if (position <= 0)
            {
                return sign + "0." + new string('0', -position) + digits;
            }

            return position >= digits.Length
                ? sign + digits + new string('0', position - digits.Length)
                : sign + digits[..position] + "." + digits[position..];
        }

        return sign + digits[0] + "." + (digits.Length > 1 ? digits[1..] : "0") + "E" + (position - 1).ToString(CultureInfo.InvariantCulture);
    }
}
