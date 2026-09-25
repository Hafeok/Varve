// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Varve.Iri;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Expressions;

/// <summary>
/// A call to one of SPARQL's built-in functions: the library of §17.4 and the
/// SPARQL 1.2 draft's additions (<c>sparql-evaluation.md</c> §7.6), each as
/// its section defines it. The functional forms — <c>BOUND</c>, <c>IF</c>,
/// <c>COALESCE</c>, <c>IN</c>, <c>NOT IN</c> — evaluate their arguments
/// themselves; every other function is an error when an argument is.
/// </summary>
internal sealed class FunctionExpr : Expr
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly BuiltInFunction _function;
    private readonly Expr[] _arguments;
    private readonly int _boundSlot;

    internal FunctionExpr(BuiltInFunction function, Expr[] arguments, int boundSlot = -1)
    {
        _function = function;
        _arguments = arguments;
        _boundSlot = boundSlot;
    }

    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        switch (_function)
        {
            case BuiltInFunction.Bound:
                return Value.Of(_boundSlot >= 0 && Rows.IsBound(row, _boundSlot));
            case BuiltInFunction.If:
                {
                    bool? condition = Semantics.Ebv(exec, _arguments[0].Eval(exec, row, graph));
                    return condition is null ? Value.Error : _arguments[condition.Value ? 1 : 2].Eval(exec, row, graph);
                }

            case BuiltInFunction.Coalesce:
                foreach (Expr argument in _arguments)
                {
                    Value value = argument.Eval(exec, row, graph);
                    if (!value.IsError)
                    {
                        return value;
                    }
                }

                return Value.Error;
            case BuiltInFunction.In:
            case BuiltInFunction.NotIn:
                return In(exec, row, graph);
            case BuiltInFunction.Rand:
                return Rand(exec);
            case BuiltInFunction.Now:
                return Value.Of(DateTimeTerm(exec.Now()));
            case BuiltInFunction.Uuid:
                return Value.Of(RdfTerm.Iri(Utf8.GetBytes("urn:uuid:" + Uuid(exec, "UUID()"))));
            case BuiltInFunction.StrUuid:
                return Value.Of(RdfTerm.Literal(Utf8.GetBytes(Uuid(exec, "STRUUID()"))));
            case BuiltInFunction.BNode when _arguments.Length == 0:
                return Value.Of(exec.InternLocal(exec.MintBlankNode()));
        }

        FourValues inline = default;
        Span<Value> values = _arguments.Length <= 4 ? inline[.._arguments.Length] : new Value[_arguments.Length];
        for (int i = 0; i < _arguments.Length; i++)
        {
            values[i] = _arguments[i].Eval(exec, row, graph);
            if (values[i].IsError)
            {
                return Value.Error;
            }
        }

        return _function switch
        {
            BuiltInFunction.SameTerm => Value.Of(Semantics.SameTerm(exec, values[0], values[1])),
            BuiltInFunction.IsIri => Value.Of(Kind(exec, values[0]) == RdfTermKind.Iri),
            BuiltInFunction.IsBlank => Value.Of(Kind(exec, values[0]) == RdfTermKind.BlankNode),
            BuiltInFunction.IsLiteral => Value.Of(Kind(exec, values[0]) == RdfTermKind.Literal),
            BuiltInFunction.IsTriple => Value.Of(Kind(exec, values[0]) == RdfTermKind.TripleTerm),
            BuiltInFunction.IsNumeric => Value.Of(Semantics.TryNumeric(exec, values[0], out _)),
            BuiltInFunction.BNode => BNode(exec, row, values[0]),
            _ => Call(exec, values),
        };
    }

    private Value Call(Exec exec, ReadOnlySpan<Value> values)
    {
        // Every remaining function reads its arguments as terms; numbers first
        // where that is all it needs, so a numeric function never materialises.
        switch (_function)
        {
            case BuiltInFunction.Abs:
            case BuiltInFunction.Ceil:
            case BuiltInFunction.Floor:
            case BuiltInFunction.Round:
                return Numeric(exec, values[0]);
            case BuiltInFunction.Concat:
                return Concat(exec, values);
        }

        RdfTerm a = Semantics.AsTerm(exec, values[0])!;
        switch (_function)
        {
            case BuiltInFunction.Str:
                return a.Kind is RdfTermKind.Iri or RdfTermKind.Literal ? Value.Of(RdfTerm.Literal(a.Lexical)) : Value.Error;
            case BuiltInFunction.Lang:
                return a.Kind == RdfTermKind.Literal ? Value.Of(RdfTerm.Literal(a.Language)) : Value.Error;
            case BuiltInFunction.LangDir:
                return a.Kind == RdfTermKind.Literal ? Value.Of(RdfTerm.Literal(Direction(a.Direction))) : Value.Error;
            case BuiltInFunction.HasLang:
                return Value.Of(Terms.IsLanguageTagged(a));
            case BuiltInFunction.HasLangDir:
                return Value.Of(a.Kind == RdfTermKind.Literal && a.Direction != TextDirection.None);
            case BuiltInFunction.Datatype:
                return a.Kind != RdfTermKind.Literal ? Value.Error
                    : Value.Of(a.Datatype ?? (a.Language.IsEmpty ? Terms.Datatype(XsdDatatype.String)
                        : a.Direction == TextDirection.None ? Terms.RdfLangString : Terms.RdfDirLangString));
            case BuiltInFunction.Iri:
                return Iri(exec, a);
            case BuiltInFunction.StrDt:
                return IsPlainString(a) && Semantics.AsTerm(exec, values[1]) is { Kind: RdfTermKind.Iri } datatype
                    && !datatype.Lexical.SequenceEqual(RdfVocabulary.RdfLangString) && !datatype.Lexical.SequenceEqual(RdfVocabulary.RdfDirLangString)
                    ? Value.Of(RdfTerm.Literal(a.Lexical, datatype))
                    : Value.Error;
            case BuiltInFunction.StrLang:
                {
                    RdfTerm tag = Semantics.AsTerm(exec, values[1])!;
                    return IsPlainString(a) && IsPlainString(tag) && LanguageTag.IsWellFormed(tag.Lexical)
                        ? Value.Of(RdfTerm.Literal(a.Lexical, tag.Lexical))
                        : Value.Error;
                }

            case BuiltInFunction.StrLangDir:
                {
                    RdfTerm tag = Semantics.AsTerm(exec, values[1])!;
                    RdfTerm dir = Semantics.AsTerm(exec, values[2])!;
                    TextDirection direction = dir.Lexical.SequenceEqual("ltr"u8) ? TextDirection.LeftToRight
                        : dir.Lexical.SequenceEqual("rtl"u8) ? TextDirection.RightToLeft
                        : TextDirection.None;
                    return IsPlainString(a) && IsPlainString(tag) && IsPlainString(dir) && direction != TextDirection.None
                        && LanguageTag.IsWellFormed(tag.Lexical)
                        ? Value.Of(RdfTerm.Literal(a.Lexical, tag.Lexical, direction))
                        : Value.Error;
                }

            case BuiltInFunction.StrLen:
                return Terms.IsStringLiteral(a) ? Value.Of(XsdNumeric.FromInteger(new XsdInteger(CodePoints(a.Lexical)))) : Value.Error;
            case BuiltInFunction.Substr:
                return Substr(exec, a, values);
            case BuiltInFunction.UCase:
            case BuiltInFunction.LCase:
                if (!Terms.IsStringLiteral(a))
                {
                    return Value.Error;
                }

                string text = Utf8.GetString(a.Lexical);
                text = _function == BuiltInFunction.UCase ? text.ToUpperInvariant() : text.ToLowerInvariant();
                return Value.Of(Terms.StringLike(Utf8.GetBytes(text), a));
            case BuiltInFunction.StrStarts:
            case BuiltInFunction.StrEnds:
            case BuiltInFunction.Contains:
            case BuiltInFunction.StrBefore:
            case BuiltInFunction.StrAfter:
                return TwoStrings(a, Semantics.AsTerm(exec, values[1])!);
            case BuiltInFunction.EncodeForUri:
                return Terms.IsStringLiteral(a) ? Value.Of(RdfTerm.Literal(EncodeForUri(a.Lexical))) : Value.Error;
            case BuiltInFunction.LangMatches:
                {
                    RdfTerm range = Semantics.AsTerm(exec, values[1])!;
                    return IsPlainString(a) && IsPlainString(range) ? Value.Of(LangMatches(a.Lexical, range.Lexical)) : Value.Error;
                }

            case BuiltInFunction.Regex:
                return RegexMatch(exec, a, values);
            case BuiltInFunction.Replace:
                return Replace(exec, a, values);
            case BuiltInFunction.Year:
            case BuiltInFunction.Month:
            case BuiltInFunction.Day:
            case BuiltInFunction.Hours:
            case BuiltInFunction.Minutes:
            case BuiltInFunction.Seconds:
            case BuiltInFunction.Timezone:
            case BuiltInFunction.Tz:
                return DatePart(a);
            case BuiltInFunction.Md5:
            case BuiltInFunction.Sha1:
            case BuiltInFunction.Sha256:
            case BuiltInFunction.Sha384:
            case BuiltInFunction.Sha512:
                return IsPlainString(a) ? Value.Of(RdfTerm.Literal(Hash(a.Lexical))) : Value.Error;
            case BuiltInFunction.Triple:
                {
                    RdfTerm p = Semantics.AsTerm(exec, values[1])!;
                    RdfTerm o = Semantics.AsTerm(exec, values[2])!;
                    return a.Kind is RdfTermKind.Iri or RdfTermKind.BlankNode && p.Kind == RdfTermKind.Iri
                        ? Value.Of(RdfTerm.TripleTerm(a, p, o))
                        : Value.Error;
                }

            case BuiltInFunction.Subject:
                return a.Kind == RdfTermKind.TripleTerm ? Value.Of(a.Subject!) : Value.Error;
            case BuiltInFunction.Predicate:
                return a.Kind == RdfTermKind.TripleTerm ? Value.Of(a.Predicate!) : Value.Error;
            case BuiltInFunction.Object:
                return a.Kind == RdfTermKind.TripleTerm ? Value.Of(a.Object!) : Value.Error;
            default:
                return Value.Error;
        }
    }

    // ------------------------------------------------------- functional forms

    private Value In(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        Value tested = _arguments[0].Eval(exec, row, graph);
        if (tested.IsError)
        {
            return Value.Error;
        }

        bool error = false;
        for (int i = 1; i < _arguments.Length; i++)
        {
            bool? equal = Semantics.Equal(exec, tested, _arguments[i].Eval(exec, row, graph));
            if (equal == true)
            {
                return Value.Of(_function == BuiltInFunction.In);
            }

            error |= equal is null;
        }

        return error ? Value.Error : Value.Of(_function == BuiltInFunction.NotIn);
    }

    // ---------------------------------------------------------- RDF terms

    private static RdfTermKind? Kind(Exec exec, in Value value) => value.Kind switch
    {
        ValueKind.Numeric or ValueKind.Boolean => RdfTermKind.Literal,
        _ => Semantics.AsTerm(exec, value)?.Kind,
    };

    /// <summary>A simple literal or an <c>xsd:string</c>: no language tag.</summary>
    private static bool IsPlainString(RdfTerm term) => Terms.IsSimple(term);

    private static ReadOnlySpan<byte> Direction(TextDirection direction) => direction switch
    {
        TextDirection.LeftToRight => "ltr"u8,
        TextDirection.RightToLeft => "rtl"u8,
        _ => default,
    };

    private static Value Iri(Exec exec, RdfTerm argument)
    {
        if (argument.Kind == RdfTermKind.Iri)
        {
            return Value.Of(argument);
        }

        if (!IsPlainString(argument))
        {
            return Value.Error;
        }

        ReadOnlySpan<byte> reference = argument.Lexical;
        if (exec.BaseIri is { } baseIri && !IriRef.IsAbsolute(reference))
        {
            int length = IriRef.ResolveLength(baseIri.Lexical, reference);
            byte[] resolved = new byte[Math.Max(length, 1)];
            if (!IriRef.TryResolve(baseIri.Lexical, reference, resolved, out int written))
            {
                return Value.Error;
            }

            return Value.Of(RdfTerm.Iri(resolved.AsSpan(0, written)));
        }

        return IriRef.TryValidate(reference, out _, out _) && IriRef.IsAbsolute(reference)
            ? Value.Of(RdfTerm.Iri(reference))
            : Value.Error;
    }

    /// <summary>
    /// <c>BNODE(s)</c> (§17.4.2.9): the same node for the same string within
    /// one solution, a different one in another. A solution is recognised by
    /// its array, and a <c>BIND</c> that copies it passes the memo on
    /// (<see cref="Exec.SameSolution"/>), so the <c>SELECT</c> expressions of
    /// one solution share it (the suite's bnode01).
    /// </summary>
    private static Value BNode(Exec exec, ulong[] row, in Value argument)
    {
        RdfTerm label = Semantics.AsTerm(exec, argument)!;
        if (!IsPlainString(label))
        {
            return Value.Error;
        }

        return Value.Of(exec.BlankNodeFor(row, label));
    }

    // -------------------------------------------------------------- numerics

    private Value Numeric(Exec exec, in Value argument)
    {
        if (!Semantics.TryNumeric(exec, argument, out XsdNumeric number))
        {
            return Value.Error;
        }

        switch (_function)
        {
            case BuiltInFunction.Abs:
                return XsdNumeric.TryAbs(number, out XsdNumeric abs) ? Value.Of(abs) : Value.Error;
            case BuiltInFunction.Ceil:
                return Value.Of(number.Ceiling());
            case BuiltInFunction.Floor:
                return Value.Of(number.Floor());
            default:
                return number.TryRound(out XsdNumeric rounded) ? Value.Of(rounded) : Value.Error;
        }
    }

    private static Value Rand(Exec exec)
    {
        Span<byte> bytes = stackalloc byte[8];
        exec.RandomBytes(bytes, "RAND()");
        ulong bits = BinaryPrimitives.ReadUInt64LittleEndian(bytes) >> 11;
        return Value.Of(XsdNumeric.FromDouble(new XsdDouble(bits * (1.0 / (1UL << 53)))));
    }

    private static string Uuid(Exec exec, string function)
    {
        Span<byte> bytes = stackalloc byte[16];
        exec.RandomBytes(bytes, function);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40); // version 4, RFC 4122 §4.4
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // the RFC 4122 variant
        string hex = Convert.ToHexStringLower(bytes);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    // --------------------------------------------------------------- strings

    private static int CodePoints(ReadOnlySpan<byte> utf8)
    {
        int count = 0;
        foreach (byte b in utf8)
        {
            if ((b & 0xC0) != 0x80)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The byte offset of the <paramref name="index"/>-th code point (from 0), or the length.</summary>
    private static int ByteOffset(ReadOnlySpan<byte> utf8, long index)
    {
        if (index <= 0)
        {
            return 0;
        }

        long seen = 0;
        for (int i = 0; i < utf8.Length; i++)
        {
            if ((utf8[i] & 0xC0) != 0x80)
            {
                if (seen == index)
                {
                    return i;
                }

                seen++;
            }
        }

        return utf8.Length;
    }

    /// <summary><c>fn:substring</c>: positions from 1, start and length rounded as <c>fn:round</c> does.</summary>
    private static Value Substr(Exec exec, RdfTerm source, ReadOnlySpan<Value> values)
    {
        if (!Terms.IsStringLiteral(source) || !Semantics.TryNumeric(exec, values[1], out XsdNumeric start))
        {
            return Value.Error;
        }

        double first = Math.Floor(start.AsDouble.Value + 0.5);
        double last = double.PositiveInfinity;
        if (values.Length > 2)
        {
            if (!Semantics.TryNumeric(exec, values[2], out XsdNumeric length))
            {
                return Value.Error;
            }

            last = first + Math.Floor(length.AsDouble.Value + 0.5);
        }

        if (double.IsNaN(first) || double.IsNaN(last))
        {
            return Value.Of(Terms.StringLike(default, source));
        }

        int count = CodePoints(source.Lexical);
        long from = (long)Math.Clamp(first, 1, count + 1L);
        long to = (long)Math.Clamp(last, 1, count + 1L);
        if (to <= from)
        {
            return Value.Of(Terms.StringLike(default, source));
        }

        ReadOnlySpan<byte> lexical = source.Lexical;
        int begin = ByteOffset(lexical, from - 1);
        int end = ByteOffset(lexical, to - 1);
        return Value.Of(Terms.StringLike(lexical[begin..end], source));
    }

    /// <summary>§17.4.3.1.2: both plain, or the same language tag, or a tagged first and a plain second.</summary>
    private static bool Compatible(RdfTerm first, RdfTerm second) =>
        Terms.IsStringLiteral(first) && Terms.IsStringLiteral(second)
        && (second.Language.IsEmpty || first.Language.SequenceEqual(second.Language));

    private Value TwoStrings(RdfTerm first, RdfTerm second)
    {
        if (!Compatible(first, second))
        {
            return Value.Error;
        }

        ReadOnlySpan<byte> a = first.Lexical;
        ReadOnlySpan<byte> b = second.Lexical;
        switch (_function)
        {
            case BuiltInFunction.StrStarts:
                return Value.Of(a.StartsWith(b));
            case BuiltInFunction.StrEnds:
                return Value.Of(a.EndsWith(b));
            case BuiltInFunction.Contains:
                return Value.Of(a.IndexOf(b) >= 0);
        }

        int at = a.IndexOf(b);
        if (at < 0)
        {
            return Value.Of(Terms.EmptyString);
        }

        return Value.Of(_function == BuiltInFunction.StrBefore
            ? Terms.StringLike(a[..at], first)
            : Terms.StringLike(a[(at + b.Length)..], first));
    }

    private static byte[] EncodeForUri(ReadOnlySpan<byte> utf8)
    {
        List<byte> encoded = new(utf8.Length);
        foreach (byte b in utf8)
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
            {
                encoded.Add(b);
            }
            else
            {
                encoded.Add((byte)'%');
                encoded.Add((byte)"0123456789ABCDEF"[b >> 4]);
                encoded.Add((byte)"0123456789ABCDEF"[b & 0xF]);
            }
        }

        return [.. encoded];
    }

    /// <summary>
    /// §17.4.3.12: the concatenated lexical forms; <c>xsd:string</c> if every
    /// argument is one, the shared tag if every argument has it, simple otherwise.
    /// </summary>
    private static Value Concat(Exec exec, ReadOnlySpan<Value> values)
    {
        List<byte> text = [];
        byte[]? language = null;
        TextDirection direction = TextDirection.None;
        bool shared = true;
        for (int i = 0; i < values.Length; i++)
        {
            RdfTerm term = Semantics.AsTerm(exec, values[i])!;
            if (!Terms.IsStringLiteral(term))
            {
                return Value.Error;
            }

            text.AddRange(term.Lexical);
            if (i == 0)
            {
                language = term.Language.ToArray();
                direction = term.Direction;
            }
            else if (!term.Language.SequenceEqual(language) || term.Direction != direction)
            {
                shared = false;
            }
        }

        byte[] lexical = [.. text];
        return shared && language is { Length: > 0 }
            ? Value.Of(RdfTerm.Literal(lexical, language, direction))
            : Value.Of(RdfTerm.Literal(lexical));
    }

    /// <summary>RFC 4647 §3.3.1, basic filtering; <c>*</c> matches any non-empty tag.</summary>
    private static bool LangMatches(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> range)
    {
        if (range.SequenceEqual("*"u8))
        {
            return !tag.IsEmpty;
        }

        if (tag.Length < range.Length)
        {
            return false;
        }

        for (int i = 0; i < range.Length; i++)
        {
            if (AsciiLower(tag[i]) != AsciiLower(range[i]))
            {
                return false;
            }
        }

        return tag.Length == range.Length || tag[range.Length] == (byte)'-';
    }

    private static byte AsciiLower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;

    // ---------------------------------------------------------------- regex

    private static Value RegexMatch(Exec exec, RdfTerm text, ReadOnlySpan<Value> values)
    {
        Regex? regex = Pattern(exec, values);
        if (regex is null || !Terms.IsStringLiteral(text))
        {
            return Value.Error;
        }

        try
        {
            return Value.Of(regex.IsMatch(Utf8.GetString(text.Lexical)));
        }
        catch (RegexMatchTimeoutException)
        {
            return Value.Error;
        }
    }

    private static Value Replace(Exec exec, RdfTerm text, ReadOnlySpan<Value> values)
    {
        RdfTerm replacement = Semantics.AsTerm(exec, values[2])!;
        Value[] patternAndFlags = values.Length > 3 ? [values[0], values[1], values[3]] : [values[0], values[1]];
        Regex? regex = Pattern(exec, patternAndFlags);
        if (regex is null || !Terms.IsStringLiteral(text) || !IsPlainString(replacement)
            || XPathRegex.TranslateReplacement(Utf8.GetString(replacement.Lexical), regex) is not { } dotnet)
        {
            return Value.Error;
        }

        try
        {
            // F&O §5.6.3: FORX0003, a pattern that matches the empty string.
            if (regex.IsMatch(string.Empty))
            {
                return Value.Error;
            }

            string result = regex.Replace(Utf8.GetString(text.Lexical), dotnet);
            return Value.Of(Terms.StringLike(Utf8.GetBytes(result), text));
        }
        catch (RegexMatchTimeoutException)
        {
            return Value.Error;
        }
    }

    /// <summary>The compiled pattern of arguments 2 and 3, cached per execution; null is an error.</summary>
    private static Regex? Pattern(Exec exec, ReadOnlySpan<Value> values)
    {
        RdfTerm pattern = Semantics.AsTerm(exec, values[1])!;
        RdfTerm? flags = values.Length > 2 ? Semantics.AsTerm(exec, values[2]) : null;
        if (!IsPlainString(pattern) || (flags is not null && !IsPlainString(flags)))
        {
            return null;
        }

        TimeSpan timeout = exec.Options.RegexTimeout;
        return exec.Regex(
            Utf8.GetString(pattern.Lexical),
            flags is null ? string.Empty : Utf8.GetString(flags.Lexical),
            (p, f) => XPathRegex.Compile(p, f, timeout));
    }

    // ---------------------------------------------------------------- dates

    private Value DatePart(RdfTerm argument)
    {
        if (argument.Kind != RdfTermKind.Literal || argument.Datatype is null)
        {
            return Value.Error;
        }

        XsdDatatype datatype = XsdDatatypes.FromIri(argument.DatatypeIri);
        if (datatype is not (XsdDatatype.DateTime or XsdDatatype.DateTimeStamp)
            || !XsdDateTime.TryParse(argument.Lexical, out XsdDateTime value))
        {
            return Value.Error;
        }

        switch (_function)
        {
            case BuiltInFunction.Year:
                return Integer(value.Year);
            case BuiltInFunction.Month:
                return Integer(value.Month);
            case BuiltInFunction.Day:
                return Integer(value.Day);
            case BuiltInFunction.Hours:
                return Integer(value.Hour);
            case BuiltInFunction.Minutes:
                return Integer(value.Minute);
            case BuiltInFunction.Seconds:
                return Value.Of(XsdNumeric.FromDecimal(value.Second));
            case BuiltInFunction.Timezone:
                if (!value.HasTimezone)
                {
                    return Value.Error;
                }

                return Value.Of(Format(value.TimezoneDuration, XsdDatatype.DayTimeDuration));
            default:
                return Value.Of(RdfTerm.Literal(Tz(value)));
        }
    }

    private static Value Integer(long value) => Value.Of(XsdNumeric.FromInteger(new XsdInteger(value)));

    private static byte[] Tz(XsdDateTime value)
    {
        if (!value.HasTimezone)
        {
            return [];
        }

        int offset = value.TimezoneOffset;
        if (offset == 0)
        {
            return "Z"u8.ToArray();
        }

        int magnitude = Math.Abs(offset);
        return Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{(offset < 0 ? '-' : '+')}{magnitude / 60:D2}:{magnitude % 60:D2}"));
    }

    internal static RdfTerm DateTimeTerm(XsdDateTime value)
    {
        Span<byte> buffer = stackalloc byte[64];
        return value.TryFormat(buffer, out int written)
            ? RdfTerm.Literal(buffer[..written], Terms.Datatype(XsdDatatype.DateTime))
            : throw new InvalidOperationException("A dateTime did not fit its buffer.");
    }

    private static RdfTerm Format(XsdDayTimeDuration value, XsdDatatype datatype)
    {
        Span<byte> buffer = stackalloc byte[64];
        return value.TryFormat(buffer, out int written)
            ? RdfTerm.Literal(buffer[..written], Terms.Datatype(datatype))
            : throw new InvalidOperationException("A duration did not fit its buffer.");
    }

    // --------------------------------------------------------------- hashes

    private byte[] Hash(ReadOnlySpan<byte> utf8)
    {
        byte[] digest = _function switch
        {
            BuiltInFunction.Md5 => Md5.Hash(utf8),
#pragma warning disable CA5350 // ADR 0048: SPARQL 1.1 §17.4.6.2 defines SHA1() as a function of the language; it hashes a query's string, not a secret.
            BuiltInFunction.Sha1 => SHA1.HashData(utf8),
#pragma warning restore CA5350
            BuiltInFunction.Sha256 => SHA256.HashData(utf8),
            BuiltInFunction.Sha384 => SHA384.HashData(utf8),
            _ => SHA512.HashData(utf8),
        };
        return Encoding.ASCII.GetBytes(Convert.ToHexStringLower(digest));
    }
}

/// <summary>Four values on the stack: the arguments of every built-in but CONCAT and COALESCE, without an array.</summary>
[System.Runtime.CompilerServices.InlineArray(4)]
internal struct FourValues
{
    private Value _element;
}
