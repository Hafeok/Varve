// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.Win32.SafeHandles;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// <c>derived/</c> for the object-storage backend (ADR 0139): extents and the
/// catalog in the store, where followers and the next writer find them;
/// everything else — runs, the projection's state, the commit index — in the
/// local cache, as derived data any instance rebuilds.
/// </summary>
/// <remarks>
/// <para>
/// **Extents are read through a page cache.** Opening one reads its first
/// and last pages, which hold what a load reads; any other page is fetched by
/// a ranged read when a read first needs it. The cache holds at most its
/// configured bytes and evicts the least recently read extent's pages, which
/// are fetched again when next read: eviction never changes a result.
/// </para>
/// <para>
/// **The catalog is fenced by the epoch.** It is stored behind a wrapper
/// that carries the writer's epoch, and replaced only against the version
/// this writer last read or wrote. A writer's claim rewrites it, so a
/// superseded writer's next swap fails with <see cref="WriterSupersededException"/>.
/// </para>
/// </remarks>
internal sealed class ObjectDerivedStore : IDerivedStore, IDerivedStoreLimits, IDisposable
{
    internal const int PageBytes = 1 << 20;

    private const string ExtentPrefix = "extents/";
    private const string CatalogName = "catalog";
    private const int WrapperLength = 12;

    private static ReadOnlySpan<byte> WrapperMagic => "VRVK"u8;

    private readonly IObjectStore _store;
    private readonly ObjectNames _names;
    private readonly TimeProvider _clock;
    private readonly IDerivedStore _local;
    private readonly string _pages;
    private readonly string _staging;
    private readonly long _cacheBytes;
    private readonly long _maxObjectBytes;
    private readonly ConcurrentDictionary<string, PagedObject> _open = new(StringComparer.Ordinal);
    private ObjectVersion? _catalogVersion;
    private long _epoch;
    private long _cached;
    private long _recency;
    private long _files;

