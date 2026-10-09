// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.JsonLd.Json;

internal enum JsonKind : byte
{
    Null,
    False,
    True,
    Number,
    String,
    Array,
    Object,

    /// <summary>A member of an object: a name and a value node.</summary>
    Member,
}

/// <summary>A span of the tree's text arena; <see cref="None"/> is absent.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal readonly record struct TextRange(int Start, int Length)
{
    internal static TextRange None => new(0, -1);

    internal bool IsNone => Length < 0;
}

/// <summary>
/// A JSON document as index-linked nodes in pooled arrays, every string in
/// one UTF-8 arena (ADR 0112, <c>Utf8JsonReaderBuildsTheTree</c>). The
/// expansion algorithm appends its result to the same tree
/// (<c>ExpansionWritesIntoTheTree</c>); nothing is an object per node.
/// </summary>
/// <remarks>
/// A container's children are a singly linked list through <c>Next</c>, with
/// the first and last child and the count on the container, so that appending
/// is constant and iteration is in document order. An object's children are
/// <see cref="JsonKind.Member"/> nodes carrying an interned name id and the
/// value node. Removing a member unlinks it; the node stays in the arrays
/// until the reset.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class JsonTree
{
    private struct Node
    {
        public JsonKind Kind;
        public int Start;
        public int Length;
        public int NameId;
        public int First;
        public int Last;
        public int Next;
        public int Count;
        public int Value;
    }

    private Node[] _nodes = ArrayPool<Node>.Shared.Rent(1024);

    private byte[] _text = ArrayPool<byte>.Shared.Rent(16384);

    // Spans over the text are handed around while the tree grows, so a buffer
    // the text outgrew is kept until the reset rather than returned at once.
    private byte[]?[] _retired = new byte[]?[8];
    private int _retiredCount;

    internal JsonTree(NameTable names)
    {
        Names = names;
    }

    internal NameTable Names { get; }

    internal int NodeCount { get; private set; }

    internal int TextLength { get; private set; }

    internal void Reset()
    {
        NodeCount = 0;
        TextLength = 0;

        for (int i = 0; i < _retiredCount; i++)
        {
            ArrayPool<byte>.Shared.Return(_retired[i]!);
            _retired[i] = null;
        }

        _retiredCount = 0;
    }

    // ------------------------------------------------------------ building

    internal int AddNull() => Add(JsonKind.Null);

    internal int AddBoolean(bool value) => Add(value ? JsonKind.True : JsonKind.False);

    internal int AddNumber(ReadOnlySpan<byte> raw) => AddNumber(AddText(raw));

    internal int AddNumber(TextRange range)
    {
        int node = Add(JsonKind.Number);
        _nodes[node].Start = range.Start;
        _nodes[node].Length = range.Length;
        return node;
    }

    internal int AddString(ReadOnlySpan<byte> utf8) => AddString(AddText(utf8));

    internal int AddString(TextRange range)
    {
        int node = Add(JsonKind.String);
        _nodes[node].Start = range.Start;
        _nodes[node].Length = range.Length;
        return node;
    }

    /// <summary>A string node whose text is a name's bytes.</summary>
    internal int AddStringOfName(int nameId) => AddString(Names.Bytes(nameId));

    internal int AddArray()
    {
        int node = Add(JsonKind.Array);
        _nodes[node].First = -1;
        _nodes[node].Last = -1;
        return node;
    }

    internal int AddObject()
    {
        int node = Add(JsonKind.Object);
        _nodes[node].First = -1;
        _nodes[node].Last = -1;
        return node;
    }

    /// <summary>An array holding one value.</summary>
    internal int AddArrayOf(int value)
    {
        int array = AddArray();
        Append(array, value);
        return array;
    }

    internal void Append(int array, int value)
    {
        ref Node container = ref _nodes[array];
        _nodes[value].Next = -1;

        if (container.Last < 0)
        {
            container.First = value;
        }
        else
        {
            _nodes[container.Last].Next = value;
        }

        container.Last = value;
        container.Count++;
    }

    /// <summary>Appends every item of <paramref name="source"/> (an array) to <paramref name="array"/>, or the value itself if it is not an array.</summary>
    internal void AppendAll(int array, int source)
    {
        if (Kind(source) != JsonKind.Array)
        {
            Append(array, source);
            return;
        }

        for (int item = First(source); item >= 0;)
        {
            int next = Next(item);
            Append(array, item);
            item = next;
        }
    }

    internal int AddMember(int obj, int nameId, int value)
    {
        int member = Add(JsonKind.Member);
        _nodes[member].NameId = nameId;
        _nodes[member].Value = value;
        Append(obj, member);
        return member;
    }

    internal int AddMember(int obj, ReadOnlySpan<byte> name, int value) => AddMember(obj, Names.Intern(name), value);

    /// <summary>Sets the member, replacing an existing one of the name.</summary>
    internal void SetMember(int obj, int nameId, int value)
    {
        int member = FindMember(obj, nameId);

        if (member >= 0)
        {
            _nodes[member].Value = value;
            return;
        }

        AddMember(obj, nameId, value);
    }

    internal void RemoveMember(int obj, int nameId)
    {
        ref Node container = ref _nodes[obj];
        int previous = -1;

        for (int member = container.First; member >= 0; member = _nodes[member].Next)
        {
            if (_nodes[member].NameId == nameId)
            {
                if (previous < 0)
                {
                    container.First = _nodes[member].Next;
                }
                else
                {
                    _nodes[previous].Next = _nodes[member].Next;
                }

                if (container.Last == member)
                {
                    container.Last = previous;
                }

                container.Count--;
                return;
            }

            previous = member;
        }
    }

    internal TextRange AddText(ReadOnlySpan<byte> utf8)
    {
        EnsureText(utf8.Length);
        utf8.CopyTo(_text.AsSpan(TextLength));
        TextRange range = new(TextLength, utf8.Length);
        TextLength += utf8.Length;
        return range;
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.ContextsAreNotPerQuad), Scope = ExceptionScope.HotPath)]
    internal TextRange AddText(string text)
    {
        int max = Encoding.UTF8.GetMaxByteCount(text.Length);
        EnsureText(max);
        int written = Encoding.UTF8.GetBytes(text, _text.AsSpan(TextLength));
        TextRange range = new(TextLength, written);
        TextLength += written;
        return range;
    }

    /// <summary>Reserves <paramref name="length"/> bytes of text to write into; commit with <see cref="CommitText"/>.</summary>
    internal Span<byte> ReserveText(int length)
    {
        EnsureText(length);
        return _text.AsSpan(TextLength, length);
    }

    internal TextRange CommitText(int written)
    {
        TextRange range = new(TextLength, written);
        TextLength += written;
        return range;
    }

    /// <summary>The concatenation of two spans, as one range.</summary>
    internal TextRange AddText(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        EnsureText(first.Length + second.Length);
        first.CopyTo(_text.AsSpan(TextLength));
        second.CopyTo(_text.AsSpan(TextLength + first.Length));
        TextRange range = new(TextLength, first.Length + second.Length);
        TextLength += range.Length;
        return range;
    }

    // ------------------------------------------------------------ reading

    internal JsonKind Kind(int node) => _nodes[node].Kind;

    internal bool IsString(int node) => node >= 0 && _nodes[node].Kind == JsonKind.String;

    internal bool IsObject(int node) => node >= 0 && _nodes[node].Kind == JsonKind.Object;

    internal bool IsArray(int node) => node >= 0 && _nodes[node].Kind == JsonKind.Array;

    internal bool IsNull(int node) => node >= 0 && _nodes[node].Kind == JsonKind.Null;

    internal bool IsScalar(int node) => node >= 0 && _nodes[node].Kind is not (JsonKind.Array or JsonKind.Object or JsonKind.Member);

    internal bool IsBoolean(int node) => node >= 0 && _nodes[node].Kind is JsonKind.True or JsonKind.False;

    internal bool IsNumber(int node) => node >= 0 && _nodes[node].Kind == JsonKind.Number;

    internal int Count(int container) => _nodes[container].Count;

    internal int First(int container) => _nodes[container].First;

    internal int Next(int node) => _nodes[node].Next;

    internal int NameOf(int member) => _nodes[member].NameId;

    internal int ValueOf(int member) => _nodes[member].Value;

    internal ReadOnlySpan<byte> NameBytes(int member) => Names.Bytes(_nodes[member].NameId);

    /// <summary>The text of a string or number node.</summary>
    internal ReadOnlySpan<byte> Bytes(int node) => _text.AsSpan(_nodes[node].Start, _nodes[node].Length);

    internal TextRange Range(int node) => new(_nodes[node].Start, _nodes[node].Length);

    internal ReadOnlySpan<byte> Bytes(TextRange range) => range.IsNone ? default : _text.AsSpan(range.Start, range.Length);

    internal bool StringEquals(int node, ReadOnlySpan<byte> text) => IsString(node) && Bytes(node).SequenceEqual(text);

    internal bool TextEquals(TextRange range, ReadOnlySpan<byte> text) => !range.IsNone && Bytes(range).SequenceEqual(text);

    internal bool TextEquals(TextRange left, TextRange right) =>
        left.IsNone ? right.IsNone : !right.IsNone && Bytes(left).SequenceEqual(Bytes(right));

    internal int FindMember(int obj, int nameId)
    {
        for (int member = _nodes[obj].First; member >= 0; member = _nodes[member].Next)
        {
            if (_nodes[member].NameId == nameId)
            {
                return member;
            }
        }

        return -1;
    }

    /// <summary>The value of the member named, or -1.</summary>
    internal int Member(int obj, int nameId)
    {
        int member = FindMember(obj, nameId);
        return member < 0 ? -1 : _nodes[member].Value;
    }

    internal bool HasMember(int obj, int nameId) => FindMember(obj, nameId) >= 0;

    /// <summary>Whether the object has exactly these members, no more.</summary>
    internal bool HasOnly(int obj, int nameId)
    {
        return _nodes[obj].Count == 1 && _nodes[_nodes[obj].First].NameId == nameId;
    }

    /// <summary>The single string of a string node or of an array holding one string; -1 otherwise.</summary>
    internal int SingleString(int node)
    {
        if (IsString(node))
        {
            return node;
        }

        if (IsArray(node) && Count(node) == 1 && IsString(First(node)))
        {
            return First(node);
        }

        return -1;
    }

    /// <summary>
    /// Sorts an object's members by name in UTF-16 code unit order (RFC 8785
    /// §3.2.3), which is also the order the specification's algorithms call
    /// lexicographical.
    /// </summary>
    [DesignDecision(typeof(JsonLdOverUtf8Json.JsonLiteralsAreCanonical), Scope = ExceptionScope.HotPath)]
    internal void SortMembers(int obj)
    {
        int count = _nodes[obj].Count;

        if (count < 2)
        {
            return;
        }

        int[] members = ArrayPool<int>.Shared.Rent(count);
        int i = 0;

        for (int member = _nodes[obj].First; member >= 0; member = _nodes[member].Next)
        {
            members[i++] = member;
        }

        // Insertion sort: objects are small, and the sort must be stable.
        for (int a = 1; a < count; a++)
        {
            int current = members[a];
            int b = a - 1;

            while (b >= 0 && CompareUtf16(NameBytes(members[b]), NameBytes(current)) > 0)
            {
                members[b + 1] = members[b];
                b--;
            }

            members[b + 1] = current;
        }

        _nodes[obj].First = members[0];

        for (int k = 0; k < count - 1; k++)
        {
            _nodes[members[k]].Next = members[k + 1];
        }

        _nodes[members[count - 1]].Next = -1;
        _nodes[obj].Last = members[count - 1];
        ArrayPool<int>.Shared.Return(members);
    }

    /// <summary>Sorts an array of strings by UTF-16 code unit order.</summary>
    internal void SortStrings(int array)
    {
        int count = _nodes[array].Count;

        if (count < 2)
        {
            return;
        }

        int[] items = ArrayPool<int>.Shared.Rent(count);
        int i = 0;

        for (int item = _nodes[array].First; item >= 0; item = _nodes[item].Next)
        {
            items[i++] = item;
        }

        for (int a = 1; a < count; a++)
        {
            int current = items[a];
            int b = a - 1;

            while (b >= 0 && CompareUtf16(Bytes(items[b]), Bytes(current)) > 0)
            {
                items[b + 1] = items[b];
                b--;
            }

            items[b + 1] = current;
        }

        _nodes[array].First = items[0];

        for (int k = 0; k < count - 1; k++)
        {
            _nodes[items[k]].Next = items[k + 1];
        }

        _nodes[items[count - 1]].Next = -1;
        _nodes[array].Last = items[count - 1];
        ArrayPool<int>.Shared.Return(items);
    }

    /// <summary>Compares two UTF-8 strings as sequences of UTF-16 code units.</summary>
    internal static int CompareUtf16(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            int a = NextUnit(left, ref i, out int aSecond);
            int b = NextUnit(right, ref j, out int bSecond);

            if (a != b)
            {
                return a - b;
            }

            if (aSecond != bSecond)
            {
                return aSecond - bSecond;
            }
        }

        return (left.Length - i) - (right.Length - j);
    }

    /// <summary>The next UTF-16 code unit of a UTF-8 string, with the low surrogate of a supplementary character as the second.</summary>
    private static int NextUnit(ReadOnlySpan<byte> text, ref int at, out int second)
    {
        second = -1;
        byte b = text[at];

        if (b < 0x80)
        {
            at++;
            return b;
        }

        if ((b & 0xE0) == 0xC0 && at + 1 < text.Length)
        {
            int cp = ((b & 0x1F) << 6) | (text[at + 1] & 0x3F);
            at += 2;
            return cp;
        }

        if ((b & 0xF0) == 0xE0 && at + 2 < text.Length)
        {
            int cp = ((b & 0x0F) << 12) | ((text[at + 1] & 0x3F) << 6) | (text[at + 2] & 0x3F);
            at += 3;
            return cp;
        }

        if ((b & 0xF8) == 0xF0 && at + 3 < text.Length)
        {
            int cp = ((b & 0x07) << 18) | ((text[at + 1] & 0x3F) << 12) | ((text[at + 2] & 0x3F) << 6) | (text[at + 3] & 0x3F);
            at += 4;
            cp -= 0x10000;
            second = 0xDC00 | (cp & 0x3FF);
            return 0xD800 | (cp >> 10);
        }

        at++;
        return b;
    }

    /// <summary>Whether two subtrees are the same JSON value, members in any order.</summary>
    internal bool SameValue(int left, int right)
    {
        JsonKind kind = Kind(left);

        if (kind != Kind(right))
        {
            return false;
        }

        switch (kind)
        {
            case JsonKind.String:
            case JsonKind.Number:
                return Bytes(left).SequenceEqual(Bytes(right));
            case JsonKind.Array:
                if (Count(left) != Count(right))
                {
                    return false;
                }

                for (int a = First(left), b = First(right); a >= 0; a = Next(a), b = Next(b))
                {
                    if (!SameValue(a, b))
                    {
                        return false;
                    }
                }

                return true;
            case JsonKind.Object:
                if (Count(left) != Count(right))
                {
                    return false;
                }

                for (int member = First(left); member >= 0; member = Next(member))
                {
                    int other = Member(right, NameOf(member));

                    if (other < 0 || !SameValue(ValueOf(member), other))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return true;
        }
    }

    /// <summary>A deep copy of a subtree, as new nodes.</summary>
    internal int Copy(int node)
    {
        switch (Kind(node))
        {
            case JsonKind.Array:
            {
                int array = AddArray();

                for (int item = First(node); item >= 0; item = Next(item))
                {
                    Append(array, Copy(item));
                }

                return array;
            }

            case JsonKind.Object:
            {
                int obj = AddObject();

                for (int member = First(node); member >= 0; member = Next(member))
                {
                    AddMember(obj, NameOf(member), Copy(ValueOf(member)));
                }

                return obj;
            }

            case JsonKind.String:
                return AddString(Range(node));
            case JsonKind.Number:
            {
                int number = Add(JsonKind.Number);
                _nodes[number].Start = _nodes[node].Start;
                _nodes[number].Length = _nodes[node].Length;
                return number;
            }

            default:
                return Add(Kind(node));
        }
    }

    // ------------------------------------------------------------ storage

    private int Add(JsonKind kind)
    {
        if (NodeCount == _nodes.Length)
        {
            Node[] bigger = ArrayPool<Node>.Shared.Rent(_nodes.Length * 2);
            _nodes.AsSpan(0, NodeCount).CopyTo(bigger);
            ArrayPool<Node>.Shared.Return(_nodes);
            _nodes = bigger;
        }

        ref Node node = ref _nodes[NodeCount];
        node = default;
        node.Kind = kind;
        node.Next = -1;
        node.First = -1;
        node.Last = -1;
        node.Value = -1;
        return NodeCount++;
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonReaderBuildsTheTree), Scope = ExceptionScope.HotPath)]
    private void EnsureText(int length)
    {
        if (TextLength + length <= _text.Length)
        {
            return;
        }

        byte[] bigger = ArrayPool<byte>.Shared.Rent(Math.Max(_text.Length * 2, TextLength + length));
        _text.AsSpan(0, TextLength).CopyTo(bigger);

        if (_retiredCount == _retired.Length)
        {
            Array.Resize(ref _retired, _retired.Length * 2);
        }

        _retired[_retiredCount++] = _text;
        _text = bigger;
    }
}
