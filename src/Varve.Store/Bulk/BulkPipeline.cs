// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// The operations a parser handed over, as their terms' bytes (ADR 0107):
/// what the parser's thread writes and a worker resolves. A term is a kind
/// byte and its fields, each a length and its bytes; a triple term nests;
/// the default graph is one byte.
/// </summary>
internal sealed class OperationBuffer
{
    private const byte Iri = 0;
    private const byte Blank = 1;
    private const byte Literal = 2;
    private const byte Triple = 3;
    private const byte DefaultGraph = 4;

    private readonly int _threshold;
    private readonly int[] _starts;
    private readonly ulong[] _sequences;
    private byte[] _arena;
    private int _used;

    internal OperationBuffer(int bytes, int operations)
    {
        _arena = new byte[bytes];
        _threshold = Math.Max(0, bytes - 4096);
        _starts = new int[operations];
        _sequences = new ulong[operations];
    }

    /// <summary>Operations held.</summary>
    internal int Count { get; private set; }

    /// <summary>The sequence number of the first operation: the run's name.</summary>
    internal ulong Sequence => Count == 0 ? 0 : _sequences[0];

    /// <summary>Whether the parser must hand the buffer over before the next operation.</summary>
    internal bool IsFull => Count == _starts.Length || _used >= _threshold;

    internal void Begin(ulong sequence)
    {
        _starts[Count] = _used;
        _sequences[Count] = sequence;
        Count++;
    }

    internal void Reset()
    {
        _used = 0;
        Count = 0;
    }

    internal void WriteDefaultGraph() => Byte(DefaultGraph);

