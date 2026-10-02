// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>How a <see cref="FileStorage"/> is opened.</summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// The clock the lease's diagnostic timestamp is read from. Required: the
    /// store never reads the machine's clock itself (ADR 0011).
    /// </summary>
    public required TimeProvider Clock { get; init; }

    /// <summary>
    /// Where the dataset's key store lives, when it has one. Checked at open
    /// and refused when inside the dataset directory (specification §2, §9;
    /// ADR 0074). No key store exists before erasure mode (milestone 9); this
    /// option is checked and otherwise unused until then.
    /// </summary>
    public KeyStoreDirectory? KeyStore { get; init; }
}

/// <summary>
/// Storage in a dataset directory of plain files (specification §2):
/// <c>log/</c>, the source of truth, and <c>derived/</c>, which may be dropped.
/// Its durability is <see cref="Durability.Synchronised"/> (ADR 0073).
/// </summary>
/// <remarks>
/// <para>
/// **One process at a time.** Opening takes <c>derived/LOCK</c> with an
/// exclusive handle that the operating system releases when the process
/// ends, so a stale lease never exists; a second opener, in this process or
/// another, is refused with <see cref="DatasetLeasedException"/> (ADR 0075).
/// <see cref="DisposeAsync"/> releases it.
/// </para>
/// <para>
/// The bytes under <c>log/</c> are the store's, in format version 1 (ADR
/// 0072); this type keeps them. Segments are <c>log/NNNNNNNN.seg</c>, sealed
/// segments read-only; the manifest is <c>log/MANIFEST</c>; derived blobs are
/// files under <c>derived/</c>, published by a temporary file, a flush and a
/// rename. <c>derived/.gitignore</c> is written when the directory is first
/// opened, so a repository never carries derived data.
/// </para>
/// </remarks>
public sealed class FileStorage : IStorage, IAsyncDisposable
{
    private const string LogDirectory = "log";
    private const string DerivedDirectory = "derived";
    private const string ManifestName = "MANIFEST";
    private const string SegmentSuffix = ".seg";
    private const string TemporarySuffix = ".tmp";
    private const string LockName = "LOCK";
    private const string OwnerName = "LOCK.owner";
    private const string IgnoreName = ".gitignore";

    private readonly IFileSystem _files;
    private readonly IDisposable _lease;
    private readonly FileSegmentStore _log;
    private readonly FileDerivedStore _derived;

    private FileStorage(IFileSystem files, DatasetDirectory directory, IDisposable lease)
    {
        _files = files;
        _lease = lease;
        Directory = directory;
        _log = new FileSegmentStore(files, Path.Combine(directory.Value, LogDirectory));
        _derived = new FileDerivedStore(files, Path.Combine(directory.Value, DerivedDirectory));
    }

    /// <summary>The dataset directory.</summary>
    public DatasetDirectory Directory { get; }

    /// <inheritdoc />
    public ISegmentStore Log => _log;

    /// <inheritdoc />
    public IDerivedStore Derived => _derived;

    /// <summary>
    /// Opens the dataset directory, creating <c>log/</c>, <c>derived/</c> and
    /// <c>derived/.gitignore</c> when they are absent, and takes the lease.
    /// Unpublished derived writes left by a crash are deleted.
    /// </summary>
    /// <exception cref="DatasetLeasedException">The directory is open elsewhere.</exception>
    /// <exception cref="ArgumentException">The key store is inside the dataset directory.</exception>
    public static ValueTask<FileStorage> OpenAsync(DatasetDirectory directory, FileStorageOptions options, CancellationToken cancellationToken = default) =>
        new(Open(new DiskFileSystem(), directory, options));

    [DesignDecision(typeof(StorageAbstraction.NoKeysInDatasetDirectory), Scope = ExceptionScope.Boundary)]
    internal static FileStorage Open(IFileSystem files, DatasetDirectory directory, FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Clock);
        ArgumentException.ThrowIfNullOrEmpty(directory.Value, nameof(directory));

        if (options.KeyStore is { } keyStore && keyStore.IsWithin(directory))
        {
            throw new ArgumentException(
                "The key store '" + keyStore + "' is inside the dataset directory '" + directory + "'. Keys never live in the dataset directory, which is copied, backed up and checked in by tools the store cannot reach (specification §2, §9).",
                nameof(options));
        }

        string log = Path.Combine(directory.Value, LogDirectory);
        string derived = Path.Combine(directory.Value, DerivedDirectory);
        files.CreateDirectory(log);
        files.CreateDirectory(derived);

        IDisposable lease = files.TryLock(Path.Combine(derived, LockName))
            ?? throw new DatasetLeasedException(directory, ReadOwner(files, Path.Combine(derived, OwnerName)));

