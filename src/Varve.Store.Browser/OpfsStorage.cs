// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Browser.Model;
using Varve.Store.Log;

namespace Varve.Store.Browser;

/// <summary>
/// Storage in the origin private file system, through synchronous access
/// handles, laid out as the file backend lays out a dataset directory
/// (ADR 0084).
/// </summary>
/// <remarks>
/// <para>
/// <c>log/</c> holds <c>NNNNNNNN.seg</c> and <c>MANIFEST</c>, the same names
/// and the same bytes as <see cref="FileStorage"/> writes, so a log copied out
/// of the browser opens on the desktop. A segment is appended to and read
/// through one held handle, and flushed with <c>flush()</c>.
/// </para>
/// <para>
/// <c>derived/</c> holds every published blob under its name and a
/// generation, <c>name~G</c>: a synchronous access handle locks its file, and
/// a locked file can be neither replaced, moved aside nor removed. So a
/// publish never replaces a file. It writes <c>name~G~tmp</c>, flushes it,
/// and moves it to <c>name~G</c> with a generation above every other; the
/// highest generation of a name is the blob, so the move is the atomic
/// replacement, across a crash too. The older file is removed when no reader
/// holds it, or at the next open. The names without <c>~</c> are the
/// backend's: <c>LOCK</c>, <c>LOCK.owner</c> and <c>SEALED</c>.
/// </para>
/// <para>
/// A seal is in the segment's bytes (ADR 0072); the backend's own record of
/// it is <c>derived/SEALED</c>, the highest sealed segment's id. Every segment
/// but the newest is sealed by construction; a lost <c>SEALED</c> reports the
/// newest open, and the store's recovery seals it again from its trailer.
/// </para>
/// </remarks>
internal sealed class OpfsStorage : BrowserStorage
{
    internal const string LockName = "LOCK";
    internal const string OwnerName = "LOCK.owner";
    internal const string SealedName = "SEALED";

    private readonly JSObject _lease;
    private readonly OpfsSegmentStore _log;
    private readonly OpfsDerivedStore _derived;

    private OpfsStorage(BrowserDatasetName name, JSObject lease, OpfsSegmentStore log, OpfsDerivedStore derived)
        : base(name)
    {
        _lease = lease;
        _log = log;
        _derived = derived;
    }

    public override BrowserBackend Backend => BrowserBackend.OriginPrivateFileSystem;

    public override ISegmentStore Log => _log;

    public override IDerivedStore Derived => _derived;

    public static async ValueTask<OpfsStorage> OpenHereAsync(BrowserDatasetName name, BrowserStorageOptions options, CancellationToken cancellationToken)
    {
        JSObject root = await Interop.OpfsDirectory(name.Value).ConfigureAwait(false);
        JSObject log = await Interop.OpfsChild(root, "log").ConfigureAwait(false);
        JSObject derived = await Interop.OpfsChild(root, "derived").ConfigureAwait(false);

        JSObject lease = await Interop.OpfsOpen(derived, LockName, create: true).ConfigureAwait(false)
            ?? throw Leased(name, await Interop.OpfsReadText(derived, OwnerName).ConfigureAwait(false));

        try
        {
            await WriteSmallFileAsync(derived, OwnerName, Encoding.UTF8.GetBytes(
                "a browser context of this origin, since " + options.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture))).ConfigureAwait(false);

            OpfsSegmentStore segments = await OpfsSegmentStore.OpenAsync(log, derived).ConfigureAwait(false);
            OpfsDerivedStore blobs = await OpfsDerivedStore.OpenAsync(derived).ConfigureAwait(false);
            return new OpfsStorage(name, lease, segments, blobs);
        }
        catch
        {
            Interop.SyncClose(lease);
            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        _log.Close();
        await _derived.CloseAsync().ConfigureAwait(false);
        Interop.SyncClose(_lease);
        _lease.Dispose();
    }

