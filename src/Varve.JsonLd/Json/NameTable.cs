// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.JsonLd.Json;

/// <summary>
/// Interns UTF-8 names to small integers, so that a member name, a term and a
/// keyword are compared and looked up as integers and no string is made for
/// them (ADR 0123, <c>Utf8JsonReaderBuildsTheTree</c>).
/// </summary>
/// <remarks>
/// The keywords are interned first, in the order of <see cref="Keyword"/>, so
/// that their ids are constants. The table is reset per document; a reset
/// keeps the keywords.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class NameTable
{
    private byte[] _bytes = new byte[4096];
    private int _bytesLength;

    private int[] _starts = ArrayPool<int>.Shared.Rent(256);
    private int[] _lengths = ArrayPool<int>.Shared.Rent(256);
    private int[] _hashes = ArrayPool<int>.Shared.Rent(256);

    private int[] _buckets = new int[512];

    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonReaderBuildsTheTree), Scope = ExceptionScope.HotPath)]
    internal NameTable()
    {
        _buckets.AsSpan().Fill(-1);

        for (int i = 0; i < Keyword.Count; i++)
        {
            Intern(Encoding.UTF8.GetBytes(Keyword.Name(i)));
        }
    }

    internal int Count { get; private set; }

    /// <summary>Forgets every name but the keywords.</summary>
    internal void Reset()
    {
        if (Count == Keyword.Count)
        {
            return;
        }

        Count = Keyword.Count;
        _bytesLength = _starts[Keyword.Count - 1] + _lengths[Keyword.Count - 1];
        _buckets.AsSpan().Fill(-1);

        for (int i = 0; i < Count; i++)
        {
            Place(i);
        }
    }

    internal int Intern(ReadOnlySpan<byte> name)
    {
        int hash = Hash(name);
        int mask = _buckets.Length - 1;
        int slot = hash & mask;

        while (true)
        {
            int id = _buckets[slot];

            if (id < 0)
            {
                break;
            }

            if (_hashes[id] == hash && Bytes(id).SequenceEqual(name))
            {
                return id;
            }

            slot = (slot + 1) & mask;
        }

        return Add(name, hash);
    }

    /// <summary>The id of <paramref name="name"/> if it has been interned, else -1.</summary>
    internal int Find(ReadOnlySpan<byte> name)
    {
        int hash = Hash(name);
        int mask = _buckets.Length - 1;
        int slot = hash & mask;

        while (true)
        {
            int id = _buckets[slot];

            if (id < 0)
            {
                return -1;
            }

            if (_hashes[id] == hash && Bytes(id).SequenceEqual(name))
            {
                return id;
            }

            slot = (slot + 1) & mask;
        }
    }

    internal ReadOnlySpan<byte> Bytes(int id) => _bytes.AsSpan(_starts[id], _lengths[id]);

    [DesignDecision(typeof(JsonLdOverUtf8Json.TheFirstErrorEndsTheParse), Scope = ExceptionScope.HotPath)]
    internal string Text(int id) => Encoding.UTF8.GetString(Bytes(id));

    /// <summary>Whether the id names one of the specification's keywords.</summary>
    internal static bool IsKeyword(int id) => id >= 0 && id < Keyword.Count;

    /// <summary>
    /// Whether a name has the form of a keyword without being one: <c>@</c>
    /// followed by letters only (JSON-LD 1.1 API §4.1.2 step 5.?, "looks
    /// like a keyword"), which the processor ignores rather than treats as a
    /// term.
    /// </summary>
    internal static bool LooksLikeKeyword(ReadOnlySpan<byte> name)
    {
        if (name.Length < 2 || name[0] != (byte)'@')
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            byte b = name[i];

            if (!((b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z')))
            {
                return false;
            }
        }

        return true;
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonReaderBuildsTheTree), Scope = ExceptionScope.HotPath)]
    private int Add(ReadOnlySpan<byte> name, int hash)
    {
        if (Count == _starts.Length)
        {
            Grow(ref _starts);
            Grow(ref _lengths);
            Grow(ref _hashes);
        }

        if (_bytesLength + name.Length > _bytes.Length)
        {
            // The old buffer is not returned: a span over a name may be in
            // flight while a new name is interned. Names are a document's
            // vocabulary, so this happens a handful of times in a lifetime.
            byte[] bigger = new byte[Math.Max(_bytes.Length * 2, _bytesLength + name.Length)];
            _bytes.AsSpan(0, _bytesLength).CopyTo(bigger);
            _bytes = bigger;
        }

        name.CopyTo(_bytes.AsSpan(_bytesLength));
        int id = Count++;
        _starts[id] = _bytesLength;
        _lengths[id] = name.Length;
        _hashes[id] = hash;
        _bytesLength += name.Length;

        if (Count * 2 > _buckets.Length)
        {
            Rehash();
        }
        else
        {
            Place(id);
        }

        return id;
    }

    private void Place(int id)
    {
        int mask = _buckets.Length - 1;
        int slot = _hashes[id] & mask;

        while (_buckets[slot] >= 0)
        {
            slot = (slot + 1) & mask;
        }

        _buckets[slot] = id;
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonReaderBuildsTheTree), Scope = ExceptionScope.HotPath)]
    private void Rehash()
    {
        // A power of two, doubled when half full; names are a document's
        // vocabulary, so this happens a handful of times per document.
        _buckets = new int[_buckets.Length * 2];
        _buckets.AsSpan().Fill(-1);

        for (int i = 0; i < Count; i++)
        {
            Place(i);
        }
    }

    private static void Grow(ref int[] array)
    {
        int[] bigger = ArrayPool<int>.Shared.Rent(array.Length * 2);
        array.AsSpan().CopyTo(bigger);
        ArrayPool<int>.Shared.Return(array);
        array = bigger;
    }

    private static int Hash(ReadOnlySpan<byte> name)
    {
        // FNV-1a, folded to a non-negative int.
        uint hash = 2166136261;

        foreach (byte b in name)
        {
            hash = (hash ^ b) * 16777619;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}

/// <summary>The keyword ids, which are the first ids of every <see cref="NameTable"/>.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class Keyword
{
    internal const int Context = 0;
    internal const int Id = 1;
    internal const int Type = 2;
    internal const int Value = 3;
    internal const int Language = 4;
    internal const int Direction = 5;
    internal const int List = 6;
    internal const int Set = 7;
    internal const int Reverse = 8;
    internal const int Index = 9;
    internal const int Graph = 10;
    internal const int Included = 11;
    internal const int Nest = 12;
    internal const int Json = 13;
    internal const int Base = 14;
    internal const int Vocab = 15;
    internal const int Version = 16;
    internal const int Propagate = 17;
    internal const int Protected = 18;
    internal const int Import = 19;
    internal const int Container = 20;
    internal const int Prefix = 21;
    internal const int None = 22;
    internal const int Default = 23;
    internal const int Embed = 24;
    internal const int Explicit = 25;
    internal const int OmitDefault = 26;
    internal const int RequireAll = 27;
    internal const int Preserve = 28;
    internal const int Any = 29;

    internal const int Count = 30;

    /// <summary>The spelling of a keyword id.</summary>
    internal static string Name(int id) => id switch
    {
        Context => "@context",
        Id => "@id",
        Type => "@type",
        Value => "@value",
        Language => "@language",
        Direction => "@direction",
        List => "@list",
        Set => "@set",
        Reverse => "@reverse",
        Index => "@index",
        Graph => "@graph",
        Included => "@included",
        Nest => "@nest",
        Json => "@json",
        Base => "@base",
        Vocab => "@vocab",
        Version => "@version",
        Propagate => "@propagate",
        Protected => "@protected",
        Import => "@import",
        Container => "@container",
        Prefix => "@prefix",
        None => "@none",
        Default => "@default",
        Embed => "@embed",
        Explicit => "@explicit",
        OmitDefault => "@omitDefault",
        RequireAll => "@requireAll",
        Preserve => "@preserve",
        _ => "@any",
    };
}
