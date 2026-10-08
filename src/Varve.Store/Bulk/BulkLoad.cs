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

    /// <summary>
    /// The threads that resolve and spill the operations the parser fills
    /// (ADR 0108): the processor count less one by default, at least one.
    /// The parser's own thread is never a worker; one worker is the 6c
    /// pipeline with one buffer of overlap.
    /// </summary>
    public int Workers
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = Math.Max(1, Environment.ProcessorCount - 1);

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
/// it over, so either is a parser's handler as it stands. The parser's thread
/// copies each operation's terms into a buffer; when the buffer fills it is
/// handed to a worker, which resolves the terms — the cache, the dictionary,
/// the table of new terms — sorts the buffer and writes it as a run, while
/// the parser fills the next (ADR 0108). The parser blocks only when every
/// buffer is busy, so a host with no threads to block — a browser — cannot
/// bulk-load (ADR 0081).
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
    private readonly TermTable _newTerms;
    private readonly object _termsLock = new();
    private readonly BulkPipeline _pipeline;
    private OperationBuffer _current;
    private ulong _sequence;
    private bool _finished;

    internal BulkLoad(Dataset dataset, IndexVersion version, TermView terms, long head, IDerivedStore derived, BulkLoadOptions options)
    {
        Dataset = dataset;
        Version = version;
        Terms = terms;
        Head = head;
        Memory = options.MemoryBytes.Value;
        Space = new SpillSpace(derived, head.ToString(CultureInfo.InvariantCulture));

        // The memory (ADR 0081, 0108): three eighths to the operation buffers
        // and the sorted runs the workers make of them, a quarter to the
        // table of new terms, a quarter to the caches, one per worker; the
        // rest is read buffers. The test seam SortRecords bounds a buffer by
        // operations rather than bytes, so that a small load spills many runs.
        int workers = options.Workers;
        int ring = workers + 1;
        long bufferBytes = Math.Max(1 << 16, Memory * 3 / 8 / ring / 2);
        int bufferOperations = options.SortRecords ?? (int)Math.Min(int.MaxValue / 2, bufferBytes / 64);
        Quads = new ExternalSort<BulkQuad>(Space, "quads", 1);
        _newTerms = new TermTable(options.TermBytes ?? (int)Math.Min(1 << 30, Memory / 4));

        // A quarter of the memory over the workers, at about 128 bytes an entry: the key's array and the table's slot.
        CacheLimit = (int)Math.Clamp(Memory / 4 / 128 / workers, 1024, 1 << 24);
        _pipeline = new BulkPipeline(this, workers, ring, (int)Math.Min(int.MaxValue / 2, bufferBytes), bufferOperations);
        _current = _pipeline.TakeFree();
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

    internal SpillSpace Space { get; }

    internal int CacheLimit { get; }

    /// <summary>Asserts a quad: a parser's handler as it stands.</summary>
    /// <exception cref="BulkLoadException">A triple term is nested deeper than the load can order, or a worker refused the load.</exception>
    public void Assert(in QuadView quad) => Add(in quad, assert: true);

    /// <summary>Retracts a quad: a parser's handler as it stands.</summary>
    public void Retract(in QuadView quad) => Add(in quad, assert: false);

    /// <summary>Asserts a quad given as terms; a null graph is the default graph.</summary>
    public void Assert(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph = null) => Add(subject, predicate, @object, graph, assert: true);

    /// <summary>Retracts a quad given as terms; a null graph is the default graph.</summary>
    public void Retract(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph = null) => Add(subject, predicate, @object, graph, assert: false);

    private void Add(in QuadView quad, bool assert)
    {
        Begin();
        _current.Begin(Next(assert));
        _current.Write(quad.Subject);
        _current.Write(quad.Predicate);
        _current.Write(quad.Object);

        if (quad.HasGraph)
        {
            _current.Write(quad.Graph);
        }
        else
        {
            _current.WriteDefaultGraph();
        }

        End();
    }

    private void Add(RdfTerm subject, RdfTerm predicate, RdfTerm @object, RdfTerm? graph, bool assert)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(@object);
        Begin();
        _current.Begin(Next(assert));
        _current.Write(subject);
        _current.Write(predicate);
        _current.Write(@object);

        if (graph is null)
        {
            _current.WriteDefaultGraph();
        }
        else
        {
            _current.Write(graph);
        }

        End();
    }

    private void Begin()
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _pipeline.ThrowIfFailed();
    }

    private ulong Next(bool assert)
    {
        ulong sequence = _sequence + (assert ? 1UL : 0UL);
        _sequence += 2;
        return sequence;
    }

    // The buffer that filled goes to a worker; the parser takes a free one,
    // waiting when every buffer is busy (ADR 0108).
    private void End()
    {
        if (_current.IsFull)
        {
            _pipeline.Submit(_current);
            _current = _pipeline.TakeFree();
        }
    }

    // -----------------------------------------------------------------------------------------
    // Terms to references: what a worker does with a buffer.

    /// <summary>A term the dictionary knows, or a new one for the table, decided by a worker with its own cache.</summary>
    internal BulkRef Known(ReadOnlySpan<byte> key, byte segment, WorkerCache cache)
    {
        if (cache.TryGet(key, out BulkRef found))
        {
            return found;
        }

        if (key[0] != TermKey.Triple && TermDictionary.TryFindKey(Version.Runs, key, Terms.CanonicalCount, out ulong id))
        {
            return Remember(key, BulkRef.Of(id), isNew: false, cache);
        }

        return Remember(key, BulkRef.New(segment, key), isNew: true, cache);
    }

    /// <summary>A blank node: its label is scoped to the load, so every one is a new node (ADR 0044).</summary>
    internal BulkRef Blank(ReadOnlySpan<byte> key, WorkerCache cache) =>
        cache.TryGet(key, out BulkRef found) ? found : Remember(key, BulkRef.New(BulkRef.NewBlank, key), isNew: true, cache);

    /// <summary>A triple term over resolved components: the dataset's when it has one, a new one otherwise.</summary>
    internal BulkRef Triple(BulkRef s, BulkRef p, BulkRef o, WorkerCache cache)
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
        return Known(key, (byte)(BulkRef.NewCanonical + depth), cache);
    }

    private static int Depth(BulkRef reference) =>
        reference.Segment is > BulkRef.NewCanonical and <= BulkRef.NewCanonical + BulkRef.MaxDepth ? reference.Segment - BulkRef.NewCanonical : 0;

    private static void Write(Span<byte> destination, BulkRef reference)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, reference.Hi);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], reference.Lo);
    }

    // Each term a worker meets: once in its cache, and once in the shared
    // table if it is new. The table is the one structure every worker
    // writes, so it is written under a lock (ADR 0108); a spill happens
    // inside it, and the other workers wait for the write.
    private BulkRef Remember(ReadOnlySpan<byte> key, BulkRef reference, bool isNew, WorkerCache cache)
    {
        cache.Set(key, reference);

        if (!isNew)
        {
            return reference;
        }

        lock (_termsLock)
        {
            if (_newTerms.Add(reference, key))
            {
                // A term met again after the spill stays in its worker's
                // cache and is not written again: the merge of the term runs
                // takes a term that two spills hold once (TermMerge).
                BlobName? spilled = _newTerms.SpillAsync(Space, CancellationToken.None).AsTask().GetAwaiter().GetResult();

                if (spilled is { } name)
                {
                    TermRuns.Add(name);
                }
            }
        }

        return reference;
    }

    /// <summary>A resolved buffer, sorted and written as a run by its worker.</summary>
    internal async ValueTask SpillAsync(BulkQuad[] quads, int count, ulong sequence, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return;
        }

        quads.AsSpan(0, count).Sort();
        BlobName name = Space.Named("quads", sequence);
        await ExternalSort<BulkQuad>.WriteAsync(Space.Store, name, quads.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        Quads.Adopt(name, count);
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

        _pipeline.ThrowIfFailed();
        _finished = true;

        try
        {
            // The last buffer goes to the workers, and every worker finishes
            // before the metadata's terms are resolved on this thread.
            _pipeline.Submit(_current);
            await _pipeline.CompleteAsync(cancellationToken).ConfigureAwait(false);
            WorkerCache cache = new(this);
            BulkRef agent = Resolve(metadata.Agent, cache);
            BulkRef cause = Resolve(metadata.Cause, cache);
            BulkRef scope = Resolve(metadata.GraphScope, cache);
            BlobName? spilled = await _newTerms.SpillAsync(Space, cancellationToken).ConfigureAwait(false);

            if (spilled is { } name)
            {
                TermRuns.Add(name);
            }

            // The input is over: its table and caches give their memory to the commit's passes.
            _newTerms.Release();
            _pipeline.Release();
            await using BulkCommit commit = new(this, Space, cancellationToken);
            return await commit.RunAsync(agent, cause, scope).ConfigureAwait(false);
        }
        finally
        {
            // The sequencer is released whatever the spill's cleanup does: a
            // cleanup that throws (a crashed disk) must not leave the dataset
            // unable to commit or to close (found by ADR 0101's drain).
            try
            {
                await Space.DeleteAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Dataset.EndBulkLoad();
            }
        }
    }

    private BulkRef Resolve(RequestTerm term, WorkerCache cache)
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

        OperationBuffer one = new(1 << 12, 1);
        one.Begin(0);
        one.Write(term.Term!);
        return one.ResolveFirstTerm(this, cache);
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
            await _pipeline.AbandonAsync().ConfigureAwait(false);
            await Space.DeleteAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Dataset.EndBulkLoad();
        }
    }
}