    private static DatasetLeasedException Leased(BrowserDatasetName name, string? owner) =>
        new("The dataset '" + name + "' is open elsewhere in this origin" + (owner is null ? string.Empty : " (" + owner + ")")
            + ". One opener at a time: the lease is a synchronous access handle on derived/LOCK, which the browser releases when the worker that holds it closes it or ends.");

    /// <summary>A small file replaced whole: truncated, written, flushed.</summary>
    internal static async ValueTask WriteSmallFileAsync(JSObject directory, string path, byte[] bytes)
    {
        JSObject handle = await Interop.OpfsOpen(directory, path, create: true).ConfigureAwait(false)
            ?? throw new IOException("'" + path + "' is held open.");

        try
        {
            Interop.SyncTruncate(handle, 0);
            Interop.Write(handle, bytes, 0);
            Interop.SyncFlush(handle);
        }
        finally
        {
            Interop.SyncClose(handle);
            handle.Dispose();
        }
    }

    /// <summary>Reads a whole file through a short-lived handle.</summary>
    internal static async ValueTask<byte[]> ReadSmallFileAsync(JSObject directory, string path)
    {
        JSObject handle = await Interop.OpfsOpen(directory, path, create: false).ConfigureAwait(false)
            ?? throw new IOException("'" + path + "' is held open.");

        try
        {
            byte[] bytes = new byte[(int)Interop.SyncSize(handle)];
            ReadFully(handle, 0, bytes);
            return bytes;
        }
        finally
        {
            Interop.SyncClose(handle);
            handle.Dispose();
        }
    }

