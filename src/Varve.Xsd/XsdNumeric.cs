// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>Which of the four numeric types an <see cref="XsdNumeric"/> holds.</summary>
/// <remarks>Ordered as XPath promotes: integer → decimal → float → double.</remarks>
public enum XsdNumericKind
{
    /// <summary><c>xsd:integer</c> or a type derived from it.</summary>
    Integer = 0,

    /// <summary><c>xsd:decimal</c>.</summary>
    Decimal = 1,

    /// <summary><c>xsd:float</c>.</summary>
    Float = 2,

    /// <summary><c>xsd:double</c>.</summary>
    Double = 3,
}

/// <summary>
/// A numeric value of any of the four numeric types, with XPath's type
/// promotion applied by every binary operation (SPARQL 1.1 §17.3, XPath
/// Functions and Operators §4.2 and §4.3).
/// </summary>
/// <remarks>
/// <para>
/// The evaluator dispatches on datatype, and once it has decided two operands
/// are numeric this is the type it works in: a binary operation promotes the
/// narrower operand to the wider kind and computes there, integer division
/// yields a decimal, and failure is the <c>Try</c> form's false rather than an
/// exception. <c>NaN</c> is unordered, and <see cref="Compare"/> says so.
/// </para>
/// </remarks>
public readonly struct XsdNumeric : IEquatable<XsdNumeric>
{
    private readonly XsdDecimal _decimal;
    private readonly double _double;
    private readonly long _integer;

    private XsdNumeric(XsdNumericKind kind, long integer, XsdDecimal @decimal, double @double)
    {
        Kind = kind;
        _integer = integer;
        _decimal = @decimal;
        _double = @double;
    }

    /// <summary>Which type this holds.</summary>
    public XsdNumericKind Kind { get; }

    /// <summary>An integer.</summary>
    public static XsdNumeric FromInteger(XsdInteger value) => new(XsdNumericKind.Integer, value.Value, default, 0);

    /// <summary>A decimal.</summary>
    public static XsdNumeric FromDecimal(XsdDecimal value) => new(XsdNumericKind.Decimal, 0, value, 0);

    /// <summary>A float.</summary>
    public static XsdNumeric FromFloat(XsdFloat value) => new(XsdNumericKind.Float, 0, default, value.Value);

    /// <summary>A double.</summary>
    public static XsdNumeric FromDouble(XsdDouble value) => new(XsdNumericKind.Double, 0, default, value.Value);

    /// <summary>The value as an integer; only meaningful when <see cref="Kind"/> says so.</summary>
    public XsdInteger AsInteger => new(_integer);

    /// <summary>The value as a decimal; meaningful for the integer and decimal kinds.</summary>
    public XsdDecimal AsDecimal => Kind == XsdNumericKind.Integer ? XsdDecimal.FromInt64(_integer) : _decimal;

    /// <summary>The value as a float; meaningful for every kind, by promotion.</summary>
    public XsdFloat AsFloat => new((float)AsDouble.Value);

    /// <summary>The value as a double; meaningful for every kind, by promotion.</summary>
    public XsdDouble AsDouble => Kind switch
    {
        XsdNumericKind.Integer => new XsdDouble(_integer),
        XsdNumericKind.Decimal => new XsdDouble(_decimal.ToDouble()),
        _ => new XsdDouble(_double),
    };

    /// <summary>
    /// Parses a lexical form of a numeric datatype into the kind that
    /// datatype promotes to. False when the datatype is not numeric or the
    /// form is not a value of it.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, XsdDatatype datatype, out XsdNumeric value)
    {
        value = default;

        if (XsdDatatypes.IsIntegerType(datatype))
        {
            if (!XsdInteger.TryParse(utf8, datatype, out XsdInteger integer))
            {
                return false;
            }

            value = FromInteger(integer);
            return true;
        }

        switch (datatype)
        {
            case XsdDatatype.Decimal:
                if (!XsdDecimal.TryParse(utf8, out XsdDecimal d))
                {
                    return false;
                }

                value = FromDecimal(d);
                return true;
            case XsdDatatype.Float:
                if (!XsdFloat.TryParse(utf8, out XsdFloat f))
                {
                    return false;
                }

                value = FromFloat(f);
                return true;
            case XsdDatatype.Double:
                if (!XsdDouble.TryParse(utf8, out XsdDouble x))
                {
                    return false;
                }

                value = FromDouble(x);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Writes the canonical form of the held kind.</summary>
    public bool TryFormat(Span<byte> destination, out int written) => Kind switch
    {
        XsdNumericKind.Integer => AsInteger.TryFormat(destination, out written),
        XsdNumericKind.Decimal => _decimal.TryFormat(destination, out written),
        XsdNumericKind.Float => new XsdFloat((float)_double).TryFormat(destination, out written),
        _ => new XsdDouble(_double).TryFormat(destination, out written),
    };

    /// <summary>The canonical form of the held kind.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>The datatype the held kind maps back to.</summary>
    public XsdDatatype Datatype => Kind switch
    {
        XsdNumericKind.Integer => XsdDatatype.Integer,
        XsdNumericKind.Decimal => XsdDatatype.Decimal,
        XsdNumericKind.Float => XsdDatatype.Float,
        _ => XsdDatatype.Double,
    };

    // --- arithmetic ---------------------------------------------------------

    /// <summary><c>op:numeric-add</c> after promotion; false on overflow.</summary>
    public static bool TryAdd(XsdNumeric left, XsdNumeric right, out XsdNumeric result) =>
        Binary(left, right, Operation.Add, out result);

    /// <summary><c>op:numeric-subtract</c> after promotion; false on overflow.</summary>
    public static bool TrySubtract(XsdNumeric left, XsdNumeric right, out XsdNumeric result) =>
        Binary(left, right, Operation.Subtract, out result);

    /// <summary><c>op:numeric-multiply</c> after promotion; false on overflow.</summary>
    public static bool TryMultiply(XsdNumeric left, XsdNumeric right, out XsdNumeric result) =>
        Binary(left, right, Operation.Multiply, out result);

    /// <summary>
    /// <c>op:numeric-divide</c> after promotion; two integers give a decimal
    /// (§17.3); false on a zero divisor for integers and decimals, and on
    /// overflow. Floats and doubles divide as IEEE does.
    /// </summary>
    public static bool TryDivide(XsdNumeric left, XsdNumeric right, out XsdNumeric result) =>
        Binary(left, right, Operation.Divide, out result);

    /// <summary><c>op:numeric-unary-minus</c>; false on overflow.</summary>
    public static bool TryNegate(XsdNumeric value, out XsdNumeric result)
    {
        switch (value.Kind)
        {
            case XsdNumericKind.Integer:
                {
                    bool ok = XsdInteger.TryNegate(value.AsInteger, out XsdInteger negated);
                    result = FromInteger(negated);
                    return ok;
                }

            case XsdNumericKind.Decimal:
                {
                    bool ok = XsdDecimal.TryNegate(value._decimal, out XsdDecimal negated);
                    result = FromDecimal(negated);
                    return ok;
                }

            default:
                result = new XsdNumeric(value.Kind, 0, default, -value._double);
                return true;
        }
    }

    /// <summary><c>fn:abs</c>; false on overflow.</summary>
    public static bool TryAbs(XsdNumeric value, out XsdNumeric result)
    {
        switch (value.Kind)
        {
            case XsdNumericKind.Integer:
                {
                    bool ok = XsdInteger.TryAbs(value.AsInteger, out XsdInteger absolute);
                    result = FromInteger(absolute);
                    return ok;
                }

            case XsdNumericKind.Decimal:
                {
                    bool ok = XsdDecimal.TryAbs(value._decimal, out XsdDecimal absolute);
                    result = FromDecimal(absolute);
                    return ok;
                }

            default:
                result = new XsdNumeric(value.Kind, 0, default, Math.Abs(value._double));
                return true;
        }
    }

    /// <summary><c>fn:floor</c>, in the held kind.</summary>
    public XsdNumeric Floor() => Kind switch
    {
        XsdNumericKind.Integer => this,
        XsdNumericKind.Decimal => FromDecimal(_decimal.Floor()),
        _ => new XsdNumeric(Kind, 0, default, Math.Floor(_double)),
    };

    /// <summary><c>fn:ceiling</c>, in the held kind.</summary>
    public XsdNumeric Ceiling() => Kind switch
    {
        XsdNumericKind.Integer => this,
        XsdNumericKind.Decimal => FromDecimal(_decimal.Ceiling()),
        _ => new XsdNumeric(Kind, 0, default, Math.Ceiling(_double)),
    };

    /// <summary>
    /// <c>fn:round</c>, in the held kind: the nearest integer, ties toward
    /// positive infinity. False when the rounded decimal does not fit.
    /// </summary>
    public bool TryRound(out XsdNumeric result)
    {
        switch (Kind)
        {
            case XsdNumericKind.Integer:
                result = this;
                return true;
            case XsdNumericKind.Decimal:
                {
                    bool ok = _decimal.TryRound(out XsdDecimal rounded);
                    result = FromDecimal(rounded);
                    return ok;
                }

            default:
                result = new XsdNumeric(Kind, 0, default, Math.Floor(_double + 0.5));
                return true;
        }
    }

    private enum Operation
    {
        Add,
        Subtract,
        Multiply,
        Divide,
    }

    private static bool Binary(XsdNumeric left, XsdNumeric right, Operation operation, out XsdNumeric result)
    {
        XsdNumericKind kind = left.Kind > right.Kind ? left.Kind : right.Kind;

        if (kind == XsdNumericKind.Integer && operation == Operation.Divide)
        {
            kind = XsdNumericKind.Decimal;
        }

        switch (kind)
        {
            case XsdNumericKind.Integer:
                {
                    XsdInteger a = left.AsInteger;
                    XsdInteger b = right.AsInteger;
                    bool ok = operation switch
                    {
                        Operation.Add => XsdInteger.TryAdd(a, b, out XsdInteger r) & Store(r, out result),
                        Operation.Subtract => XsdInteger.TrySubtract(a, b, out XsdInteger r) & Store(r, out result),
                        _ => XsdInteger.TryMultiply(a, b, out XsdInteger r) & Store(r, out result),
                    };
                    return ok;
                }

            case XsdNumericKind.Decimal:
                {
                    XsdDecimal a = left.AsDecimal;
                    XsdDecimal b = right.AsDecimal;
                    bool ok = operation switch
                    {
                        Operation.Add => XsdDecimal.TryAdd(a, b, out XsdDecimal r) & Store(r, out result),
                        Operation.Subtract => XsdDecimal.TrySubtract(a, b, out XsdDecimal r) & Store(r, out result),
                        Operation.Multiply => XsdDecimal.TryMultiply(a, b, out XsdDecimal r) & Store(r, out result),
                        _ => XsdDecimal.TryDivide(a, b, out XsdDecimal r) & Store(r, out result),
                    };
                    return ok;
                }

            case XsdNumericKind.Float:
                {
                    float a = left.AsFloat.Value;
                    float b = right.AsFloat.Value;
                    float r = operation switch
                    {
                        Operation.Add => a + b,
                        Operation.Subtract => a - b,
                        Operation.Multiply => a * b,
                        _ => a / b,
                    };
                    result = new XsdNumeric(XsdNumericKind.Float, 0, default, r);
                    return true;
                }

            default:
                {
                    double a = left.AsDouble.Value;
                    double b = right.AsDouble.Value;
                    double r = operation switch
                    {
                        Operation.Add => a + b,
                        Operation.Subtract => a - b,
                        Operation.Multiply => a * b,
                        _ => a / b,
                    };
                    result = new XsdNumeric(XsdNumericKind.Double, 0, default, r);
                    return true;
                }
        }
    }

    private static bool Store(XsdInteger value, out XsdNumeric result)
    {
        result = FromInteger(value);
        return true;
    }

    private static bool Store(XsdDecimal value, out XsdNumeric result)
    {
        result = FromDecimal(value);
        return true;
    }

    // --- order --------------------------------------------------------------

    /// <summary>
    /// <c>op:numeric-less-than</c> and its relatives after promotion:
    /// <see cref="PartialOrdering.Indeterminate"/> only when a <c>NaN</c> is
    /// involved.
    /// </summary>
    public static PartialOrdering Compare(XsdNumeric left, XsdNumeric right)
    {
        XsdNumericKind kind = left.Kind > right.Kind ? left.Kind : right.Kind;

        return kind switch
        {
            XsdNumericKind.Integer => Ordering(left._integer.CompareTo(right._integer)),
            XsdNumericKind.Decimal => Ordering(left.AsDecimal.CompareTo(right.AsDecimal)),
            XsdNumericKind.Float => XsdFloat.Compare(left.AsFloat, right.AsFloat),
            _ => XsdDouble.Compare(left.AsDouble, right.AsDouble),
        };
    }

    private static PartialOrdering Ordering(int comparison) =>
        comparison < 0 ? PartialOrdering.Less : comparison > 0 ? PartialOrdering.Greater : PartialOrdering.Equal;

    /// <summary><c>op:numeric-equal</c> after promotion; <c>NaN</c> equals nothing.</summary>
    public bool Equals(XsdNumeric other) => Compare(this, other) == PartialOrdering.Equal;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdNumeric other && Equals(other);

    /// <summary>
    /// A hash consistent with promoted equality: every kind hashes through
    /// its double approximation, so <c>1</c>, <c>1.0</c> and <c>1.0E0</c> agree.
    /// </summary>
    public override int GetHashCode() => AsDouble.GetHashCode();

    /// <summary>Promoted equality.</summary>
    public static bool operator ==(XsdNumeric left, XsdNumeric right) => left.Equals(right);

    /// <summary>Promoted inequality.</summary>
    public static bool operator !=(XsdNumeric left, XsdNumeric right) => !left.Equals(right);
}
