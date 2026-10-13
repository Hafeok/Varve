// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;

namespace Varve.Protocol.Client;

/// <summary>
/// A <see cref="DatasetSetup"/> from a setup file (ADR 0130): the YAML subset
/// <c>docs/operator/datasets.md</c> specifies, read without a package, and
/// anything outside it refused with its line and column, never accepted
/// silently.
/// </summary>
/// <remarks>
/// <para>The subset:</para>
/// <list type="bullet">
/// <item>mappings of plain keys, <c>key: value</c>, nested by indentation in spaces;</item>
/// <item>sequences of mappings, <c>- key: value</c>;</item>
/// <item>plain, <c>'single-quoted'</c> and <c>"double-quoted"</c> scalars, and <c>|</c> block scalars;</item>
/// <item>comments, a <c>#</c> starting a line or following a space.</item>
/// </list>
/// <para>
/// Refused: a tab in indentation, an anchor, an alias, a tag, a flow collection,
/// a folded scalar (<c>&gt;</c>), a document marker, a key outside the schema or
/// given twice. Files a setup names are relative to the setup file.
/// </para>
/// </remarks>
public static class DatasetSetupFile
{
    private static ImmutableArray<string> TopKeys { get; } = ["dataset", "storage", "settings", "baseline", "migrations"];

    private static ImmutableArray<string> BaselineKeys { get; } = ["name", "graph", "file", "format"];

    private static ImmutableArray<string> MigrationKeys { get; } = ["id", "update", "file"];