        try
        {
            WriteSmallFile(files, Path.Combine(derived, OwnerName), Owner(options.Clock));
            string ignore = Path.Combine(derived, IgnoreName);

            if (!files.FileExists(ignore))
            {
                WriteSmallFile(files, ignore, "*\n");
            }

            FileStorage storage = new(files, directory, lease);
            storage._derived.DeleteUnpublished();
            return storage;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Releases the lease and every open file.</summary>
    public ValueTask DisposeAsync()
    {
        _log.Dispose();
        _derived.Dispose();
        _lease.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string Owner(TimeProvider clock) =>
        "process " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
        + " on " + Environment.MachineName
        + ", since " + clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private static string? ReadOwner(IFileSystem files, string path)
    {
        try
        {
            if (!files.FileExists(path))
            {
                return null;
            }

            using IFileHandle handle = files.Open(path, FileOpen.Read);
            byte[] bytes = new byte[Math.Min(handle.Length, 1024)];
            return Encoding.UTF8.GetString(bytes, 0, handle.Read(0, bytes));
        }
        catch (IOException)
        {
            return null;
        }
    }

    // A small file replaced whole: written beside, flushed, renamed (ADR 0073).
    private static void WriteSmallFile(IFileSystem files, string path, string text)
    {
        string temporary = path + TemporarySuffix;

        if (files.FileExists(temporary))
        {
            files.Delete(temporary);
        }

        using (IFileHandle handle = files.Open(temporary, FileOpen.CreateNew))
        {
            handle.WriteAsync(0, Encoding.UTF8.GetBytes(text), CancellationToken.None).AsTask().GetAwaiter().GetResult();
            handle.Flush();
        }

        files.Move(temporary, path);
    }

    /// <summary>The log's segments as files: <c>log/NNNNNNNN.seg</c> and <c>log/MANIFEST</c>.</summary>
    private sealed class FileSegmentStore : ISegmentStore, IDisposable
    {
        private readonly IFileSystem _files;
        private readonly string _directory;
        private readonly Lock _gate = new();
        private readonly SortedDictionary<int, IFileHandle> _readers = [];
        private readonly List<int> _segments;
        private IFileHandle? _active;
        private int _activeId = -1;
        private long _activeLength;

        public FileSegmentStore(IFileSystem files, string directory)
        {
            _files = files;
            _directory = directory;
            _segments = [];

            foreach (string file in files.Files(directory))
            {
                string name = Path.GetFileName(file);

                if (Path.GetDirectoryName(file) == directory
                    && name.Length == 8 + SegmentSuffix.Length
                    && name.EndsWith(SegmentSuffix, StringComparison.Ordinal)
                    && int.TryParse(name.AsSpan(0, 8), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    _segments.Add(id);
                }
            }

            _segments.Sort();
        }

        public Durability Durability => Durability.Synchronised;

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                SegmentInfo[] list = new SegmentInfo[_segments.Count];

                for (int i = 0; i < list.Length; i++)
                {
                    int id = _segments[i];
                    ByteCount length = new(id == _activeId ? _activeLength : Length(id));
                    bool sealedSegment = i < list.Length - 1 || (id != _activeId && _files.IsReadOnly(PathOf(id)));
                    list[i] = sealedSegment ? SegmentInfo.Sealed(new SegmentId(id), length) : SegmentInfo.Open(new SegmentId(id), length);
                }

                return new ValueTask<IReadOnlyList<SegmentInfo>>(list);
            }
        }

        public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_segments.Count > 0 && !IsSealed(_segments[^1]))
                {
                    throw new InvalidOperationException("The newest segment is not sealed; only one segment may be open.");
                }

                int id = _segments.Count == 0 ? 0 : _segments[^1] + 1;
                _active = _files.Open(PathOf(id), FileOpen.CreateNew);
                _activeId = id;
                _activeLength = 0;
                _segments.Add(id);
                return new ValueTask<SegmentId>(new SegmentId(id));
            }
        }

        public async ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            IFileHandle handle;
            long offset;

            lock (_gate)
            {
                handle = Writable(segment.Value);
                offset = _activeLength;
                _activeLength += bytes.Length;
            }

            await handle.WriteAsync(offset, bytes, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (segment.Value == _activeId && _active is not null)
                {
                    _active.Flush();
                }
                else if (!_segments.Contains(segment.Value))
                {
                    throw new ArgumentOutOfRangeException(nameof(segment), segment.Value, "No such segment.");
                }

                return ValueTask.CompletedTask;
            }
        }

        public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (!_segments.Contains(segment.Value))
                {
                    throw new ArgumentOutOfRangeException(nameof(segment), segment.Value, "No such segment.");
                }

                if (segment.Value == _activeId && _active is not null)
                {
                    _active.Flush();
                    _active.Dispose();
                    _active = null;
                    _activeId = -1;
                }

                if (!_files.IsReadOnly(PathOf(segment.Value)))
                {
                    _files.MakeReadOnly(PathOf(segment.Value));
                }

                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            IFileHandle reader;
            long available;

            lock (_gate)
            {
                if (!_segments.Contains(segment.Value))
                {
                    throw new ArgumentOutOfRangeException(nameof(segment), segment.Value, "No such segment.");
                }

                if (!_readers.TryGetValue(segment.Value, out reader!))
                {
                    reader = _files.Open(PathOf(segment.Value), FileOpen.Read);
                    _readers[segment.Value] = reader;
                }

                available = segment.Value == _activeId ? _activeLength : reader.Length;
            }

            // The bytes are the caller's to hold: a fresh array, never a buffer
            // this store writes again (ADR 0040).
            long count = Math.Max(0, Math.Min(length.Value, available - offset.Value));

            if (count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length.Value, "One read holds at most int.MaxValue bytes.");
            }

            byte[] bytes = new byte[count];
            int read = reader.Read(offset.Value, bytes);
            return new ValueTask<ReadOnlyMemory<byte>>(bytes.AsMemory(0, read));
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken)
        {
            string path = Path.Combine(_directory, ManifestName);

            if (!_files.FileExists(path))
            {
                return new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
            }

            using IFileHandle handle = _files.Open(path, FileOpen.Read);
            byte[] bytes = new byte[Math.Min(handle.Length, 1 << 16)];
            return new ValueTask<ReadOnlyMemory<byte>>(bytes.AsMemory(0, handle.Read(0, bytes)));
        }

        public async ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken)
        {
            string path = Path.Combine(_directory, ManifestName);

            lock (_gate)
            {
                if (_segments.Count > 0)
                {
                    throw new InvalidOperationException("The manifest is written before any segment.");
                }
            }

            if (_files.FileExists(path))
            {
                throw new InvalidOperationException("The log already has a manifest; it is written once.");
            }

            // Written beside, flushed and renamed, before any segment: a crash
            // leaves the manifest whole or absent, never torn (ADR 0073).
            string temporary = path + TemporarySuffix;

            if (_files.FileExists(temporary))
            {
                _files.Delete(temporary);
            }

            using (IFileHandle handle = _files.Open(temporary, FileOpen.CreateNew))
            {
                await handle.WriteAsync(0, manifest, cancellationToken).ConfigureAwait(false);
                handle.Flush();
            }

            _files.Move(temporary, path);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _active?.Dispose();
                _active = null;
                _activeId = -1;

                foreach (IFileHandle reader in _readers.Values)
                {
                    reader.Dispose();
                }

                _readers.Clear();
            }
        }

        private IFileHandle Writable(int id)
        {
            if (_segments.Count == 0 || id != _segments[^1])
            {
                throw new InvalidOperationException("Segment " + id + " is sealed and never changes again.");
            }

            if (id == _activeId && _active is not null)
            {
                return _active;
            }

            if (_files.IsReadOnly(PathOf(id)))
            {
                throw new InvalidOperationException("Segment " + id + " is sealed and never changes again.");
            }

            // The newest segment, open in a copy or after a restart.
            _active = _files.Open(PathOf(id), FileOpen.Write);
            _activeId = id;
            _activeLength = _active.Length;
            return _active;
        }

        private bool IsSealed(int id) => id != _activeId && _files.IsReadOnly(PathOf(id));

        private long Length(int id)
        {
            if (_readers.TryGetValue(id, out IFileHandle? reader))
            {
                return reader.Length;
            }

            using IFileHandle handle = _files.Open(PathOf(id), FileOpen.Read);
            return handle.Length;
        }

        private string PathOf(int id) => Path.Combine(_directory, id.ToString("D8", CultureInfo.InvariantCulture) + SegmentSuffix);
    }

    /// <summary>Derived blobs as files under <c>derived/</c>.</summary>
    private sealed class FileDerivedStore : IDerivedStore, IDisposable
    {
        private readonly IFileSystem _files;
        private readonly string _directory;
        private readonly Lock _gate = new();
        private readonly HashSet<string> _deferred = new(StringComparer.Ordinal);
        private long _temporaries;

        public FileDerivedStore(IFileSystem files, string directory)
        {
            _files = files;
            _directory = directory;
        }

        public ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken)
        {
            string path = PathOf(name);
            string temporary = path + "." + Interlocked.Increment(ref _temporaries).ToString(CultureInfo.InvariantCulture) + TemporarySuffix;
            _files.CreateDirectory(Path.GetDirectoryName(path)!);
            return new ValueTask<IBlobWriter>(new Writer(this, name, path, temporary, _files.Open(temporary, FileOpen.CreateNew)));
        }

        public ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
        {
            string path = PathOf(name);

            lock (_gate)
            {
                if (_deferred.Contains(name.Value) || !_files.FileExists(path))
                {
                    throw new KeyNotFoundException("No derived blob named '" + name + "'.");
                }
            }

            return new ValueTask<IReadableBlob>(new FileBlob(_files.Open(path, FileOpen.Read)));
        }

        public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                string path = PathOf(name);

                if (_deferred.Contains(name.Value) || !_files.FileExists(path))
                {
                    return new ValueTask<bool>(false);
                }

                try
                {
                    _files.Delete(path);
                }
                catch (IOException)
                {
                    // Open by a reader on a platform that will not delete an open
                    // file: gone from the listing now, deleted when it can be.
                    _deferred.Add(name.Value);
                }

                RetryDeferred();
                return new ValueTask<bool>(true);
            }
        }

        public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken)
        {
            List<string> names = [];

            lock (_gate)
            {
                RetryDeferred();

                foreach (string file in _files.Files(_directory))
                {
                    string relative = Path.GetRelativePath(_directory, file).Replace(Path.DirectorySeparatorChar, '/');

                    if (IsBlobName(relative) && !_deferred.Contains(relative))
                    {
                        names.Add(relative);
                    }
                }
            }

            names.Sort(StringComparer.Ordinal);
            BlobName[] result = new BlobName[names.Count];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new BlobName(names[i]);
            }

            return new ValueTask<IReadOnlyList<BlobName>>(result);
        }

        /// <summary>Deletes what a crash left unpublished.</summary>
        public void DeleteUnpublished()
        {
            foreach (string file in _files.Files(_directory))
            {
                if (file.EndsWith(TemporarySuffix, StringComparison.Ordinal))
                {
                    _files.Delete(file);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                RetryDeferred();
            }
        }

        private void Publish(BlobName name, string temporary, string path)
        {
            lock (_gate)
            {
                _files.Move(temporary, path);
                _deferred.Remove(name.Value);
            }
        }

        private void RetryDeferred()
        {
            foreach (string name in new List<string>(_deferred))
            {
                try
                {
                    string path = PathOf(new BlobName(name));

                    if (_files.FileExists(path))
                    {
                        _files.Delete(path);
                    }

                    _deferred.Remove(name);
                }
                catch (IOException)
                {
                }
            }
        }

        // Names are the store's own (checkpoints/…, index/…). A name that could
        // leave derived/ or collide with the backend's own files is refused.
        private string PathOf(BlobName name)
        {
            if (!IsBlobName(name.Value))
            {
                throw new ArgumentException("'" + name + "' is not a derived blob name this backend stores.", nameof(name));
            }

            return Path.Combine(_directory, name.Value.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool IsBlobName(string name)
        {
            if (name is LockName or OwnerName or IgnoreName || name.EndsWith(TemporarySuffix, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string part in name.Split('/'))
            {
                if (part.Length == 0 || part[0] == '.')
                {
                    return false;
                }

                foreach (char c in part)
                {
                    if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private sealed class Writer(FileDerivedStore store, BlobName name, string path, string temporary, IFileHandle handle) : IBlobWriter
        {
            private long _length;
            private bool _done;

            public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(_done, this);
                await handle.WriteAsync(_length, bytes, cancellationToken).ConfigureAwait(false);
                _length += bytes.Length;
            }

            public ValueTask PublishAsync(CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(_done, this);
                handle.Flush();
                handle.Dispose();
                _done = true;
                store.Publish(name, temporary, path);
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                if (!_done)
                {
                    _done = true;
                    handle.Dispose();
                    store._files.Delete(temporary);
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>A published blob, read through a held handle.</summary>
    private sealed class FileBlob(IFileHandle handle) : IReadableBlob
    {
        private readonly long _length = handle.Length;

        public ByteCount Length => new(_length);

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public int Read(ByteOffset offset, Span<byte> destination)
        {
            long remaining = _length - offset.Value;

            if (remaining <= 0)
            {
                return 0;
            }

            return handle.Read(offset.Value, remaining < destination.Length ? destination[..(int)remaining] : destination);
        }

        public void Dispose() => handle.Dispose();
    }
}

/// <summary>
/// The dataset directory is open in another <see cref="FileStorage"/>, in this
/// process or another (ADR 0075).
/// </summary>
public sealed class DatasetLeasedException : IOException
{
    /// <summary>The directory is leased.</summary>
    public DatasetLeasedException()
    {
    }

    /// <summary>The directory is leased, and why.</summary>
    public DatasetLeasedException(string message)
        : base(message)
    {
    }

    /// <summary>The directory is leased, why, and the cause.</summary>
    public DatasetLeasedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal DatasetLeasedException(DatasetDirectory directory, string? owner)
        : base("The dataset directory '" + directory + "' is open elsewhere" + (owner is null ? string.Empty : " (" + owner + ")") + ". One process opens a dataset at a time; the lease is released when that process closes it or ends.")
    {
    }
}
