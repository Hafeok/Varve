// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>How a bulk load uses memory and disk (ADR 0081).</summary>
public sealed class BulkLoadOptions
{
    /// <summary>
    /// What the load may hold in memory, beyond the dataset's own state and a
    /// fixed few megabytes of read buffers: the sort buffer, the table of new
    /// terms and the caches are carved from it. 256 MiB by default; at least
    /// 16 MiB.
    /// </summary>
    public ByteCount MemoryBytes { get; init; } = new(256L << 20);

    /// <summary>Test seam: operations per sorted run, in place of what the memory gives.</summary>
    internal int? SortRecords { get; init; }

    /// <summary>Test seam: bytes of new terms per spill, in place of what the memory gives.</summary>
    internal int? TermBytes { get; init; }
}

/// <summary>A bulk load that cannot proceed for a reason in its input, not in the dataset.</summary>
public sealed class BulkLoadException : Exception
{
    /// <summary>Creates one with a message.</summary>
    public BulkLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates one with a message and the error underneath.</summary>
    public BulkLoadException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Creates one with no message.</summary>
    public BulkLoadException()
    {
    }

    internal static BulkLoadException Collision() => new(
        "Two different terms of this load have the same 120-bit content hash. The load is refused rather than "
        + "merging them; load the input in two parts (ADR 0081).");
}

/// <summary>
/// One bulk load: any number of operations, from any parser, committed as
/// one commit (ADRs 0076, 0077, 0081). The dataset's sequencer is held from
/// <see cref="Dataset.BeginBulkLoadAsync"/> until the load commits or is
/// disposed; every other commit waits.
/// </summary>
/// <remarks>
/// <para>
/// **Memory is bounded**, by <see cref="BulkLoadOptions.MemoryBytes"/> and not
/// by the input: operations are sorted outside memory in runs spilled to
/// <c>derived/bulk/</c>, new terms are spilled the same way, and every pass
/// after the input reads its runs a buffer at a time.
/// </para>
/// <para>
/// **Feeding it.** <see cref="Assert(in QuadView)"/> and
/// <see cref="Retract(in QuadView)"/> take a parser's quad as the parser hands
/// it over, so either is a parser's handler as it stands. When the sort buffer
/// fills, the call that filled it writes the buffer out before it returns:
/// the parser's own thread does the disk work, so a host with no threads to
/// block — a browser — cannot bulk-load (ADR 0081).
/// </para>
/// <para>
/// **The commit** computes the effective delta by merging the sorted
/// operations with the pinned state's runs in one sequential pass (ADR 0076),
/// writes it as a disk run, has the dataset's validators read it there
/// (ADR 0077), and appends it as one commit of as many records as it takes.
/// A crash before the last record leaves the dataset where it was.
/// </para>
/// </remarks>
public sealed class BulkLoad : IAsyncDisposable
{
    private readonly SpillSpace _space;
    private readonly TermTable _newTerms;
    private readonly Dictionary<byte[], BulkRef> _cache = new(KeyComparer.Instance);
    private readonly Dictionary<byte[], BulkRef>.AlternateLookup<ReadOnlySpan<byte>> _lookup;
    private ulong _sequence;
    private bool _finished;
    private byte[] _scratch = new byte[1024];
    private readonly int _cacheLimit;

    internal BulkLoad(Dataset dataset, IndexVersion version, TermView terms, long head, IDerivedStore derived, BulkLoadOptions options)
    {
        Dataset = dataset;
        Version = version;
        Terms = terms;
        Head = head;
        Memory = options.MemoryBytes.Value;
        _space = new SpillSpace(derived, head.ToString(CultureInfo.InvariantCulture));
        Quads = new ExternalSort<BulkQuad>(_space, "quads", options.SortRecords ?? (int)Math.Min(int.MaxValue / BulkQuad.Size, Memory * 3 / 8 / BulkQuad.Size));
        _newTerms = new TermTable(options.TermBytes ?? (int)Math.Min(1 << 30, Memory / 4));
        _lookup = _cache.GetAlternateLookup<ReadOnlySpan<byte>>();

        // A quarter of the memory, at about 128 bytes an entry: the key's array and the table's slot.
        _cacheLimit = (int)Math.Clamp(Memory / 4 / 128, 1024, 1 << 24);
    }

    internal Dataset Dataset { get; }

    internal IndexVersion Version { get; }

    internal TermView Terms { get; }

    internal long Head { get; }

    internal long Memory { get; }

    internal ExternalSort<BulkQuad> Quads { get; }

    internal List<BlobName> TermRuns { get; } = [];

    internal bool HasTriples { get; private set; }

    /// <summary>Operations taken so far.</summary>
    public long Operations => (long)(_sequence >> 1);

    /// <summary>Asserts a quad: a parser's handler as it stands.</summary>
    /// <exception cref="BulkLoadException">A triple term is nested deeper than the load can order.</exception>
    public void Assert(in QuadView quad) => Add(in quad, assert: true);