    internal void Write(in RdfTermView term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.TripleTerm:
                Byte(Triple);
                Write(term.Subject);
                Write(term.Predicate);
                Write(term.Object);
                return;
            case RdfTermKind.BlankNode:
                Byte(Blank);
                Field(term.Lexical);
                return;
            case RdfTermKind.Iri:
                Byte(Iri);
                Field(term.Lexical);
                return;
            default:
                Byte(Literal);
                Field(term.Lexical);
                Field(term.HasLanguage || !term.HasDatatype || term.Datatype.SequenceEqual(RdfVocabulary.XsdString) ? default : term.Datatype);
                Field(term.HasLanguage ? term.Language : default);
                Byte((byte)term.Direction);
                return;
        }
    }

    internal void Write(RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.TripleTerm:
                Byte(Triple);
                Write(term.Subject!);
                Write(term.Predicate!);
                Write(term.Object!);
                return;
            case RdfTermKind.BlankNode:
                Byte(Blank);
                Field(term.Lexical);
                return;
            case RdfTermKind.Iri:
                Byte(Iri);
                Field(term.Lexical);
                return;
            default:
                Byte(Literal);
                Field(term.Lexical);
                Field(term.Datatype is null ? default : term.Datatype.Lexical);
                Field(term.Language);
                Byte((byte)term.Direction);
                return;
        }
    }

    /// <summary>Resolves every operation into <paramref name="quads"/>, in order; returns how many.</summary>
    internal int Resolve(BulkLoad load, WorkerCache cache, BulkQuad[] quads)
    {
        for (int i = 0; i < Count; i++)
        {
            int at = _starts[i];
            BulkRef s = Term(load, cache, ref at);
            BulkRef p = Term(load, cache, ref at);
            BulkRef o = Term(load, cache, ref at);
            BulkRef g = Term(load, cache, ref at);
            quads[i] = new BulkQuad(s, p, o, g, _sequences[i]);
        }

        return Count;
    }

    /// <summary>The first operation's first term, resolved: a commit's metadata term.</summary>
    internal BulkRef ResolveFirstTerm(BulkLoad load, WorkerCache cache)
    {
        int at = _starts[0];
        return Term(load, cache, ref at);
    }

    private BulkRef Term(BulkLoad load, WorkerCache cache, ref int at)
    {
        byte kind = _arena[at++];

        switch (kind)
        {
            case DefaultGraph:
                return BulkRef.Of(0);
            case Triple:
                {
                    BulkRef s = Term(load, cache, ref at);
                    BulkRef p = Term(load, cache, ref at);
                    BulkRef o = Term(load, cache, ref at);
                    return load.Triple(s, p, o, cache);
                }

            case Blank:
                {
                    ReadOnlySpan<byte> label = Read(ref at);
                    Span<byte> key = cache.Scratch(label.Length + 1)[..(label.Length + 1)];
                    key[0] = 1;
                    label.CopyTo(key[1..]);
                    return load.Blank(key, cache);
                }

            case Iri:
                {
                    ReadOnlySpan<byte> lexical = Read(ref at);
                    Span<byte> key = cache.Scratch(2 + LogFormat.MaxUlebLength + lexical.Length);
                    key[0] = TermKey.Iri;
                    int length = 1 + Put(key[1..], lexical);
                    return load.Known(key[..length], BulkRef.NewCanonical, cache);
                }

            default:
                {
                    ReadOnlySpan<byte> lexical = Read(ref at);
                    ReadOnlySpan<byte> datatype = Read(ref at);
                    ReadOnlySpan<byte> language = Read(ref at);
                    TextDirection direction = (TextDirection)_arena[at++];

                    if (datatype.Length > 0 && language.Length == 0 && TermIds.TryInline(lexical, datatype, out ulong inline))
                    {
                        return BulkRef.Of(inline);
                    }

                    Span<byte> key = cache.Scratch(2 + (3 * LogFormat.MaxUlebLength) + lexical.Length + datatype.Length + language.Length);
                    key[0] = TermKey.Literal;
                    int length = 1 + Put(key[1..], lexical);
                    length += Put(key[length..], datatype);
                    length += Put(key[length..], language);
                    key[length++] = (byte)direction;
                    TermKey.LowerLanguage(key[..length]);
                    return load.Known(key[..length], BulkRef.NewCanonical, cache);
                }
        }
    }

    // A length written by Field: at most ten bytes of unsigned LEB128.
    private ReadOnlySpan<byte> Read(ref int at)
    {
        ulong length = 0;

        for (int shift = 0; ; shift += 7)
        {
            byte b = _arena[at++];
            length |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        ReadOnlySpan<byte> bytes = _arena.AsSpan(at, (int)length);
        at += (int)length;
        return bytes;
    }

    private static int Put(Span<byte> destination, ReadOnlySpan<byte> bytes)
    {
        int at = LogFormat.Uleb(destination, (ulong)bytes.Length);
        bytes.CopyTo(destination[at..]);
        return at + bytes.Length;
    }

    private void Byte(byte value)
    {
        Ensure(1);
        _arena[_used++] = value;
    }

    private void Field(ReadOnlySpan<byte> bytes)
    {
        Ensure(LogFormat.MaxUlebLength + bytes.Length);
        _used += LogFormat.Uleb(_arena.AsSpan(_used), (ulong)bytes.Length);
        bytes.CopyTo(_arena.AsSpan(_used));
        _used += bytes.Length;
    }

    // A term longer than what is left grows the arena: the buffer is then
    // full, and the next operation goes to a fresh one.
    private void Ensure(int bytes)
    {
        if (_arena.Length - _used < bytes)
        {
            Array.Resize(ref _arena, Math.Max(_arena.Length * 2, _used + bytes));
        }
    }
}

/// <summary>A worker's own cache of the terms it has resolved, and its scratch key (ADR 0107).</summary>
internal sealed class WorkerCache
{
    private readonly Dictionary<byte[], BulkRef> _cache = new(KeyComparer.Instance);
    private readonly Dictionary<byte[], BulkRef>.AlternateLookup<ReadOnlySpan<byte>> _lookup;
    private readonly int _limit;
    private byte[] _scratch = new byte[1024];

    internal WorkerCache(BulkLoad load)
    {
        _limit = load.CacheLimit;
        _lookup = _cache.GetAlternateLookup<ReadOnlySpan<byte>>();
    }

    internal bool TryGet(ReadOnlySpan<byte> key, out BulkRef reference) => _lookup.TryGetValue(key, out reference);