    /// <summary>The setup the file at <paramref name="path"/> declares.</summary>
    /// <exception cref="SetupFileException">The file is outside the subset, or outside the schema.</exception>
    public static DatasetSetup Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        return Parse(File.ReadAllText(full, new UTF8Encoding(false, throwOnInvalidBytes: true)), Path.GetDirectoryName(full)!, full);
    }

    /// <summary>The setup <paramref name="text"/> declares, its files relative to <paramref name="directory"/>.</summary>
    /// <exception cref="SetupFileException">The text is outside the subset, or outside the schema.</exception>
    public static DatasetSetup Parse(string text, string directory, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(directory);
        Reader reader = new(text, source ?? "the setup");
        Node root = reader.Document();
        return Build(root, directory, source ?? "the setup");
    }

    private static DatasetSetup Build(Node root, string directory, string source)
    {
        Mapping top = root.AsMapping(source);
        top.Only(TopKeys, source);
        DatasetSetup setup = new(top.Required("dataset", source).AsScalar(source).Text)
        {
            Storage = top.Optional("storage") is Node storage
                ? storage.AsScalar(source).Text switch
                {
                    "File" => SetupStorage.File,
                    "Memory" => SetupStorage.Memory,
                    _ => throw storage.Error(source, "storage is File or Memory"),
                }
                : SetupStorage.File,
            Settings = top.Optional("settings")?.AsScalar(source).Text,
        };

        if (top.Optional("baseline") is Node baseline)
        {
            foreach (Node item in baseline.AsSequence(source).Items)
            {
                Mapping entry = item.AsMapping(source);
                entry.Only(BaselineKeys, source);
                Node file = entry.Required("file", source);
                string path = Path.Combine(directory, file.AsScalar(source).Text);
                string? graph = entry.Optional("graph")?.AsScalar(source).Text is { } named && named != "default" ? named : null;
                string format = entry.Optional("format")?.AsScalar(source).Text ?? BaselineSource.MediaTypeOf(path)
                    ?? throw file.Error(source, "the media type of " + file.AsScalar(source).Text + " is not known from its extension; give format");
                string name = entry.Optional("name")?.AsScalar(source).Text ?? file.AsScalar(source).Text;
                setup.Baselines.Add(new BaselineSource(name, graph, format, async cancellationToken =>
                    BaselineSource.WithoutByteOrderMark(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false))));
            }
        }

        if (top.Optional("migrations") is Node migrations)
        {
            foreach (Node item in migrations.AsSequence(source).Items)
            {
                Mapping entry = item.AsMapping(source);
                entry.Only(MigrationKeys, source);
                string id = entry.Required("id", source).AsScalar(source).Text;
                Node? update = entry.Optional("update");
                Node? file = entry.Optional("file");

                if ((update is null) == (file is null))
                {
                    throw item.Error(source, "a migration has update or file, one of the two");
                }

                string text = update is not null
                    ? update.AsScalar(source).Text
                    : File.ReadAllText(Path.Combine(directory, file!.AsScalar(source).Text), Encoding.UTF8);
                setup.Migrations.Add(new SetupMigration(id, text));
            }
        }

        return setup;
    }

    // ---- the tree ---------------------------------------------------------------

    private abstract class Node(int line, int column)
    {
        internal int Line { get; } = line;

        internal int Column { get; } = column;

        internal SetupFileException Error(string source, string message) => new(source, Line, Column, message);

        internal Mapping AsMapping(string source) => this as Mapping ?? throw Error(source, "a mapping is expected here");

        internal Sequence AsSequence(string source) => this as Sequence ?? throw Error(source, "a sequence of '- key: value' items is expected here");

        internal Scalar AsScalar(string source) => this as Scalar ?? throw Error(source, "a value is expected here, not a mapping or a sequence");
    }

    private sealed class Scalar(int line, int column, string text) : Node(line, column)
    {
        internal string Text { get; } = text;
    }

    private sealed class Sequence(int line, int column) : Node(line, column)
    {
        internal List<Node> Items { get; } = [];
    }

    private sealed class Mapping(int line, int column) : Node(line, column)
    {
        internal List<(string Key, int Line, int Column, Node Value)> Entries { get; } = [];

        internal Node? Optional(string key)
        {
            foreach ((string name, _, _, Node value) in Entries)
            {
                if (name == key)
                {
                    return value;
                }
            }

            return null;
        }

        internal Node Required(string key, string source) => Optional(key) ?? throw Error(source, "'" + key + "' is required here");

        internal void Only(ImmutableArray<string> keys, string source)
        {
            foreach ((string key, int line, int column, _) in Entries)
            {
                if (keys.IndexOf(key) < 0)
                {
                    throw new SetupFileException(source, line, column, "'" + key + "' is not a key here; the keys are " + string.Join(", ", keys));
                }
            }
        }
    }

    // ---- the reader ---------------------------------------------------------------

    private sealed class Reader
    {
        private readonly string[] _lines;
        private readonly string _source;
        private int _index;

        internal Reader(string text, string source)
        {
            _lines = text.ReplaceLineEndings("\n").Split('\n');
            _source = source;
        }

        internal Node Document()
        {
            SkipBlank();

            if (_index >= _lines.Length)
            {
                throw new SetupFileException(_source, 1, 1, "the setup is empty");
            }

            Node root = Block(Indent(_lines[_index]));
            SkipBlank();

            if (_index < _lines.Length)
            {
                throw Here(Indent(_lines[_index]), "this line is indented less than the block it would belong to, or more than its own");
            }

            return root;
        }

        // A mapping or a sequence whose lines start at `indent`.
        private Node Block(int indent)
        {
            string line = _lines[_index];
            return line.AsSpan(indent).StartsWith("- ") || line.AsSpan(indent).SequenceEqual("-") ? SequenceAt(indent) : MappingAt(indent);
        }

        private Sequence SequenceAt(int indent)
        {
            Sequence sequence = new(_index + 1, indent + 1);

            while (true)
            {
                SkipBlank();

                if (_index >= _lines.Length || Indent(_lines[_index]) != indent || !_lines[_index].AsSpan(indent).StartsWith("-"))
                {
                    return sequence;
                }

                string line = _lines[_index];

                if (line.Length > indent + 1 && line[indent + 1] != ' ')
                {
                    throw Here(indent + 1, "a sequence item is '- ' and then the item");
                }

                // The item's first key sits after "- "; its other keys align with it.
                int content = indent + 2;

                while (content < line.Length && line[content] == ' ')
                {
                    content++;
                }

                if (content >= line.Length || line[content] == '#')
                {
                    throw Here(indent, "a sequence item here is a mapping, '- key: value'");
                }

                sequence.Items.Add(MappingAt(content, firstLineIndent: content));
            }
        }

        private Mapping MappingAt(int indent, int? firstLineIndent = null)
        {
            Mapping mapping = new(_index + 1, indent + 1);
            HashSet<string> seen = new(StringComparer.Ordinal);
            bool first = true;

            while (true)
            {
                if (!first || firstLineIndent is null)
                {
                    SkipBlank();
                }

                if (_index >= _lines.Length)
                {
                    return mapping;
                }

                string line = _lines[_index];
                int at = first && firstLineIndent is int given ? given : Indent(line);

                if (at != indent)
                {
                    if (at > indent)
                    {
                        throw Here(at, "this line is indented more than the mapping it is in");
                    }

                    return mapping;
                }

                if (!first || firstLineIndent is null)
                {
                    if (line.AsSpan(indent).StartsWith("-"))
                    {
                        return mapping;
                    }
                }

                first = false;
                (string key, int keyColumn, string rest, int restColumn) = KeyAt(line, indent);

                if (!seen.Add(key))
                {
                    throw new SetupFileException(_source, _index + 1, keyColumn, "'" + key + "' is given twice");
                }

                int keyLine = _index + 1;
                _index++;
                mapping.Entries.Add((key, keyLine, keyColumn, Value(rest, restColumn, keyLine, indent)));
            }
        }

        // The value after "key:" on line `line`: a scalar on the same line, a
        // block scalar after "|", or a nested block on the lines after.
        private Node Value(string rest, int column, int line, int indent)
        {
            string trimmed = rest.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                SkipBlank();

                if (_index >= _lines.Length || Indent(_lines[_index]) <= indent)
                {
                    // An empty value is refused by the schema, not the subset.
                    return new Scalar(line, column, string.Empty);
                }

                int nested = Indent(_lines[_index]);
                return Block(nested);
            }

            int start = column + (rest.Length - rest.TrimStart().Length);

            switch (trimmed[0])
            {
                case '|':
                    if (trimmed.Length > 1 && !trimmed[1..].TrimStart().StartsWith('#'))
                    {
                        throw new SetupFileException(_source, line, start + 1, "a block scalar is '|' alone; its chomping and indentation indicators are outside the subset");
                    }

                    return BlockScalar(line, start, indent);
                case '>':
                    throw new SetupFileException(_source, line, start, "a folded scalar ('>') is outside the subset; use '|'");
                case '&':
                    throw new SetupFileException(_source, line, start, "an anchor ('&') is outside the subset");
                case '*':
                    throw new SetupFileException(_source, line, start, "an alias ('*') is outside the subset");
                case '!':
                    throw new SetupFileException(_source, line, start, "a tag ('!') is outside the subset");
                case '[':
                case '{':
                    throw new SetupFileException(_source, line, start, "a flow collection ('" + trimmed[0] + "') is outside the subset");
                case '\'':
                    return new Scalar(line, start, SingleQuoted(trimmed, line, start));
                case '"':
                    return new Scalar(line, start, DoubleQuoted(trimmed, line, start));
                case '@':
                case '`':
                case '%':
                    throw new SetupFileException(_source, line, start, "'" + trimmed[0] + "' cannot start a plain value; quote it");
                default:
                    return new Scalar(line, start, Plain(trimmed));
            }
        }

        // "|": the lines after, indented more than the key, with their common
        // indentation removed and their line ends kept; trailing empty lines
        // are dropped and one line end kept (YAML's clip).
        private Scalar BlockScalar(int line, int column, int indent)
        {
            List<string> content = [];
            int? common = null;

            while (_index < _lines.Length)
            {
                string text = _lines[_index];

                if (text.Trim().Length == 0)
                {
                    content.Add(string.Empty);
                    _index++;
                    continue;
                }

                int at = Indent(text);

                if (at <= indent)
                {
                    break;
                }

                common ??= at;

                if (at < common)
                {
                    throw Here(at, "a block scalar's lines are indented at least as much as its first");
                }

                content.Add(text[common.Value..]);
                _index++;
            }

            while (content.Count > 0 && content[^1].Length == 0)
            {
                content.RemoveAt(content.Count - 1);
                _index--;
            }

            // The empty lines given back above are skipped as blank lines.
            while (_index < _lines.Length && _lines[_index].Trim().Length == 0 && content.Count > 0)
            {
                _index++;
            }

            return new Scalar(line, column, content.Count == 0 ? string.Empty : string.Join('\n', content) + "\n");
        }

        private (string Key, int KeyColumn, string Remainder, int RemainderColumn) KeyAt(string line, int indent)
        {
            int end = indent;

            while (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] is '_' or '-'))
            {
                end++;
            }

            if (end == indent || !char.IsAsciiLetter(line[indent]))
            {
                char found = indent < line.Length ? line[indent] : ' ';
                throw Here(indent, found is '"' or '\'' ? "a key is plain, not quoted" : found is '?' ? "a complex key ('?') is outside the subset" : "a line here is 'key: value'");
            }

            if (end >= line.Length || line[end] != ':' || (end + 1 < line.Length && line[end + 1] != ' '))
            {
                throw Here(end, "a key is followed by ': ' and its value, or ':' at the end of the line");
            }

            return (line[indent..end], indent + 1, line[(end + 1)..], end + 2);
        }

        // A plain value: up to a comment, which is " #".
        private static string Plain(string text)
        {
            int comment = text.IndexOf(" #", StringComparison.Ordinal);
            return (comment < 0 ? text : text[..comment]).TrimEnd();
        }

        private string SingleQuoted(string text, int line, int column)
        {
            StringBuilder value = new();
            int i = 1;

            for (; i < text.Length; i++)
            {
                if (text[i] == '\'')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        value.Append('\'');
                        i++;
                        continue;
                    }

                    break;
                }

                value.Append(text[i]);
            }

            if (i >= text.Length)
            {
                throw new SetupFileException(_source, line, column, "a single-quoted value ends on its own line with '");
            }

            Trailing(text[(i + 1)..], line, column + i + 1);
            return value.ToString();
        }

        private string DoubleQuoted(string text, int line, int column)
        {
            StringBuilder value = new();
            int i = 1;

            for (; i < text.Length && text[i] != '"'; i++)
            {
                if (text[i] != '\\')
                {
                    value.Append(text[i]);
                    continue;
                }

                if (++i >= text.Length)
                {
                    break;
                }

                switch (text[i])
                {
                    case '"':
                        value.Append('"');
                        break;
                    case '\\':
                        value.Append('\\');
                        break;
                    case '/':
                        value.Append('/');
                        break;
                    case 'n':
                        value.Append('\n');
                        break;
                    case 't':
                        value.Append('\t');
                        break;
                    case 'r':
                        value.Append('\r');
                        break;
                    case 'u' when i + 4 < text.Length && int.TryParse(text.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code):
                        value.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new SetupFileException(_source, line, column + i - 1, "'\\" + text[i] + "' is not an escape the subset reads: \\\" \\\\ \\/ \\n \\t \\r \\uXXXX");
                }
            }

            if (i >= text.Length)
            {
                throw new SetupFileException(_source, line, column, "a double-quoted value ends on its own line with \"");
            }

            Trailing(text[(i + 1)..], line, column + i + 1);
            return value.ToString();
        }

        private void Trailing(string after, int line, int column)
        {
            string rest = after.TrimStart();

            if (rest.Length > 0 && rest[0] != '#')
            {
                throw new SetupFileException(_source, line, column + (after.Length - rest.Length), "nothing but a comment follows a quoted value");
            }
        }

        private void SkipBlank()
        {
            while (_index < _lines.Length)
            {
                string line = _lines[_index];
                CheckIndentation(line);
                string trimmed = line.TrimStart(' ');

                if (trimmed.StartsWith("---", StringComparison.Ordinal) || trimmed.StartsWith("...", StringComparison.Ordinal) || (trimmed.StartsWith('%') && Indent(line) == 0))
                {
                    throw Here(Indent(line), "a document marker or directive is outside the subset: a setup file is one document");
                }

                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    _index++;
                    continue;
                }

                return;
            }
        }

        private void CheckIndentation(string line)
        {
            for (int i = 0; i < line.Length && (line[i] == ' ' || line[i] == '\t'); i++)
            {
                if (line[i] == '\t')
                {
                    throw new SetupFileException(_source, _index + 1, i + 1, "indentation is spaces; a tab is outside the subset");
                }
            }
        }

        private static int Indent(string line)
        {
            int i = 0;

            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            return i;
        }

        private SetupFileException Here(int column, string message) => new(_source, _index + 1, column + 1, message);
    }
}

/// <summary>A setup file outside the YAML subset or the setup's schema (ADR 0130), at a line and a column.</summary>
public sealed class SetupFileException : Exception
{
    /// <summary>An error in <paramref name="source"/> at <paramref name="line"/> and <paramref name="column"/>, both from 1.</summary>
    public SetupFileException(string source, int line, int column, string message)
        : base(source + ":" + line.ToString(CultureInfo.InvariantCulture) + ":" + column.ToString(CultureInfo.InvariantCulture) + ": " + message)
    {
        Line = line;
        Column = column;
    }

    /// <summary>Creates one with a message.</summary>
    public SetupFileException(string message)
        : base(message)
    {
    }

    /// <summary>Creates one with a message and the error underneath.</summary>
    public SetupFileException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Creates one with no message.</summary>
    public SetupFileException()
    {
    }

    /// <summary>The line, from 1.</summary>
    public int Line { get; }

    /// <summary>The column, from 1.</summary>
    public int Column { get; }
}
