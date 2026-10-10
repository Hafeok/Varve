// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.JsonLd.Model;
using Varve.JsonLd.Processing;

namespace Varve.JsonLd.Json;

/// <summary>
/// Reads a JSON document into a <see cref="JsonTree"/> with
/// <see cref="Utf8JsonReader"/>, which does the grammar, the escapes and the
/// chunk boundaries of a <see cref="ReadOnlySequence{T}"/> (ADR 0123).
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class JsonTreeReader
{
    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonTypesAdmittedOnTheHotPath), Scope = ExceptionScope.HotPath)]
    private static JsonReaderOptions Options() => new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 512,
    };

    /// <summary>Reads one document; the root node is returned.</summary>
    internal static int Read(in ReadOnlySequence<byte> utf8, JsonTree tree, JsonLdErrorCode onMalformed)
    {
        Utf8JsonReader reader = new(utf8, isFinalBlock: true, new JsonReaderState(Options()));

        try
        {
            if (!reader.Read())
            {
                throw new JsonLdException(onMalformed, "The document is empty.");
            }

            int root = Value(ref reader, tree, onMalformed);

            if (reader.Read())
            {
                throw new JsonLdException(onMalformed, "The document continues after its value.");
            }

            return root;
        }
        catch (JsonException exception)
        {
            throw new JsonLdException(onMalformed, "The document is not JSON: " + exception.Message);
        }
    }

    internal static int Read(ReadOnlySpan<byte> utf8, JsonTree tree, JsonLdErrorCode onMalformed)
    {
        Utf8JsonReader reader = new(utf8, isFinalBlock: true, new JsonReaderState(Options()));

        try
        {
            if (!reader.Read())
            {
                throw new JsonLdException(onMalformed, "The document is empty.");
            }

            int root = Value(ref reader, tree, onMalformed);

            if (reader.Read())
            {
                throw new JsonLdException(onMalformed, "The document continues after its value.");
            }

            return root;
        }
        catch (JsonException exception)
        {
            throw new JsonLdException(onMalformed, "The document is not JSON: " + exception.Message);
        }
    }

    /// <summary>The value at the reader's current token, read whole.</summary>
    private static int Value(ref Utf8JsonReader reader, JsonTree tree, JsonLdErrorCode onMalformed)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
            {
                int obj = tree.AddObject();

                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    int name = CopyName(ref reader, tree);

                    if (!reader.Read())
                    {
                        throw new JsonLdException(onMalformed, "The document ended inside an object.");
                    }

                    int value = Value(ref reader, tree, onMalformed);

                    // A repeated member is not valid JSON-LD (the grammar is
                    // over objects with unique keys); the last one stands, as
                    // JSON.parse would have it.
                    tree.SetMember(obj, name, value);
                }

                return obj;
            }

            case JsonTokenType.StartArray:
            {
                int array = tree.AddArray();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    tree.Append(array, Value(ref reader, tree, onMalformed));
                }

                return array;
            }

            case JsonTokenType.String:
                return tree.AddString(CopyString(ref reader, tree));
            case JsonTokenType.Number:
                return reader.HasValueSequence
                    ? tree.AddNumber(CopySequence(ref reader, tree))
                    : tree.AddNumber(reader.ValueSpan);
            case JsonTokenType.True:
                return tree.AddBoolean(true);
            case JsonTokenType.False:
                return tree.AddBoolean(false);
            case JsonTokenType.Null:
                return tree.AddNull();
            default:
                throw new JsonLdException(onMalformed, "Unexpected JSON token " + reader.TokenType.ToString() + ".");
        }
    }

    private static TextRange CopyString(ref Utf8JsonReader reader, JsonTree tree)
    {
        int length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
        Span<byte> destination = tree.ReserveText(length);
        int written = reader.CopyString(destination);
        return tree.CommitText(written);
    }

    private static int CopyName(ref Utf8JsonReader reader, JsonTree tree)
    {
        int length = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;

        if (!reader.ValueIsEscaped && !reader.HasValueSequence)
        {
            return tree.Names.Intern(reader.ValueSpan);
        }

        Span<byte> destination = tree.ReserveText(length);
        int written = reader.CopyString(destination);
        return tree.Names.Intern(destination[..written]);
    }

    private static TextRange CopySequence(ref Utf8JsonReader reader, JsonTree tree)
    {
        int length = checked((int)reader.ValueSequence.Length);
        Span<byte> destination = tree.ReserveText(length);
        reader.ValueSequence.CopyTo(destination);
        return tree.CommitText(length);
    }
}