    internal ObjectDerivedStore(IObjectStore store, ObjectNames names, TimeProvider clock, IDerivedStore local, string pages, string staging, long cacheBytes, long maxObjectBytes)
    {
        _store = store;
        _names = names;
        _clock = clock;
        _local = local;
        _pages = pages;
        _staging = staging;
        _cacheBytes = cacheBytes;
        _maxObjectBytes = maxObjectBytes;

        // Pages and staged uploads are this process's: a file left by another is not trusted.
        foreach (string directory in (string[])[pages, staging])
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);
        }
    }

    /// <inheritdoc />
    public ByteCount MaxBlobBytes => new(_maxObjectBytes);

    /// <summary>
    /// What a read that finds an extent gone throws (ADR 0160): the storage
    /// decides, from its reader record, whether it was forgotten, reads
    /// unannounced, or found damage.
    /// </summary>
    internal Func<ObjectName, Exception> Gone { get; set; } = name => new IOException("The extent '" + name + "' is gone from the store; a catalog names it.");

    /// <summary>Whether this store holds the epoch's catalog: the writer's, which alone writes and deletes extents.</summary>
    internal bool Writes => _catalogVersion is not null && !CatalogSuperseded;

    /// <summary>Whether a claim found the catalog under a newer epoch than its own.</summary>
    internal bool CatalogSuperseded { get; private set; }

    /// <summary>The bytes the page cache holds now.</summary>
    internal long CachedBytes => Interlocked.Read(ref _cached);

    private static bool Shared(BlobName name) => name.Value.StartsWith(ExtentPrefix, StringComparison.Ordinal) || name.Value == CatalogName;

    /// <summary>
    /// After a claim of <paramref name="epoch"/>: rewrites the catalog under it,
    /// or writes an empty one, so the version every older writer holds is
    /// stale. False when the catalog is already under a newer epoch.
    /// </summary>
    [DesignDecision(typeof(ExtentsAndTheProjectionCatalog.CatalogUnderTheEpochFence), Scope = ExceptionScope.Boundary)]
    internal async ValueTask<bool> FenceCatalogAsync(long epoch, CancellationToken cancellationToken)
    {
        _epoch = epoch;
        ObjectName name = _names.Derived(CatalogName);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            ObjectRead? read = await ConditionalWrites.ReadWholeAsync(_store, name, cancellationToken).ConfigureAwait(false);

            if (read is null)
            {
                byte[] empty = Wrap(epoch, ReadOnlySpan<byte>.Empty);
                (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_store, name, empty, _clock, cancellationToken).ConfigureAwait(false);

                if (outcome == CreateOutcome.Ours && await ConditionalWrites.ReadWholeAsync(_store, name, cancellationToken).ConfigureAwait(false) is { } created && created.Bytes.Span.SequenceEqual(empty))
                {
                    _catalogVersion = created.Version;
                    return true;
                }

                continue;
            }

            (long held, ReadOnlyMemory<byte> payload) = Unwrap(read.Bytes);

            if (held > epoch)
            {
                CatalogSuperseded = true;
                return false;
            }

            ObjectVersion? version = await ConditionalWrites.ReplaceAsync(_store, name, read.Version, Wrap(epoch, payload.Span), _clock, cancellationToken).ConfigureAwait(false);

            if (version is not null)
            {
                _catalogVersion = version;
                return true;
            }
        }

        throw new IOException("The catalog changed under every attempt to fence it; the claim is not complete.");
    }

    /// <summary>
    /// A blob of <c>derived/</c>. Extents and the catalog are written only by
    /// the epoch's writer: a read-only or superseded storage is refused before
    /// anything reaches the store.
    /// </summary>
    public async ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken)
    {
        if (!Shared(name))
        {
            return await _local.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return !Writes
            ? throw new WriterSupersededException("This storage holds no epoch: extents and the catalog are written only by the dataset's writer (ADR 0139).")
            : new ObjectWriter(this, name, Path.Combine(_staging, Interlocked.Increment(ref _files).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public async ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
    {
        if (!Shared(name))
        {
            return await _local.OpenAsync(name, cancellationToken).ConfigureAwait(false);
        }

        if (name.Value == CatalogName)
        {
            ObjectRead? read = await ConditionalWrites.ReadWholeAsync(_store, _names.Derived(CatalogName), cancellationToken).ConfigureAwait(false);
            (_, ReadOnlyMemory<byte> payload) = read is null ? (0, ReadOnlyMemory<byte>.Empty) : Unwrap(read.Bytes);

            return payload.IsEmpty ? throw new KeyNotFoundException("No catalog.") : new BytesBlob(payload);
        }

        while (true)
        {
            if (_open.TryGetValue(name.Value, out PagedObject? held))
            {
                if (held.TryAcquire())
                {
                    return new PagedBlob(held);
                }

                // Let go of since it was looked up: no longer the cache's.
                _open.TryRemove(new KeyValuePair<string, PagedObject>(name.Value, held));
                continue;
            }

            PagedObject? fetched;

            try
            {
                fetched = await PagedObject.OpenAsync(this, name, cancellationToken).ConfigureAwait(false);
            }
            catch (ExtentGoneException)
            {
                // Deleted between its first page and its last: as if absent.
                fetched = null;
            }

            if (fetched is null)
            {
                throw new KeyNotFoundException("No extent '" + name + "'.");
            }

            // The table holds one reference and the open another.
            if (fetched.TryAcquire() && _open.TryAdd(name.Value, fetched))
            {
                Evict(fetched);
                return new PagedBlob(fetched);
            }

            fetched.Release();
            fetched.Release();
        }
    }

    /// <summary>
    /// A blob under <c>derived/</c>. An extent is deleted from the store only
    /// by the writer, which asks only once the deletion rule of reader
    /// announcements allows it (ADR 0160); anywhere else its pages are dropped.
    /// </summary>
    [DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.DeletionWaitsForExtentMaintenance), Scope = ExceptionScope.Boundary)]
    public async ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken)
    {
        if (!Shared(name))
        {
            return await _local.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }

        if (_open.TryRemove(name.Value, out PagedObject? held))
        {
            held.Release();
        }

        return Writes && name.Value != CatalogName && await _store.DeleteAsync(_names.Derived(name.Value), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken)
    {
        List<BlobName> names = [.. await _local.ListAsync(cancellationToken).ConfigureAwait(false)];
        string start = _names.Derived(string.Empty).Value;

        foreach (ObjectEntry entry in await _store.ListAsync(_names.Derived(string.Empty), cancellationToken).ConfigureAwait(false))
        {
            string name = entry.Name.Value[start.Length..];

            // An empty catalog — the wrapper alone — stands for none.
            if (name.StartsWith(ExtentPrefix, StringComparison.Ordinal) || (name == CatalogName && entry.Length.Value > WrapperLength))
            {
                names.Add(new BlobName(name));
            }
        }

        return names;
    }

    public void Dispose()
    {
        foreach (PagedObject held in _open.Values)
        {
            held.Release();
        }

        _open.Clear();
    }

    private static byte[] Wrap(long epoch, ReadOnlySpan<byte> payload)
    {
        byte[] bytes = new byte[WrapperLength + payload.Length];
        WrapperMagic.CopyTo(bytes);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), epoch);
        payload.CopyTo(bytes.AsSpan(WrapperLength));
        return bytes;
    }

    private static (long Epoch, ReadOnlyMemory<byte> Payload) Unwrap(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length < WrapperLength || !bytes.Span[..4].SequenceEqual(WrapperMagic))
        {
            throw new IOException("The catalog object is not wrapped by an epoch; it was not written by this backend.");
        }

        return (BinaryPrimitives.ReadInt64LittleEndian(bytes.Span[4..]), bytes[WrapperLength..]);
    }

    // Publishes the catalog by a replace against the version this writer
    // holds (ADR 0139); an extent is published from its staged file.
    private async ValueTask PublishCatalogAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (_catalogVersion is not { } expected || CatalogSuperseded)
        {
            throw new WriterSupersededException("This storage holds no epoch's catalog: it is read-only, or superseded.");
        }

        ObjectVersion? version = await ConditionalWrites.ReplaceAsync(_store, _names.Derived(CatalogName), expected, Wrap(_epoch, bytes.Span), _clock, cancellationToken).ConfigureAwait(false);

        if (version is null)
        {
            _catalogVersion = null;
            CatalogSuperseded = true;
            throw new WriterSupersededException("The catalog was replaced by a newer writer; this writer's swap is refused (ADR 0136).");
        }

        _catalogVersion = version;
    }

    [DesignDecision(typeof(BackgroundExtentMerges.UploadsStagedThroughTheCache), Scope = ExceptionScope.Boundary)]
    private async ValueTask PublishStagedAsync(BlobName name, string staged, CancellationToken cancellationToken)
    {
        ObjectContent content = new(new FileInfo(staged));

        if (content.Length.Value > _maxObjectBytes)
        {
            throw new IOException("The extent '" + name + "' is " + content.Length + " bytes, more than one upload to this store may hold (" + _maxObjectBytes + "); multipart upload is not supported (ADR 0158).");
        }

        if (!Writes)
        {
            throw new WriterSupersededException("This storage holds no epoch's catalog: it is read-only, or superseded.");
        }

        CreateOutcome outcome = await ConditionalWrites.CreateAsync(_store, _names.Derived(name.Value), content, _clock, cancellationToken).ConfigureAwait(false);

        if (outcome == CreateOutcome.Taken)
        {
            // A superseded writer's: names are never reused, so the set is
            // written again under the next sequence (ADR 0158).
            throw new DerivedNameTakenException("The extent '" + name + "' already exists with other bytes; an extent's name is never reused.");
        }
    }

    // Brings the cache under its bound: the least recently read extents'
    // pages go first, never those of the one just read.
    [DesignDecision(typeof(ExtentsAndTheProjectionCatalog.EvictionNeverChangesAResult), Scope = ExceptionScope.HotPath)]
    private void Evict(PagedObject keep)
    {
        while (Interlocked.Read(ref _cached) > _cacheBytes)
        {
            PagedObject? coldest = null;

            foreach (PagedObject candidate in _open.Values)
            {
                if (candidate != keep && candidate.Fetched > 0 && (coldest is null || candidate.LastRead < coldest.LastRead))
                {
                    coldest = candidate;
                }
            }

            if (coldest is null || !coldest.TryDropPages())
            {
                return;
            }
        }
    }

    private long NextRecency() => Interlocked.Increment(ref _recency);

    /// <summary>
    /// A blob under <c>derived/</c> in the store, staged in a file of the
    /// cache and published from it (ADR 0158): an extent is uploaded streamed
    /// and its file becomes its page cache file, every page held; the catalog,
    /// small, is replaced from memory.
    /// </summary>
    private sealed class ObjectWriter(ObjectDerivedStore owner, BlobName name, string staged) : IBlobWriter
    {
        private FileStream? _file = new(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1 << 16);

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_file is null, this);
            await _file.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask PublishAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_file is null, this);
            await _file.FlushAsync(cancellationToken).ConfigureAwait(false);
            long length = _file.Length;
            await _file.DisposeAsync().ConfigureAwait(false);
            _file = null;

            if (name.Value == CatalogName)
            {
                byte[] bytes = await File.ReadAllBytesAsync(staged, cancellationToken).ConfigureAwait(false);
                File.Delete(staged);
                await owner.PublishCatalogAsync(bytes, cancellationToken).ConfigureAwait(false);
                return;
            }

            await owner.PublishStagedAsync(name, staged, cancellationToken).ConfigureAwait(false);

            // An extent just written is in the cache already: its staged file.
            if (!owner._open.ContainsKey(name.Value))
            {
                PagedObject written = PagedObject.Adopt(owner, name, staged, length);

                if (owner._open.TryAdd(name.Value, written))
                {
                    owner.Evict(written);
                    return;
                }

                written.Release();
                return;
            }

            File.Delete(staged);
        }

        public async ValueTask DisposeAsync()
        {
            if (_file is not null)
            {
                await _file.DisposeAsync().ConfigureAwait(false);
                _file = null;
            }

            if (File.Exists(staged))
            {
                File.Delete(staged);
            }
        }
    }

    /// <summary>The catalog's payload, held in memory.</summary>
    private sealed class BytesBlob(ReadOnlyMemory<byte> bytes) : IReadableBlob
    {
        public ByteCount Length => new(bytes.Length);

        public int Read(ByteOffset offset, Span<byte> destination)
        {
            if (offset.Value >= bytes.Length)
            {
                return 0;
            }

            int count = (int)Math.Min(destination.Length, bytes.Length - offset.Value);
            bytes.Span.Slice((int)offset.Value, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>One open of a paged extent; disposing it lets the extent go.</summary>
    private sealed class PagedBlob(PagedObject paged) : IReadableBlob
    {
        private int _disposed;

        public ByteCount Length => new(paged.Length);

        [DesignDecision(typeof(ExtentsAndTheProjectionCatalog.ExtentPagesFetchedOnAMiss), Scope = ExceptionScope.HotPath)]
        public int Read(ByteOffset offset, Span<byte> destination) => paged.Read(offset.Value, destination);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                paged.Release();
            }
        }
    }

    /// <summary>
    /// An extent in the page cache: a sparse local file the length of the
    /// object, and which of its pages it holds. Held by the store's table and
    /// by every open; the file is deleted when the last lets go.
    /// </summary>
    private sealed class PagedObject
    {
        private readonly ObjectDerivedStore _owner;
        private readonly ObjectName _object;
        private readonly SafeFileHandle _file;
        private readonly string _path;
        private readonly bool[] _present;
        private readonly Lock _lock = new();
        private int _references = 1;

        private PagedObject(ObjectDerivedStore owner, BlobName name, long length, string? adopted = null)
        {
            _owner = owner;
            _object = owner._names.Derived(name.Value);
            Length = length;
            _present = new bool[(int)((length + PageBytes - 1) / PageBytes)];
            _path = Path.Combine(owner._pages, Interlocked.Increment(ref owner._files).ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (adopted is not null)
            {
                File.Move(adopted, _path);
                _file = File.OpenHandle(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            else
            {
                _file = File.OpenHandle(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, FileOptions.None, length);
            }

            LastRead = owner.NextRecency();
        }

        internal long Length { get; }

        /// <summary>The bytes of the pages held.</summary>
        internal long Fetched { get; private set; }

        internal long LastRead { get; private set; }

        /// <summary>An extent just written: its staged file becomes the page file, every page held.</summary>
        internal static PagedObject Adopt(ObjectDerivedStore owner, BlobName name, string staged, long length)
        {
            PagedObject paged = new(owner, name, length, staged);

            lock (paged._lock)
            {
                Array.Fill(paged._present, true);
                paged.Fetched = length;
                Interlocked.Add(ref owner._cached, length);
            }

            return paged;
        }

        /// <summary>An extent as the store holds it, its first and last pages read; null when there is none.</summary>
        internal static async ValueTask<PagedObject?> OpenAsync(ObjectDerivedStore owner, BlobName name, CancellationToken cancellationToken)
        {
            ObjectName objectName = owner._names.Derived(name.Value);
            ObjectRead? first = await owner._store.ReadAsync(objectName, new ByteOffset(0), new ByteCount(PageBytes), cancellationToken).ConfigureAwait(false);

            if (first is null)
            {
                return null;
            }

            PagedObject paged = new(owner, name, first.Length.Value);
            int last = paged._present.Length - 1;
            ReadOnlyMemory<byte> tail = last <= 0 ? ReadOnlyMemory<byte>.Empty
                : await paged.FetchAsync(last, cancellationToken).ConfigureAwait(false);

            lock (paged._lock)
            {
                if (paged._present.Length > 0 && first.Bytes.Length == paged.Page(0).Count)
                {
                    paged.Hold(0, first.Bytes.Span);
                }

                if (last > 0)
                {
                    paged.Hold(last, tail.Span);
                }
            }

            return paged;
        }

        internal bool TryAcquire()
        {
            while (true)
            {
                int current = Volatile.Read(ref _references);

                if (current == 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        internal void Release()
        {
            if (Interlocked.Decrement(ref _references) == 0)
            {
                lock (_lock)
                {
                    Interlocked.Add(ref _owner._cached, -Fetched);
                    Fetched = 0;
                    _file.Dispose();
                    File.Delete(_path);
                }
            }
        }

        /// <summary>Copies bytes from the cache, fetching the pages it does not hold first.</summary>
        [DesignDecision(typeof(ExtentsAndTheProjectionCatalog.ExtentPagesFetchedOnAMiss), Scope = ExceptionScope.HotPath)]
        internal int Read(long offset, Span<byte> destination)
        {
            if (offset >= Length)
            {
                return 0;
            }

            int count = (int)Math.Min(destination.Length, Length - offset);
            int firstPage = (int)(offset / PageBytes);
            int lastPage = (int)((offset + count - 1) / PageBytes);
            int read;

            lock (_lock)
            {
                for (int page = firstPage; page <= lastPage; page++)
                {
                    if (!_present[page])
                    {
                        // The read is synchronous (ADR 0071); the miss waits for the store.
                        ReadOnlyMemory<byte> bytes;

                        try
                        {
                            bytes = FetchAsync(page, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                        }
                        catch (ExtentGoneException)
                        {
                            throw _owner.Gone(_object);
                        }

                        Hold(page, bytes.Span);
                    }
                }

                read = RandomAccess.Read(_file, destination[..count], offset);
                LastRead = _owner.NextRecency();
            }

            _owner.Evict(this);
            return read;
        }

        /// <summary>Drops every page, unless a read holds the extent now; the pages are fetched again when next read.</summary>
        internal bool TryDropPages()
        {
            if (!_lock.TryEnter())
            {
                return false;
            }

            try
            {
                if (Fetched == 0 || Volatile.Read(ref _references) == 0)
                {
                    return false;
                }

                Array.Clear(_present);
                RandomAccess.SetLength(_file, 0);
                RandomAccess.SetLength(_file, Length);
                Interlocked.Add(ref _owner._cached, -Fetched);
                Fetched = 0;
                return true;
            }
            finally
            {
                _lock.Exit();
            }
        }

        private (long Start, int Count) Page(int page)
        {
            long start = (long)page * PageBytes;
            return (start, (int)Math.Min(PageBytes, Length - start));
        }

        // Called under the lock.
        private void Hold(int page, ReadOnlySpan<byte> bytes)
        {
            (long start, int count) = Page(page);

            if (bytes.Length != count)
            {
                throw new IOException("A page of '" + _object + "' came back " + bytes.Length + " bytes long where " + count + " were due.");
            }

            RandomAccess.Write(_file, bytes, start);
            _present[page] = true;
            Fetched += count;
            Interlocked.Add(ref _owner._cached, count);
        }

        // One page by ranged reads, retried on a transient failure: an extent
        // is never replaced, so every read is of the same bytes.
        private async ValueTask<ReadOnlyMemory<byte>> FetchAsync(int page, CancellationToken cancellationToken)
        {
            (long start, int count) = Page(page);
            byte[] bytes = new byte[count];
            int filled = 0;
            Exception? last = null;

            for (int attempt = 0; attempt < 6 && filled < count; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50 << attempt), _owner._clock, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    while (filled < count)
                    {
                        ObjectRead read = await _owner._store.ReadAsync(_object, new ByteOffset(start + filled), new ByteCount(count - filled), cancellationToken).ConfigureAwait(false)
                            ?? throw new ExtentGoneException("The extent '" + _object + "' is gone from the store; a catalog names it.");

                        if (read.Bytes.IsEmpty)
                        {
                            throw new IOException("The extent '" + _object + "' is shorter than when it was opened.");
                        }

                        read.Bytes.Span.CopyTo(bytes.AsSpan(filled));
                        filled += read.Bytes.Length;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error) when (error is not ExtentGoneException)
                {
                    last = error;
                }
            }

            return filled == count ? bytes : throw new IOException("Reading a page of '" + _object + "' failed six times.", last);
        }
    }

    /// <summary>An extent a catalog names is not in the store: retrying cannot help.</summary>
    private sealed class ExtentGoneException(string message) : IOException(message);
}