    /// <summary>Reads until the span is full or the file ends; returns how many.</summary>
    internal static int ReadFully(JSObject handle, long offset, Span<byte> destination)
    {
        int total = 0;

        while (total < destination.Length)
        {
            int read = Interop.SyncRead(handle, destination[total..], offset + total);

            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>The log's segments, each read and appended through one held handle.</summary>
    private sealed class OpfsSegmentStore : ISegmentStore
    {
        private const string ManifestName = "MANIFEST";
        private const string TemporaryManifest = "MANIFEST.tmp";
        private const string SegmentSuffix = ".seg";

        private readonly JSObject _directory;
        private readonly JSObject _derived;
        private readonly List<int> _segments;
        private readonly Dictionary<int, JSObject> _handles = [];
        private readonly Dictionary<int, long> _lengths = [];
        private int _sealedThrough;
        private byte[]? _manifest;
        private bool _hasManifest;

        private OpfsSegmentStore(JSObject directory, JSObject derived, List<int> segments, int sealedThrough, bool hasManifest)
        {
            _directory = directory;
            _derived = derived;
            _segments = segments;
            _sealedThrough = sealedThrough;
            _hasManifest = hasManifest;
        }

        public Durability Durability => Durability.Committed;

        public static async ValueTask<OpfsSegmentStore> OpenAsync(JSObject directory, JSObject derived)
        {
            List<int> segments = [];
            bool hasManifest = false;

            foreach (string file in Lines(await Interop.OpfsFiles(directory).ConfigureAwait(false)))
            {
                if (file == ManifestName)
                {
                    hasManifest = true;
                }
                else if (file.Length == 8 + SegmentSuffix.Length
                    && file.EndsWith(SegmentSuffix, StringComparison.Ordinal)
                    && int.TryParse(file.AsSpan(0, 8), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    segments.Add(id);
                }
            }

            segments.Sort();
            string? marker = await Interop.OpfsReadText(derived, SealedName).ConfigureAwait(false);
            int sealedThrough = int.TryParse(marker, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : -1;
            OpfsSegmentStore store = new(directory, derived, segments, sealedThrough, hasManifest);

            foreach (int id in segments)
            {
                await store.HandleAsync(id).ConfigureAwait(false);
            }

            return store;
        }

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
        {
            SegmentInfo[] list = new SegmentInfo[_segments.Count];

            for (int i = 0; i < list.Length; i++)
            {
                int id = _segments[i];
                ByteCount length = new(_lengths[id]);
                list[i] = IsSealed(id) ? SegmentInfo.Sealed(new SegmentId(id), length) : SegmentInfo.Open(new SegmentId(id), length);
            }

            return new ValueTask<IReadOnlyList<SegmentInfo>>(list);
        }

        public async ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
        {
            if (_segments.Count > 0 && !IsSealed(_segments[^1]))
            {
                throw new InvalidOperationException("The newest segment is not sealed; only one segment may be open.");
            }

            int id = _segments.Count == 0 ? 0 : _segments[^1] + 1;
            _segments.Add(id);
            await HandleAsync(id).ConfigureAwait(false);
            return new SegmentId(id);
        }

        public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            int id = segment.Value;

            if (_segments.Count == 0 || id != _segments[^1] || IsSealed(id))
            {
                throw new InvalidOperationException("Segment " + id + " is sealed and never changes again.");
            }

            long at = _lengths[id];
            _lengths[id] = at + Interop.Write(_handles[id], bytes, at);
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken)
        {
            Interop.SyncFlush(Existing(segment.Value));
            return ValueTask.CompletedTask;
        }

        public async ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken)
        {
            Interop.SyncFlush(Existing(segment.Value));

            if (segment.Value > _sealedThrough)
            {
                _sealedThrough = segment.Value;
                await WriteSmallFileAsync(_derived, SealedName, Encoding.ASCII.GetBytes(segment.Value.ToString("D8", CultureInfo.InvariantCulture))).ConfigureAwait(false);
            }
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            JSObject handle = Existing(segment.Value);
            long count = Math.Max(0, Math.Min(length.Value, _lengths[segment.Value] - offset.Value));

            if (count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length.Value, "One read holds at most int.MaxValue bytes.");
            }

            // A fresh array, the caller's to hold (ADR 0040).
            byte[] bytes = new byte[count];
            int read = ReadFully(handle, offset.Value, bytes);
            return new ValueTask<ReadOnlyMemory<byte>>(bytes.AsMemory(0, read));
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken)
        {
            if (!_hasManifest)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            _manifest ??= await ReadSmallFileAsync(_directory, ManifestName).ConfigureAwait(false);
            return _manifest;
        }

        public async ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken)
        {
            if (_hasManifest)
            {
                throw new InvalidOperationException("The log already has a manifest; it is written once.");
            }

            if (_segments.Count > 0)
            {
                throw new InvalidOperationException("The manifest is written before any segment.");
            }

            // Written beside, flushed and moved, before any segment: whole or
            // absent, never torn (ADR 0073's rule, kept here).
            await Interop.OpfsRemove(_directory, TemporaryManifest).ConfigureAwait(false);
            await WriteSmallFileAsync(_directory, TemporaryManifest, manifest.ToArray()).ConfigureAwait(false);

            if (await Interop.OpfsMove(_directory, TemporaryManifest, ManifestName).ConfigureAwait(false) != 0)
            {
                throw new IOException("The manifest is held open.");
            }

            _hasManifest = true;
            _manifest = manifest.ToArray();
        }

        public void Close()
        {
            foreach (JSObject handle in _handles.Values)
            {
                Interop.SyncClose(handle);
                handle.Dispose();
            }

            _handles.Clear();
        }

        private bool IsSealed(int id) => id != _segments[^1] || id <= _sealedThrough;

        private JSObject Existing(int id) =>
            _handles.TryGetValue(id, out JSObject? handle)
                ? handle
                : throw new ArgumentOutOfRangeException(nameof(id), id, "No such segment.");

        private async ValueTask HandleAsync(int id)
        {
            JSObject handle = await Interop.OpfsOpen(_directory, id.ToString("D8", CultureInfo.InvariantCulture) + SegmentSuffix, create: true).ConfigureAwait(false)
                ?? throw new IOException("Segment " + id + " is held open by another handle, which the lease should have made impossible.");
            _handles[id] = handle;
            _lengths[id] = (long)Interop.SyncSize(handle);
        }
    }

