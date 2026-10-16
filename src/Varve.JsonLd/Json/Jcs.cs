// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;

namespace Varve.JsonLd.Json;

/// <summary>
/// RFC 8785, the JSON Canonicalization Scheme, over this package's tree: the
/// walk is here, and the three rules are <see cref="CanonicalJson"/>'s (ADR
/// 0147, ADR 0123 amended 2026-10-15). The lexical form of an
/// <c>rdf:JSON</c> literal (JSON-LD 1.1 API §8.6, step 3; ADR 0123,
/// <c>JsonLiteralsAreCanonical</c>). Members sorted by UTF-16 code units, no
/// whitespace, strings escaped only where JSON requires it, numbers in
/// ECMAScript's <c>Number::toString</c> form.
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.JsonLiteralsAreCanonical), Scope = ExceptionScope.HotPath)]
internal static class Jcs
{
    /// <summary>Writes the canonical form of <paramref name="node"/> into the tree's text; returns its range.</summary>
    internal static TextRange Canonicalise(JsonTree tree, int node)
    {
        // The canonical bytes go through a pooled buffer, because sorting an
        // object's members reorders nodes whose text would otherwise be
        // written where it is read from.
        ArrayBufferWriter<byte> buffer = new(256);
        Write(buffer, tree, node);
        return tree.AddText(buffer.WrittenSpan);
    }

    private static void Write(ArrayBufferWriter<byte> output, JsonTree tree, int node)
    {
        switch (tree.Kind(node))
        {
            case JsonKind.Object:
                tree.SortMembers(node);
                Put(output, (byte)'{');
                bool first = true;

                for (int member = tree.First(node); member >= 0; member = tree.Next(member))
                {
                    if (!first)
                    {
                        Put(output, (byte)',');
                    }

                    first = false;
                    WriteString(output, tree.NameBytes(member));
                    Put(output, (byte)':');
                    Write(output, tree, tree.ValueOf(member));
                }

                Put(output, (byte)'}');
                break;
            case JsonKind.Array:
                Put(output, (byte)'[');
                bool firstItem = true;

                for (int item = tree.First(node); item >= 0; item = tree.Next(item))
                {
                    if (!firstItem)
                    {
                        Put(output, (byte)',');
                    }

                    firstItem = false;
                    Write(output, tree, item);
                }

                Put(output, (byte)']');
                break;
            case JsonKind.String:
                WriteString(output, tree.Bytes(node));
                break;
            case JsonKind.Number:
                WriteNumber(output, tree.Bytes(node));
                break;
            case JsonKind.True:
                Put(output, "true"u8);
                break;
            case JsonKind.False:
                Put(output, "false"u8);
                break;
            default:
                Put(output, "null"u8);
                break;
        }
    }

    private static void WriteString(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> text) =>
        CanonicalJson.WriteString(output, text);

    /// <summary>
    /// RFC 8785 §3.2.2.3: the number as ECMAScript's <c>Number::toString</c>
    /// renders the IEEE double it denotes (<see cref="CanonicalJson.FormatNumber"/>).
    /// A JSON number too large for a double is written as it was read, since
    /// RFC 8785 has no form for it.
    /// </summary>
    internal static void WriteNumber(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> raw)
    {
        double value = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);

        if (!double.IsFinite(value))
        {
            Put(output, raw);
            return;
        }

        CanonicalJson.WriteNumber(output, value);
    }

    private static void Put(ArrayBufferWriter<byte> output, byte b)
    {
        output.GetSpan(1)[0] = b;
        output.Advance(1);
    }

    private static void Put(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }
}