    /// <summary>Retracts a quad: a parser's handler as it stands.</summary>
    public void Retract(in QuadView quad) => Add(in quad, assert: false);

    /// <summary>Asserts a quad given as terms; a null graph is the default graph.</summary>
    public void Assert(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph = null) =>
        Add(Resolve(subject), Resolve(predicate), Resolve(@object), graph is null ? BulkRef.Of(0) : Resolve(graph), assert: true);

    /// <summary>Retracts a quad given as terms; a null graph is the default graph.</summary>
    public void Retract(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph = null) =>
        Add(Resolve(subject), Resolve(predicate), Resolve(@object), graph is null ? BulkRef.Of(0) : Resolve(graph), assert: false);

    private void Add(in QuadView quad, bool assert) =>
        Add(Resolve(quad.Subject), Resolve(quad.Predicate), Resolve(quad.Object), quad.HasGraph ? Resolve(quad.Graph) : BulkRef.Of(0), assert);

    private void Add(BulkRef s, BulkRef p, BulkRef o, BulkRef g, bool assert)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        ulong sequence = _sequence + (assert ? 1UL : 0UL);
        _sequence += 2;

        if (Quads.Add(new BulkQuad(s, p, o, g, sequence)))
        {
            // The parser's thread writes the full buffer before it goes on.
            Quads.SpillAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Terms to references.

    private BulkRef Resolve(RdfTermView term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.TripleTerm:
                return Triple(Resolve(term.Subject), Resolve(term.Predicate), Resolve(term.Object));

            case RdfTermKind.BlankNode:
                return Blank(term.Lexical);

            case RdfTermKind.Iri:
                return Plain(TermKey.Iri, term.Lexical, default, default, TextDirection.None);

            default:
                if (term.HasDatatype && !term.HasLanguage && TermIds.TryInline(term.Lexical, term.Datatype, out ulong inline))
                {
                    return BulkRef.Of(inline);
                }

                ReadOnlySpan<byte> datatype = term.HasLanguage || !term.HasDatatype || term.Datatype.SequenceEqual(RdfVocabulary.XsdString) ? default : term.Datatype;
                return Plain(TermKey.Literal, term.Lexical, datatype, term.HasLanguage ? term.Language : default, term.Direction);
        }
    }