    /// <summary>Derived blobs as generations of files under <c>derived/</c>.</summary>
    private sealed class OpfsDerivedStore : IDerivedStore
    {
        private const char Separator = '~';
        private const string TemporarySuffix = "~tmp";

        private readonly JSObject _directory;
        private readonly SortedDictionary<string, long> _current = new(StringComparer.Ordinal);
        private readonly Dictionary<string, OpenFile> _open = new(StringComparer.Ordinal);
        private readonly HashSet<string> _unreferenced = new(StringComparer.Ordinal);
        private long _nextGeneration;

        private OpfsDerivedStore(JSObject directory) => _directory = directory;

        /// <summary>
        /// Lists the blobs: the highest generation of each name. What a crash
        /// left behind — unpublished temporaries, generations a newer one
        /// replaced — is removed.
        /// </summary>
        public static async ValueTask<OpfsDerivedStore> OpenAsync(JSObject directory)
        {
            OpfsDerivedStore store = new(directory);
            List<string> stale = [];

            foreach (string file in Lines(await Interop.OpfsFiles(directory).ConfigureAwait(false)))
            {
                if (file.EndsWith(TemporarySuffix, StringComparison.Ordinal))
                {
                    stale.Add(file);
                    continue;
                }

                int separator = file.LastIndexOf(Separator);

                if (separator <= 0
                    || !long.TryParse(file.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long generation)
                    || !IsBlobName(file[..separator]))
                {
                    continue;
                }

                string name = file[..separator];
                store._nextGeneration = Math.Max(store._nextGeneration, generation + 1);

                if (store._current.TryGetValue(name, out long existing))
                {
                    stale.Add(Physical(name, Math.Min(existing, generation)));
                    store._current[name] = Math.Max(existing, generation);
                }
                else
                {
                    store._current[name] = generation;
                }
            }

            foreach (string file in stale)
            {
                await Interop.OpfsRemove(directory, file).ConfigureAwait(false);
            }

            return store;
        }

        public async ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken)
        {
            Check(name);
            long generation = _nextGeneration++;
            string temporary = Physical(name.Value, generation) + TemporarySuffix;
            JSObject handle = await Interop.OpfsOpen(_directory, temporary, create: true).ConfigureAwait(false)
                ?? throw new IOException("'" + temporary + "' is held open.");
            return new Writer(this, name.Value, generation, temporary, handle);
        }

        public async ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
        {
            Check(name);

            if (!_current.TryGetValue(name.Value, out long generation))
            {
                throw new KeyNotFoundException("No derived blob named '" + name + "'.");
            }

            string physical = Physical(name.Value, generation);

            // A synchronous access handle is exclusive, so readers of one file
            // share one handle, closed when the last of them is disposed.
            if (!_open.TryGetValue(physical, out OpenFile? file))
            {
                JSObject handle = await Interop.OpfsOpen(_directory, physical, create: false).ConfigureAwait(false)
                    ?? throw new IOException("Derived blob '" + name + "' is held open by another handle, which the lease should have made impossible.");

                // The blob may have been replaced while the handle was opening.
                if (_open.TryGetValue(physical, out OpenFile? raced))
                {
                    Interop.SyncClose(handle);
                    handle.Dispose();
                    file = raced;
                }
                else
                {
                    file = new OpenFile(this, physical, handle, (long)Interop.SyncSize(handle));
                    _open[physical] = file;
                }
            }

            file.References++;
            return new OpfsBlob(file);
        }

