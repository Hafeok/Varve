// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.JsonLd.Json;

/// <summary>Writes a <see cref="JsonTree"/> subtree with <see cref="Utf8JsonWriter"/> (ADR 0112).</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class JsonTreeWriter
{
    /// <summary>The writer options for a document; the relaxed encoder keeps non-ASCII text as it is, which is JSON.</summary>
    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonTypesAdmittedOnTheHotPath), Scope = ExceptionScope.HotPath)]
    internal static JsonWriterOptions Options(bool indent) => new()
    {
        Indented = indent,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true,
    };

    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonTypesAdmittedOnTheHotPath), Scope = ExceptionScope.HotPath)]
    internal static void Write(IBufferWriter<byte> output, JsonTree tree, int node, bool indent)
    {
        using Utf8JsonWriter writer = new(output, Options(indent));
        Write(writer, tree, node);
        writer.Flush();
    }

    internal static void Write(Utf8JsonWriter writer, JsonTree tree, int node)
    {
        switch (tree.Kind(node))
        {
            case JsonKind.Object:
                writer.WriteStartObject();

                for (int member = tree.First(node); member >= 0; member = tree.Next(member))
                {
                    writer.WritePropertyName(tree.NameBytes(member));
                    Write(writer, tree, tree.ValueOf(member));
                }

                writer.WriteEndObject();
                break;
            case JsonKind.Array:
                writer.WriteStartArray();

                for (int item = tree.First(node); item >= 0; item = tree.Next(item))
                {
                    Write(writer, tree, item);
                }

                writer.WriteEndArray();
                break;
            case JsonKind.String:
                writer.WriteStringValue(tree.Bytes(node));
                break;
            case JsonKind.Number:
                writer.WriteRawValue(tree.Bytes(node), skipInputValidation: true);
                break;
            case JsonKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
