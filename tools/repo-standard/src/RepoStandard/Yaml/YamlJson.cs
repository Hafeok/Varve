// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace RepoStandard.Yaml;

/// <summary>
/// YAML to <see cref="JsonNode"/> and back, on YamlDotNet's parser and emitter
/// only.
/// </summary>
/// <remarks>
/// <para>
/// YamlDotNet's object serializer is reflection-based, and its static context
/// needs a typed model. A declaration is not typed all the way down: a ruleset
/// is carried in the JSON shape GitHub's ruleset export uses, and that shape
/// belongs to GitHub. So YAML is read as a tree, exactly as JSON would be, and
/// the tree is what the rest of the tool works on. The parser and the emitter
/// use no reflection, which is what keeps the Native AOT build clean (ADR 0039).
/// </para>
/// <para>
/// Scalars resolve by the YAML 1.2 core schema, narrowed: <c>null</c>,
/// <c>~</c> and an empty plain scalar are null; <c>true</c> and <c>false</c> are
/// booleans; decimal integers and decimal floats are numbers; everything else,
/// and anything quoted, is a string. Octal, hexadecimal, <c>.inf</c> and
/// <c>.nan</c> are not numbers here — none of GitHub's settings take one, and a
/// colour such as <c>0e8a16</c> must stay the string it looks like.
/// </para>
/// </remarks>
internal static partial class YamlJson
{
    private const string StringTag = "tag:yaml.org,2002:str";