        public async ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken)
        {
            Check(name);

            if (!_current.Remove(name.Value, out long generation))
            {
                return false;
            }

            _unreferenced.Add(Physical(name.Value, generation));
            await RemoveUnreferencedAsync().ConfigureAwait(false);
            return true;
        }

        public async ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken)
        {
            await RemoveUnreferencedAsync().ConfigureAwait(false);
            BlobName[] names = new BlobName[_current.Count];
            int i = 0;

            foreach (string name in _current.Keys)
            {
                names[i++] = new BlobName(name);
            }

            return names;
        }

        public async ValueTask CloseAsync()
        {
            foreach (OpenFile file in _open.Values)
            {
                Interop.SyncClose(file.Handle);
                file.Handle.Dispose();
            }

            _open.Clear();
            await RemoveUnreferencedAsync().ConfigureAwait(false);
        }

        private async ValueTask PublishAsync(string name, long generation)
        {
            if (_current.TryGetValue(name, out long previous))
            {
                _unreferenced.Add(Physical(name, previous));
            }

            _current[name] = generation;
            await RemoveUnreferencedAsync().ConfigureAwait(false);
        }

        // Files no name refers to any more, removed once no reader holds them.
        // One still held stays until its reader is disposed and a later call
        // comes here, or until the next open removes it.
        private async ValueTask RemoveUnreferencedAsync()
        {
            if (_unreferenced.Count == 0)
            {
                return;
            }

            foreach (string physical in new List<string>(_unreferenced))
            {
                if (!_open.ContainsKey(physical) && await Interop.OpfsRemove(_directory, physical).ConfigureAwait(false) != 2)
                {
                    _unreferenced.Remove(physical);
                }
            }
        }

        private static string Physical(string name, long generation) =>
            name + Separator + generation.ToString(CultureInfo.InvariantCulture);

        private static void Check(BlobName name)
        {
            if (!IsBlobName(name.Value))
            {
                throw new ArgumentException("'" + name + "' is not a derived blob name this backend stores.", nameof(name));
            }
        }

        // The file backend's rule: parts of ASCII letters, digits, '-', '_'
        // and '.', none starting with '.'. '~' is the backend's own.
        private static bool IsBlobName(string name)
        {
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

        internal sealed class OpenFile(OpfsDerivedStore store, string physical, JSObject handle, long length)
        {
            public JSObject Handle { get; } = handle;

            public long Length { get; } = length;

            public int References { get; set; }

            public void Release()
            {
                if (--References == 0 && store._open.Remove(physical))
                {
                    Interop.SyncClose(Handle);
                    Handle.Dispose();
                }
            }
        }

        private sealed class Writer(OpfsDerivedStore store, string name, long generation, string temporary, JSObject handle) : IBlobWriter
        {
            private long _length;
            private bool _done;

            public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(_done, this);
                _length += Interop.Write(handle, bytes, _length);
                return ValueTask.CompletedTask;
            }

            public async ValueTask PublishAsync(CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(_done, this);
                _done = true;
                Interop.SyncFlush(handle);
                Interop.SyncClose(handle);
                handle.Dispose();

                if (await Interop.OpfsMove(store._directory, temporary, Physical(name, generation)).ConfigureAwait(false) != 0)
                {
                    throw new IOException("Derived blob '" + name + "' could not be published: its new generation is held open.");
                }

                await store.PublishAsync(name, generation).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (!_done)
                {
                    _done = true;
                    Interop.SyncClose(handle);
                    handle.Dispose();
                    await Interop.OpfsRemove(store._directory, temporary).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>A published blob, read through a shared synchronous access handle.</summary>
    private sealed class OpfsBlob(OpfsDerivedStore.OpenFile file) : IReadableBlob
    {
        private bool _disposed;

        public ByteCount Length => new(file.Length);

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        [DesignDecision(typeof(TheBrowserBackend.SynchronousReadThroughJSImport), Scope = ExceptionScope.HotPath)]
        public int Read(ByteOffset offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long remaining = file.Length - offset.Value;

            if (remaining <= 0)
            {
                return 0;
            }

            return ReadFully(file.Handle, offset.Value, remaining < destination.Length ? destination[..(int)remaining] : destination);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                file.Release();
            }
        }
    }

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Split('\n');
}