    private BulkRef Resolve(RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.TripleTerm:
                return Triple(Resolve(term.Subject!), Resolve(term.Predicate!), Resolve(term.Object!));

            case RdfTermKind.BlankNode:
                return Blank(term.Lexical);

            case RdfTermKind.Iri:
                return Plain(TermKey.Iri, term.Lexical, default, default, TextDirection.None);

            default:
                if (TermIds.TryInline(term, out ulong inline))
                {
                    return BulkRef.Of(inline);
                }

                return Plain(TermKey.Literal, term.Lexical, term.Datatype is null ? default : term.Datatype.Lexical, term.Language, term.Direction);
        }
    }

    // An IRI or a literal: its key, then the cache, then the dataset's
    // dictionary, and otherwise a new term.
    private BulkRef Plain(byte kind, ReadOnlySpan<byte> lexical, ReadOnlySpan<byte> datatype, ReadOnlySpan<byte> language, TextDirection direction)
    {
        int needed = 2 + (3 * LogFormat.MaxUlebLength) + lexical.Length + datatype.Length + language.Length;

        if (_scratch.Length < needed)
        {
            _scratch = new byte[needed * 2];
        }

        Span<byte> key = _scratch;
        key[0] = kind;
        int at = 1 + Field(key[1..], lexical);

        if (kind == TermKey.Literal)
        {
            at += Field(key[at..], datatype);
            at += Field(key[at..], language);
            key[at++] = (byte)direction;
            TermKey.LowerLanguage(key[..at]);
        }

        return Known(key[..at], BulkRef.NewCanonical);
    }

    private BulkRef Blank(ReadOnlySpan<byte> label)
    {
        if (_scratch.Length < label.Length + 1)
        {
            _scratch = new byte[(label.Length + 1) * 2];
        }

        _scratch[0] = 1;
        label.CopyTo(_scratch.AsSpan(1));
        ReadOnlySpan<byte> key = _scratch.AsSpan(0, label.Length + 1);

        if (_lookup.TryGetValue(key, out BulkRef found))
        {
            return found;
        }

        // A label is scoped to the load: every one is a new node (ADR 0044).
        return Remember(key, BulkRef.New(BulkRef.NewBlank, key), isNew: true);
    }

    private BulkRef Triple(BulkRef s, BulkRef p, BulkRef o)
    {
        if (!s.IsNew && !p.IsNew && !o.IsNew
            && Terms.TryFindTriple(s.Lo, p.Lo, o.Lo, out ulong existing))
        {
            return BulkRef.Of(existing);
        }

        Span<byte> key = stackalloc byte[1 + (3 * BulkRef.Size)];
        key[0] = TermKey.Triple;
        Write(key[1..], s);
        Write(key[17..], p);
        Write(key[33..], o);
        int depth = Math.Max(Depth(s), Math.Max(Depth(p), Depth(o))) + 1;

        if (depth > BulkRef.MaxDepth)
        {
            throw new BulkLoadException("A triple term is nested " + depth + " deep; a bulk load orders at most " + BulkRef.MaxDepth + ".");
        }

        HasTriples = true;
        return Known(key, (byte)(BulkRef.NewCanonical + depth));
    }

    private static int Depth(BulkRef reference) =>
        reference.Segment is > BulkRef.NewCanonical and <= BulkRef.NewCanonical + BulkRef.MaxDepth ? reference.Segment - BulkRef.NewCanonical : 0;

    private static void Write(Span<byte> destination, BulkRef reference)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, reference.Hi);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], reference.Lo);
    }

    private BulkRef Known(ReadOnlySpan<byte> key, byte segment)
    {
        if (_lookup.TryGetValue(key, out BulkRef found))
        {
            return found;
        }

        if (key[0] != TermKey.Triple && TermDictionary.TryFindKey(Version.Runs, key, Terms.CanonicalCount, out ulong id))
        {
            return Remember(key, BulkRef.Of(id), isNew: false);
        }

        return Remember(key, BulkRef.New(segment, key), isNew: true);
    }

    // Each term a batch meets: once in the cache, and once in the table if it is new.
    private BulkRef Remember(ReadOnlySpan<byte> key, BulkRef reference, bool isNew)
    {
        if (_cache.Count >= _cacheLimit)
        {
            _cache.Clear();
        }

        _lookup[key] = reference;

        if (isNew && _newTerms.Add(reference, key))
        {
            BlobName? spilled = _newTerms.SpillAsync(_space, CancellationToken.None).AsTask().GetAwaiter().GetResult();

            if (spilled is { } name)
            {
                TermRuns.Add(name);
            }

            // Every term of the next batch is written to its table again.
            _cache.Clear();
        }

        return reference;
    }

    private static int Field(Span<byte> destination, ReadOnlySpan<byte> bytes)
    {
        int at = LogFormat.Uleb(destination, (ulong)bytes.Length);
        bytes.CopyTo(destination[at..]);
        return at + bytes.Length;
    }

    // -----------------------------------------------------------------------------------------
    // The commit.

    /// <summary>
    /// Computes the load's effective delta against the pinned state, runs the
    /// dataset's validators over it on disk, and commits it as one commit —
    /// or answers <see cref="CommitOutcome.NoChange"/> or
    /// <see cref="CommitOutcome.Rejected"/> and leaves no trace.
    /// </summary>
    /// <exception cref="BulkLoadException">Two different terms of the load hash alike.</exception>
    public async ValueTask<CommitResult> CommitAsync(CommitMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ObjectDisposedException.ThrowIf(_finished, this);

        BulkRef agent = Resolve(metadata.Agent);
        BulkRef cause = Resolve(metadata.Cause);
        BulkRef scope = Resolve(metadata.GraphScope);
        _finished = true;

        try
        {
            BlobName? spilled = await _newTerms.SpillAsync(_space, cancellationToken).ConfigureAwait(false);

            if (spilled is { } name)
            {
                TermRuns.Add(name);
            }

            // The input is over: its table and cache give their memory to the commit's passes.
            _newTerms.Release();
            _cache.Clear();
            _cache.TrimExcess();
            await using BulkCommit commit = new(this, _space, cancellationToken);
            return await commit.RunAsync(agent, cause, scope).ConfigureAwait(false);
        }
        finally
        {
            // The sequencer is released whatever the spill's cleanup does: a
            // cleanup that throws (a crashed disk) must not leave the dataset
            // unable to commit or to close (found by ADR 0101's drain).
            try
            {
                await _space.DeleteAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Dataset.EndBulkLoad();
            }
        }
    }

    private BulkRef Resolve(RequestTerm term)
    {
        if (term.IsNone)
        {
            return BulkRef.Of(0);
        }

        if (term.IsExisting)
        {
            if (!TermDictionary.IsKnown(term.Handle.Value, Terms.CanonicalCount, Terms.BlankCount))
            {
                throw new ArgumentException("The handle " + term.Handle.Value + " was not issued by this dataset at its head (ADR 0044).", nameof(term));
            }

            return BulkRef.Of(term.Handle.Value);
        }

        return Resolve(term.Term!);
    }

    /// <summary>Abandons the load if it has not committed: nothing was written to the log, and its spills are deleted.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        try
        {
            await _space.DeleteAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Dataset.EndBulkLoad();
        }
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