    internal void Set(ReadOnlySpan<byte> key, BulkRef reference)
    {
        if (_cache.Count >= _limit)
        {
            _cache.Clear();
        }

        _lookup[key] = reference;
    }

    /// <summary>A key buffer of at least <paramref name="bytes"/>, the worker's own.</summary>
    internal Span<byte> Scratch(int bytes)
    {
        if (_scratch.Length < bytes)
        {
            _scratch = new byte[bytes * 2];
        }

        return _scratch;
    }

    /// <summary>Byte-array keys compared by content, and looked up by span without a copy.</summary>
    private sealed class KeyComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        internal static KeyComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => (int)TermKey.Hash(obj);

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

        public int GetHashCode(ReadOnlySpan<byte> alternate) => (int)TermKey.Hash(alternate);

        public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();
    }
}

/// <summary>
/// The ring of operation buffers and the workers that resolve and spill
/// them (ADR 0107). The parser takes a free buffer, fills it, and submits
/// it; a worker resolves its terms with a cache of its own, sorts the
/// resolved operations and writes them as a run named by the buffer's
/// sequence number, and returns the buffer to the ring. The parser blocks
/// only when every buffer is busy. A worker's failure is kept and thrown on
/// the parser's next call, or at the commit.
/// </summary>
internal sealed class BulkPipeline
{
    private readonly BulkLoad _load;
    private readonly Channel<OperationBuffer> _free = Channel.CreateUnbounded<OperationBuffer>();
    private readonly Channel<OperationBuffer> _full = Channel.CreateUnbounded<OperationBuffer>(new UnboundedChannelOptions { SingleWriter = true });
    private readonly Task[] _workers;
    private readonly int _operations;
    private ExceptionDispatchInfo? _failure;

    internal BulkPipeline(BulkLoad load, int workers, int ring, int bufferBytes, int bufferOperations)
    {
        _load = load;
        _operations = bufferOperations;

        for (int i = 0; i < ring; i++)
        {
            _free.Writer.TryWrite(new OperationBuffer(bufferBytes, bufferOperations));
        }

        _workers = new Task[workers];

        for (int i = 0; i < workers; i++)
        {
            _workers[i] = Task.Run(WorkAsync);
        }
    }

    /// <summary>A buffer to fill; waits for a worker to free one when every buffer is busy.</summary>
    internal OperationBuffer TakeFree()
    {
        ThrowIfFailed();
        return _free.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Hands a buffer to the workers.</summary>
    internal void Submit(OperationBuffer buffer) => _full.Writer.TryWrite(buffer);

    /// <summary>Rethrows a worker's failure on the parser's thread.</summary>
    internal void ThrowIfFailed() => Volatile.Read(ref _failure)?.Throw();

    /// <summary>No more buffers: every worker finishes what it holds, and a failure is thrown.</summary>
    internal async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        _full.Writer.TryComplete();
        await Task.WhenAll(_workers).WaitAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfFailed();
    }

    /// <summary>The load is abandoned: the workers stop, and what they met is not reported.</summary>
    internal async ValueTask AbandonAsync()
    {
        _full.Writer.TryComplete();

        try
        {
            await Task.WhenAll(_workers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The load's outcome is the caller's disposal; nothing to report.
        }
    }

    /// <summary>Drops the buffers: the input is over and the commit's passes take the memory.</summary>
    internal void Release()
    {
        _free.Writer.TryComplete();

        while (_free.Reader.TryRead(out _))
        {
        }
    }

    private async Task WorkAsync()
    {
        WorkerCache cache = new(_load);
        BulkQuad[] quads = new BulkQuad[_operations];

        await foreach (OperationBuffer buffer in _full.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (_failure is null)
                {
                    int count = buffer.Resolve(_load, cache, quads);
                    await _load.SpillAsync(quads, count, buffer.Sequence, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception failure)
            {
                // The first failure is the one reported; the parser is let
                // through so that it meets it on its next call.
                Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(failure), null);
            }

            buffer.Reset();
            _free.Writer.TryWrite(buffer);
        }
    }
}