    private static readonly FrozenSet<string> Yaml11Booleans = new[]
    {
        "y", "n", "yes", "no", "on", "off",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses one YAML document. An empty stream is null.</summary>
    /// <param name="text">The YAML.</param>
    /// <param name="source">Where it came from, for messages.</param>
    public static JsonNode? Parse(string text, string source)
    {
        try
        {
            Parser parser = new(new StringReader(text));
            parser.Consume<StreamStart>();

            if (parser.Accept<StreamEnd>(out _))
            {
                return null;
            }

            parser.Consume<DocumentStart>();
            Dictionary<string, JsonNode?> anchors = new(StringComparer.Ordinal);
            JsonNode? root = ReadNode(parser, anchors, source);
            parser.Consume<DocumentEnd>();

            if (!parser.Accept<StreamEnd>(out _))
            {
                throw new DeclarationException($"{source}: more than one YAML document; a declaration is one document.");
            }

            return root;
        }
        catch (YamlException exception)
        {
            throw new DeclarationException(
                $"{source}:{exception.Start.Line}:{exception.Start.Column}: {exception.Message}");
        }
    }

    /// <summary>Writes a tree as a block-style YAML document.</summary>
    /// <param name="node">The tree.</param>
    public static string Write(JsonNode? node)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
        Emitter emitter = new(writer, new EmitterSettings(bestIndent: 2, bestWidth: int.MaxValue, isCanonical: false, maxSimpleKeyLength: 1024, newLine: "\n", indentSequences: false));

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart(null, null, isImplicit: true));
        WriteNode(emitter, node);
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());

        return writer.ToString();
    }

    private static JsonNode? ReadNode(IParser parser, Dictionary<string, JsonNode?> anchors, string source)
    {
        if (Take(parser, out AnchorAlias? alias))
        {
            if (!anchors.TryGetValue(alias.Value.Value, out JsonNode? target))
            {
                throw new DeclarationException($"{source}:{alias.Start.Line}: alias *{alias.Value.Value} names no anchor.");
            }

            return target?.DeepClone();
        }

        JsonNode? node;
        string? anchor;

        if (Take(parser, out Scalar? scalar))
        {
            anchor = scalar.Anchor.IsEmpty ? null : scalar.Anchor.Value;
            node = ResolveScalar(scalar, source);
        }
        else if (Take(parser, out MappingStart? mappingStart))
        {
            anchor = mappingStart.Anchor.IsEmpty ? null : mappingStart.Anchor.Value;
            RejectTag(mappingStart.Tag, mappingStart.Start, source);
            JsonObject mapping = [];

            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (!Take(parser, out Scalar? key))
                {
                    throw new DeclarationException($"{source}:{parser.Current?.Start.Line}: a mapping key must be a plain value.");
                }

                if (mapping.ContainsKey(key.Value))
                {
                    throw new DeclarationException($"{source}:{key.Start.Line}: duplicate key '{key.Value}'.");
                }

                mapping[key.Value] = ReadNode(parser, anchors, source);
            }

            node = mapping;
        }
        else if (Take(parser, out SequenceStart? sequenceStart))
        {
            anchor = sequenceStart.Anchor.IsEmpty ? null : sequenceStart.Anchor.Value;
            RejectTag(sequenceStart.Tag, sequenceStart.Start, source);
            JsonArray sequence = [];

            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                sequence.Add(ReadNode(parser, anchors, source));
            }

            node = sequence;
        }
        else
        {
            throw new DeclarationException($"{source}:{parser.Current?.Start.Line}: unexpected YAML content.");
        }

        if (anchor is not null)
        {
            anchors[anchor] = node?.DeepClone();
        }

        return node;
    }

    /// <summary>
    /// Consumes the current event when it is a <typeparamref name="T"/>.
    /// YamlDotNet's own TryConsume carries no nullability annotation.
    /// </summary>
    private static bool Take<T>(IParser parser, [NotNullWhen(true)] out T? taken)
        where T : ParsingEvent
    {
        if (parser.Current is T current)
        {
            parser.MoveNext();
            taken = current;
            return true;
        }

        taken = null;
        return false;
    }

    private static void RejectTag(TagName tag, Mark start, string source)
    {
        if (!tag.IsEmpty && !tag.IsNonSpecific)
        {
            throw new DeclarationException($"{source}:{start.Line}: YAML tags are not supported ('{tag.Value}').");
        }
    }

    private static JsonNode? ResolveScalar(Scalar scalar, string source)
    {
        if (!scalar.Tag.IsEmpty && !scalar.Tag.IsNonSpecific)
        {
            if (string.Equals(scalar.Tag.Value, StringTag, StringComparison.Ordinal))
            {
                return JsonValue.Create(scalar.Value);
            }

            throw new DeclarationException($"{source}:{scalar.Start.Line}: YAML tags are not supported ('{scalar.Tag.Value}').");
        }

        if (scalar.Style is not ScalarStyle.Plain)
        {
            return JsonValue.Create(scalar.Value);
        }

        return ResolvePlain(scalar.Value);
    }

    /// <summary>What a plain scalar with this text means.</summary>
    internal static JsonNode? ResolvePlain(string text)
    {
        switch (text)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
            default:
                break;
        }

        // A leading zero keeps an integer a string: YAML 1.2 would read 000000
        // as 0, and a label colour written unquoted must not become a number.
        if (IntegerPattern().IsMatch(text))
        {
            return !HasLeadingZero(text)
                && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer)
                    ? JsonValue.Create(integer)
                    : JsonValue.Create(text);
        }

        if (FloatPattern().IsMatch(text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
        {
            return JsonValue.Create(real);
        }

        return JsonValue.Create(text);
    }

    private static bool HasLeadingZero(string text)
    {
        ReadOnlySpan<char> digits = text.AsSpan().TrimStart("+-");
        return digits.Length > 1 && digits[0] == '0';
    }

    private static void WriteNode(IEmitter emitter, JsonNode? node)
    {
        switch (node)
        {
            case null:
                emitter.Emit(Plain("null"));
                break;

            case JsonObject mapping:
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, isImplicit: true,
                    mapping.Count == 0 ? MappingStyle.Flow : MappingStyle.Block));

                foreach (KeyValuePair<string, JsonNode?> property in mapping)
                {
                    WriteString(emitter, property.Key);
                    WriteNode(emitter, property.Value);
                }

                emitter.Emit(new MappingEnd());
                break;

            case JsonArray sequence:
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, isImplicit: true,
                    sequence.Count == 0 ? SequenceStyle.Flow : SequenceStyle.Block));

                foreach (JsonNode? item in sequence)
                {
                    WriteNode(emitter, item);
                }

                emitter.Emit(new SequenceEnd());
                break;

            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteString(emitter, value.GetValue<string>());
                        break;
                    case JsonValueKind.True:
                        emitter.Emit(Plain("true"));
                        break;
                    case JsonValueKind.False:
                        emitter.Emit(Plain("false"));
                        break;
                    case JsonValueKind.Number:
                        emitter.Emit(Plain(value.ToJsonString()));
                        break;
                    default:
                        emitter.Emit(Plain("null"));
                        break;
                }

                break;
        }
    }

    private static void WriteString(IEmitter emitter, string text)
    {
        ScalarStyle style;

        if (text.Contains('\n', StringComparison.Ordinal))
        {
            style = text.EndsWith('\n') && !text.EndsWith("\n\n", StringComparison.Ordinal)
                ? ScalarStyle.Literal
                : ScalarStyle.DoubleQuoted;
        }
        else if (ResolvePlain(text) is not JsonValue resolved
            || resolved.GetValueKind() is not JsonValueKind.String
            || IntegerPattern().IsMatch(text)
            || FloatPattern().IsMatch(text)
            || Yaml11Booleans.Contains(text))
        {
            // Would read back as null, a boolean or a number - here, or in a
            // YAML 1.1 reader, which is what many other tools still are.
            style = ScalarStyle.DoubleQuoted;
        }
        else
        {
            style = ScalarStyle.Any;
        }

        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, text, style, isPlainImplicit: true, isQuotedImplicit: true));
    }

    private static Scalar Plain(string text) =>
        new(AnchorName.Empty, TagName.Empty, text, ScalarStyle.Plain, isPlainImplicit: true, isQuotedImplicit: false);

    [GeneratedRegex("^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex FloatPattern();
}
