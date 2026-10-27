// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// A dataset's storage in an object store with conditional writes (ADRs
/// 0134–0136): the log as write-once chunk objects, one writer by an epoch
/// claimed in the writer record and fenced in the log itself, and a local
/// cache for copies of sealed segments and for derived data.
/// </summary>
/// <remarks>
/// <para>
/// **Opening for write claims the next epoch** and creates the next log slot
/// carrying it, so a writer that still runs elsewhere appends nothing after
/// it: its next create finds the slot taken. A writer that loses the claim,
/// or finds a newer epoch already in the log, opens read-only and reports
/// <see cref="IsSuperseded"/>; it never claims again on its own. Nothing is
/// renewed, and nothing is decided from elapsed time (ADR 0136).
/// </para>
/// <para>
/// **Closing releases the epoch** with the reason
/// <see cref="ReleaseReason.Shutdown"/>, so the next writer's claim follows a
/// release. A writer that dies without closing is taken over by the next one
/// that opens.
/// </para>
/// <para>
/// Declares <see cref="Durability.Replicated"/>: a flush returns when the
/// store has acknowledged the chunk. The cache directory is used by one
/// process at a time; a second opener of it is refused with
/// <see cref="DatasetLeasedException"/>.
/// </para>
/// </remarks>
[Contract(typeof(TheLogAsChunkObjects.ChunkPerFlush), Role = "a dataset's storage in an object store with conditional writes")]
public sealed class ObjectStorage : ITailingStorage, IStorageMaintenance, IExtentReaders, IAsyncDisposable
{
    private const string SegmentCopies = "segments";
    private const string LocalStorage = "local";
    private const string ExtentPages = "pages";
    private const string StagedUploads = "staging";
    private const string ReaderIdFile = "reader.id";

    private readonly IObjectStore _store;
    private readonly ObjectNames _names;
    private readonly TimeProvider _clock;
    private readonly ObjectSegmentStore _log;
    private readonly FileStorage _local;
    private readonly ObjectDerivedStore _derived;
    private readonly SemaphoreSlim _readerGate = new(1, 1);
    private ObjectVersion? _recordVersion;
    private ObjectVersion? _readerVersion;
    private bool _readerForgotten;
    private int _disposed;

    private ObjectStorage(IObjectStore store, ObjectNames names, TimeProvider clock, ObjectSegmentStore log, FileStorage local, ObjectDerivedStore derived, WriterRecord? record, ObjectVersion? recordVersion, bool supersededAtOpen)
    {
        _derived = derived;
        SupersededAtOpen = supersededAtOpen;
        _store = store;
        _names = names;
        _clock = clock;
        _log = log;
        _local = local;
        Writer = record;
        _recordVersion = recordVersion;
    }

    /// <inheritdoc />
    public ISegmentStore Log => _log;

    /// <inheritdoc />
    public IDerivedStore Derived => _derived;

    /// <summary>How this storage was opened.</summary>
    public ObjectStorageAccess Access => _log.Writes ? ObjectStorageAccess.Write : ObjectStorageAccess.ReadOnly;

    /// <summary>The writer epoch this storage holds, or none when it holds none.</summary>
    public WriterEpoch? Epoch => _log.Writes && !_log.Superseded ? Writer?.Epoch : null;

    /// <summary>
    /// Whether this storage was opened for write and does not hold the epoch:
    /// it lost the claim, or a newer writer has taken the log since. Reads
    /// continue; every write is refused with <see cref="WriterSupersededException"/>.
    /// </summary>
    public bool IsSuperseded => SupersededAtOpen || (_log.Writes && (_log.Superseded || _derived.CatalogSuperseded));

    /// <summary>The writer record as this storage last read or wrote it, or none when the store has none.</summary>
    public WriterRecord? Writer { get; private set; }

    /// <inheritdoc />
    public bool MaintenanceDue => _log.ConsolidationDue;

    /// <summary>This instance's reader id: its reader record's name (ADR 0160).</summary>
    public ReaderId Reader { get; private set; }

    /// <summary>Whether this open writes a reader record.</summary>
    public ReaderAnnouncing Announcing { get; private set; }

    /// <inheritdoc />
    public bool DeletesExtents => _log.Writes && !IsSuperseded && _derived.Writes;

    // The name the record gives the instance: the writer's, or none.
    private WriterInstance? Holder { get; set; }

    // Opened for write and lost the claim: read-only from the start.
    private bool SupersededAtOpen { get; }

    /// <summary>
    /// Opens a dataset in an object store. For write, first probes that the
    /// store enforces conditional writes (ADR 0134), then claims the next
    /// epoch and fences the log with it; read-only, changes nothing in the
    /// store.
    /// </summary>
    /// <exception cref="ConditionalWritesNotEnforcedException">The store does not enforce conditional writes.</exception>
    /// <exception cref="DatasetLeasedException">The cache directory is in use by another opener.</exception>
    /// <exception cref="LogVerificationException">A chunk of the log does not verify.</exception>
    public static ValueTask<ObjectStorage> OpenAsync(ObjectStorageOptions options, CancellationToken cancellationToken = default) =>
        OpenAsync(options, ClaimTrigger.Restart, null, cancellationToken);

    /// <summary>
    /// Takes the writer epoch over from <paramref name="from"/>, an operator's
    /// act (ADR 0136): claims the next epoch only if the writer record still
    /// shows <paramref name="from"/>, fences the log with it so the writer that
    /// held it appends nothing more, and releases it for the next writer to
    /// claim. No time is consulted. Returns the record after, or none when the
    /// record no longer showed <paramref name="from"/> and nothing was done.
    /// </summary>
    public static async ValueTask<WriterRecord?> TakeOverAsync(ObjectStorageOptions options, WriterEpoch from, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Access != ObjectStorageAccess.Write)
        {
            throw new ArgumentException("A takeover writes: open it with ObjectStorageAccess.Write.", nameof(options));
        }

        ObjectStorage storage = await OpenAsync(options, ClaimTrigger.Operator, from, cancellationToken).ConfigureAwait(false);

        try
        {
            if (storage.IsSuperseded)
            {
                return null;
            }

            await storage.ReleaseAsync(ReleaseReason.OperatorTakeover, cancellationToken).ConfigureAwait(false);
            return storage.Writer;
        }
        finally
        {
            storage._recordVersion = null;
            await storage.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Reads a dataset's writer record, or none when no writer has claimed it. Changes nothing.</summary>
    public static async ValueTask<WriterRecord?> ReadWriterAsync(IObjectStore store, ObjectName? prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ObjectRead? read = await ConditionalWrites.ReadWholeAsync(store, new ObjectNames(prefix).Writer, cancellationToken).ConfigureAwait(false);
        return read is null ? null : WriterRecordFormat.Read(read.Bytes.Span);
    }

    /// <summary>
    /// A follower's refresh (ADR 0164): probes the next log slot, and adopts
    /// what the writer has appended since. Never lists, and never writes.
    /// </summary>
    /// <exception cref="InvalidOperationException">This storage was opened for write.</exception>
    /// <exception cref="LogVerificationException">A chunk does not verify or does not follow.</exception>
    public ValueTask<bool> RefreshAsync(CancellationToken cancellationToken) => _log.RefreshAsync(cancellationToken);

    /// <summary>
    /// The highest writer epoch in a chunk header this storage has adopted,
    /// or none: how a follower learns, from its own refresh, that the
    /// dataset's writer has changed (ADR 0179 point 12). The epoch is the
    /// backend's, in the chunk header, never the log's (ADR 0135).
    /// </summary>
    public WriterEpoch? AdoptedEpoch => _log.AdoptedEpoch is > 0 and var epoch ? new WriterEpoch(epoch) : null;

    /// <inheritdoc />
    public ValueTask<bool> MaintainStorageAsync(CancellationToken cancellationToken) => _log.ConsolidateAsync(cancellationToken);

    /// <summary>
    /// Replaces this instance's reader record with what it announces (ADR
    /// 0160): created on the first announcement — replacing the one a previous
    /// run of the same id left — and afterwards replaced only against the
    /// version this instance last wrote. A replace that finds another version
    /// means the record was forgotten, or another instance of the same id
    /// wrote it: the instance is forgotten, and its next announcement is a
    /// fresh record.
    /// </summary>
    /// <exception cref="ReaderForgottenException">The record was forgotten.</exception>
    /// <exception cref="ObjectStoreAccessDeniedException">The identity may not write <c>readers/</c>; the message names the role.</exception>
    [DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.AnnouncingIsRequired), Scope = ExceptionScope.Boundary)]
    public async ValueTask AnnounceAsync(CatalogVersion oldest, CatalogVersion newest, CancellationToken cancellationToken)
    {
        if (Announcing == ReaderAnnouncing.Unannounced)
        {
            return;
        }

        ReaderRecord record = new()
        {
            Reader = Reader,
            Holder = Holder,
            Oldest = oldest,
            Newest = newest,
            AnnouncedAt = new ClaimTime(_clock.GetUtcNow()),
            MaxDerived = DerivedVersion.ThisBuildReads,
        };
        byte[] bytes = ReaderRecordFormat.Write(record);
        ObjectName name = _names.Reader(Reader);

        await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_readerVersion is { } expected)
            {
                ObjectVersion? replaced = await ConditionalWrites.ReplaceAsync(_store, name, expected, bytes, _clock, cancellationToken).ConfigureAwait(false);

                if (replaced is null && await OwnRecordVersionAsync(name, cancellationToken).ConfigureAwait(false) is { } unheard)
                {
                    // The version held is stale, but the record is this
                    // instance's own: an earlier replace landed after its
                    // answer was lost, at a version this instance never heard
                    // (#145). Adopt it; the record was not forgotten.
                    replaced = await ConditionalWrites.ReplaceAsync(_store, name, unheard, bytes, _clock, cancellationToken).ConfigureAwait(false);
                }

                if (replaced is null)
                {
                    _readerVersion = null;
                    _readerForgotten = true;
                    throw new ReaderForgottenException("The reader record '" + name + "' was forgotten, or replaced by another instance of the reader id '" + Reader + "'; this instance attaches again (ADR 0160).");
                }

                _readerVersion = replaced;
                return;
            }

            // A fresh record: over what a previous run of this id left, or new.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                ObjectRead? current = await ConditionalWrites.ReadWholeAsync(_store, name, cancellationToken).ConfigureAwait(false);

                if (current is null)
                {
                    (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_store, name, bytes, _clock, cancellationToken).ConfigureAwait(false);

                    if (outcome == CreateOutcome.Ours && await ConditionalWrites.ReadWholeAsync(_store, name, cancellationToken).ConfigureAwait(false) is { } written && written.Bytes.Span.SequenceEqual(bytes))
                    {
                        _readerVersion = written.Version;
                        _readerForgotten = false;
                        return;
                    }

                    continue;
                }

                if (await ConditionalWrites.ReplaceAsync(_store, name, current.Version, bytes, _clock, cancellationToken).ConfigureAwait(false) is { } version)
                {
                    _readerVersion = version;
                    _readerForgotten = false;
                    return;
                }
            }

            throw new IOException("The reader record '" + name + "' changed under every attempt to write it: another instance announces under the reader id '" + Reader + "'. Give each instance its own id.");
        }
        catch (ObjectStoreAccessDeniedException denied)
        {
            throw new ObjectStoreAccessDeniedException(
                "This open announces the catalog versions it reads in its reader record '" + name + "' (ADR 0160), and the store refused the write. "
                + "The identity needs write access to readers/ under the dataset's prefix: on Azure, Storage Blob Data Contributor with a condition on the blob path readers/*; "
                + "on S3, s3:PutObject and s3:DeleteObject on the prefix's readers/*. A read-only identity can read unannounced, explicitly (--unannounced).",
                denied);
        }
        finally
        {
            _readerGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReaderAnnouncement>> ReadAnnouncementsAsync(CancellationToken cancellationToken)
    {
        List<ReaderAnnouncement> announcements = [];

        foreach (ReaderRecord record in await ReadReadersAsync(_store, _names, cancellationToken).ConfigureAwait(false))
        {
            if (record.State == ReaderRecordState.Announced || !record.Understood)
            {
                announcements.Add(record.Announcement);
            }
        }

        return announcements;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, long>> ExtentBytesAsync(CancellationToken cancellationToken) =>
        await SetBytesAsync(_store, _names, cancellationToken).ConfigureAwait(false);

    /// <summary>Every reader record of a dataset, forgotten ones included (ADR 0160). Changes nothing.</summary>
    public static ValueTask<IReadOnlyList<ReaderRecord>> ReadReadersAsync(IObjectStore store, ObjectName? prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        return ReadReadersAsync(store, new ObjectNames(prefix), cancellationToken);
    }

    /// <summary>
    /// Forgets a reader record, the operator's act (ADR 0160, ADR 0162): only
    /// if it still announces <paramref name="oldest"/> to <paramref name="newest"/>,
    /// as it was listed. It is replaced by a tombstone against the version
    /// read, so a reader that announced since is not forgotten by mistake,
    /// then deleted. The writer may then delete what the record held, at its
    /// next round. Returns false, doing nothing, when the record is gone or
    /// announces something else now. No time is consulted.
    /// </summary>
    [DesignDecision(typeof(StorageOperationsAndTheReadersCommand.ReadersListAndForget), Scope = ExceptionScope.Boundary)]
    public static async ValueTask<bool> ForgetReaderAsync(
        IObjectStore store, ObjectName? prefix, ReaderId reader, CatalogVersion oldest, CatalogVersion newest, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ObjectNames names = new(prefix);
        ObjectName name = names.Reader(reader);
        ObjectRead? read = await ConditionalWrites.ReadWholeAsync(store, name, cancellationToken).ConfigureAwait(false);

        if (read is null)
        {
            return false;
        }

        ReaderRecord record = ReaderRecordFormat.Read(reader, read.Bytes.Span);

        if (record.Announcement.Oldest != oldest || record.Announcement.Newest != newest)
        {
            return false;
        }

        ReaderRecord tombstone = record.Understood
            ? record with { State = ReaderRecordState.Forgotten, AnnouncedAt = new ClaimTime(clock.GetUtcNow()) }
            : new ReaderRecord { Reader = reader, Oldest = oldest, Newest = newest, State = ReaderRecordState.Forgotten, AnnouncedAt = new ClaimTime(clock.GetUtcNow()) };

        if (await ConditionalWrites.ReplaceAsync(store, name, read.Version, ReaderRecordFormat.Write(tombstone), clock, cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        await store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// What <c>varve storage</c> reports (ADR 0162): the catalog's sets with
    /// their bytes per permutation, the sets no catalog names that the store
    /// still holds, and what each reader record holds of them — a set the
    /// catalog stopped naming is held by a record that announces any version
    /// after the set's sequence and before the current one. Changes nothing.
    /// </summary>
    [DesignDecision(typeof(StorageOperationsAndTheReadersCommand.StorageReportsSetsAndRetention), Scope = ExceptionScope.Boundary)]
    public static async ValueTask<StorageReport> ReadReportAsync(IObjectStore store, ObjectName? prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ObjectNames names = new(prefix);
        ObjectRead? catalogRead = await ConditionalWrites.ReadWholeAsync(store, names.Derived("catalog"), cancellationToken).ConfigureAwait(false);
        CatalogSummary? catalog = null;

        if (catalogRead is not null && catalogRead.Bytes.Length > 12)
        {
            catalog = CatalogSummary.TryRead(catalogRead.Bytes[12..]);
        }

        Dictionary<string, long> blobs = new(StringComparer.Ordinal);
        string start = names.Derived(string.Empty).Value;

        foreach (ObjectEntry entry in await store.ListAsync(names.Extents, cancellationToken).ConfigureAwait(false))
        {
            blobs[entry.Name.Value[start.Length..]] = entry.Length.Value;
        }

        HashSet<string> named = new(StringComparer.Ordinal);
        List<StoredSet> sets = [];

        foreach (CatalogSet set in catalog?.Sets ?? [])
        {
            named.Add(set.Name.Value);
            ByteCount[] orders = new ByteCount[6];
            long rest = 0;
            int o = 0;

            foreach (BlobName blob in set.Blobs)
            {
                long length = blobs.TryGetValue(blob.Value, out long b) ? b : 0;

                if (o < orders.Length)
                {
                    orders[o++] = new ByteCount(length);
                }
                else
                {
                    rest += length;
                }
            }

            sets.Add(new StoredSet(set, orders, new ByteCount(rest)));
        }

        Dictionary<string, long> unnamed = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, long> set in Group(blobs))
        {
            if (!named.Contains(set.Key))
            {
                unnamed[set.Key] = set.Value;
            }
        }

        CatalogVersion current = catalog?.Version ?? new CatalogVersion(0);
        List<ReaderHolding> readers = [];

        foreach (ReaderRecord record in await ReadReadersAsync(store, names, cancellationToken).ConfigureAwait(false))
        {
            long retained = 0;

            foreach (KeyValuePair<string, long> set in unnamed)
            {
                if (record.State == ReaderRecordState.Announced || !record.Understood)
                {
                    if (CatalogSet.SequenceOf(new BlobName(set.Key)) is { } sequence && record.Announcement.Holds(sequence, current))
                    {
                        retained += set.Value;
                    }
                }
            }

            readers.Add(new ReaderHolding(record, new ByteCount(retained)));
        }

        long total = 0;

        foreach (long bytes in unnamed.Values)
        {
            total += bytes;
        }

        return new StorageReport { Catalog = catalog, Sets = sets, UnnamedSets = [.. Unnamed(unnamed)], RetainedBytes = new ByteCount(total), Readers = readers };
    }

    private static IEnumerable<BlobName> Unnamed(Dictionary<string, long> sets)
    {
        foreach (string name in sets.Keys)
        {
            yield return new BlobName(name);
        }
    }

    private static async ValueTask<IReadOnlyList<ReaderRecord>> ReadReadersAsync(IObjectStore store, ObjectNames names, CancellationToken cancellationToken)
    {
        List<ReaderRecord> records = [];

        foreach (ObjectEntry entry in await store.ListAsync(names.Readers, cancellationToken).ConfigureAwait(false))
        {
            if (names.ReaderOf(entry.Name) is not { } reader)
            {
                continue;
            }

            // Deleted since the list: forgotten, or closed.
            if (await ConditionalWrites.ReadWholeAsync(store, entry.Name, cancellationToken).ConfigureAwait(false) is { } read)
            {
                records.Add(ReaderRecordFormat.Read(reader, read.Bytes.Span));
            }
        }

        return records;
    }

    private static async ValueTask<IReadOnlyDictionary<string, long>> SetBytesAsync(IObjectStore store, ObjectNames names, CancellationToken cancellationToken)
    {
        Dictionary<string, long> blobs = new(StringComparer.Ordinal);
        string start = names.Derived(string.Empty).Value;

        foreach (ObjectEntry entry in await store.ListAsync(names.Extents, cancellationToken).ConfigureAwait(false))
        {
            blobs[entry.Name.Value[start.Length..]] = entry.Length.Value;
        }

        return Group(blobs);
    }

    // Blob lengths by extent set: each name up to the slash after its set's.
    private static Dictionary<string, long> Group(Dictionary<string, long> blobs)
    {
        Dictionary<string, long> sets = new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, long> blob in blobs)
        {
            int slash = blob.Key.IndexOf('/', "extents/".Length);

            if (slash < 0)
            {
                continue;
            }

            string set = blob.Key[..(slash + 1)];
            sets[set] = (sets.TryGetValue(set, out long bytes) ? bytes : 0) + blob.Value;
        }

        return sets;
    }

    // The record's current version when the record is this instance's own,
    // by its reader id and its holder; null when it is gone, of a format this
    // build cannot read, or another holder's.
    private async ValueTask<ObjectVersion?> OwnRecordVersionAsync(ObjectName name, CancellationToken cancellationToken)
    {
        ObjectRead? current = await ConditionalWrites.ReadWholeAsync(_store, name, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            return null;
        }

        ReaderRecord found = ReaderRecordFormat.Read(Reader, current.Bytes.Span);
        return found.Understood && found.Reader == Reader && found.Holder == Holder ? current.Version : null;
    }

    // Whether this instance's record no longer says what it last wrote:
    // forgotten, or written over by another instance of its id.
    private bool RecordLost()
    {
        if (_readerForgotten)
        {
            return true;
        }

        if (_readerVersion is not { } held)
        {
            return true;
        }

        ObjectRead? read = ConditionalWrites.ReadWholeAsync(_store, _names.Reader(Reader), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return read is null || read.Version != held;
    }

    // What a read that finds an extent gone throws (ADR 0160): never a wrong
    // result, and damage only when this instance's record still stands.
    private Exception Gone(ObjectName extent)
    {
        if (Announcing == ReaderAnnouncing.Unannounced)
        {
            return new UnannouncedExtentGoneException("The extent '" + extent + "' is gone from the store. This read was opened unannounced, so the writer may delete what it reads (ADR 0160); open it again.");
        }

        bool lost;

        try
        {
            lost = RecordLost();
        }
        catch (IOException)
        {
            lost = false;
        }

        return lost
            ? new ReaderForgottenException("The extent '" + extent + "' is gone from the store, and this instance's reader record '" + _names.Reader(Reader) + "' was forgotten; it attaches again (ADR 0160).")
            : new IOException("The extent '" + extent + "' is gone from the store, though a catalog version this instance announces names it: damage, or a deletion that broke the rule of ADR 0160.");
    }

    /// <summary>
    /// The reason a close releases the epoch with: <see cref="ReleaseReason.Shutdown"/>
    /// unless the host says otherwise, as it does when the dataset was placed
    /// on another instance (<see cref="ReleaseReason.Placement"/>, ADR 0163).
    /// </summary>
    public ReleaseReason ReleaseOnClose { get; set; } = ReleaseReason.Shutdown;

    /// <summary>
    /// Releases the writer epoch, when this storage holds it, with the reason
    /// <see cref="ReleaseOnClose"/>; then closes the cache. A release
    /// that cannot be written is left undone: the next writer's claim
    /// supersedes instead.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            if (_log.Writes && !IsSuperseded && _recordVersion is not null)
            {
                await ReleaseAsync(ReleaseOnClose, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
        }

        try
        {
            // A clean close announces nothing more: its record goes. One a
            // crash leaves holds deletion back until the same id opens again,
            // or the operator forgets it (ADR 0160).
            if (_readerVersion is not null && !_readerForgotten && !RecordLost())
            {
                await _store.DeleteAsync(_names.Reader(Reader), CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            _readerGate.Dispose();
            _log.Dispose();
            _derived.Dispose();
            await _local.DisposeAsync().ConfigureAwait(false);
        }
    }

    [DesignDecision(typeof(OneWriterByAnEpochAndALogSlotFence.WriterOpenClaimsTheNextEpoch), Scope = ExceptionScope.Boundary)]
    private static async ValueTask<ObjectStorage> OpenAsync(ObjectStorageOptions options, ClaimTrigger trigger, WriterEpoch? from, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Store);
        ArgumentNullException.ThrowIfNull(options.Clock);
        ArgumentException.ThrowIfNullOrEmpty(options.Cache.Value, nameof(options));

        if (options.Prefix is { IsPrefix: false } prefix)
        {
            throw new ArgumentException("The prefix '" + prefix + "' does not end in '/'.", nameof(options));
        }

        bool write = options.Access == ObjectStorageAccess.Write;

        if (write && options.Instance is null)
        {
            throw new ArgumentException("A writer names itself: set ObjectStorageOptions.Instance.", nameof(options));
        }

        if (write && options.ProbeRandomness is null)
        {
            throw new ArgumentException("A writer probes the store under a random key: set ObjectStorageOptions.ProbeRandomness (ADR 0134).", nameof(options));
        }

        ObjectNames names = new(options.Prefix);
        string copies = Path.Combine(options.Cache.Value, SegmentCopies);
        Directory.CreateDirectory(copies);
        FileStorage local = await FileStorage.OpenAsync(
            new DatasetDirectory(Path.Combine(options.Cache.Value, LocalStorage)),
            new FileStorageOptions { Clock = options.Clock },
            cancellationToken).ConfigureAwait(false);

        try
        {
            // Before a writer reads or writes anything: a store that does not
            // refuse what the fence and the writer record need refused is
            // never given a log (ADR 0134). A follower writes nothing, so a
            // store cannot be corrupted through it, and it does not probe.
            if (write)
            {
                await ProbeAsync(options.Store, names, options.ProbeRandomness!, options.Clock, cancellationToken).ConfigureAwait(false);
            }

            WriterRecord? record = null;
            ObjectVersion? version = null;
            bool lost = false;

            if (write)
            {
                (record, version) = await ClaimAsync(options.Store, names, options.Instance!.Value, options.Address, trigger, from, options.Clock, cancellationToken).ConfigureAwait(false);
                lost = version is null;
            }
            else if (await ConditionalWrites.ReadWholeAsync(options.Store, names.Writer, cancellationToken).ConfigureAwait(false) is { } read)
            {
                record = WriterRecordFormat.Read(read.Bytes.Span);
            }

            long epoch = write && !lost ? record!.Epoch.Value : 0;
            ObjectSegmentStore log = await ObjectSegmentStore.OpenAsync(options.Store, names, options.Clock, copies, epoch, cancellationToken).ConfigureAwait(false);
            ObjectDerivedStore derived = new(
                options.Store, names, options.Clock, local.Derived, Path.Combine(options.Cache.Value, ExtentPages), Path.Combine(options.Cache.Value, StagedUploads), options.CacheBytes.Value, options.MaxObjectBytes.Value);
            ObjectStorage storage = new(options.Store, names, options.Clock, log, local, derived, record, version, write && lost)
            {
                Reader = options.Reader ?? KeptReaderId(options.Cache.Value, options.ProbeRandomness),
                Announcing = write ? ReaderAnnouncing.Required : options.Announcing,
                Holder = options.Instance,
            };
            derived.Gone = storage.Gone;

            if (write && !lost && !log.Superseded && log.Fence is { } fence)
            {
                await storage.RecordFenceAsync(fence, cancellationToken).ConfigureAwait(false);

                // The catalog under the new epoch, so no older writer swaps it (ADR 0139).
                await derived.FenceCatalogAsync(epoch, cancellationToken).ConfigureAwait(false);
            }

            return storage;
        }
        catch
        {
            await local.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Claims the epoch after the one the record shows, or the first. Returns
    // the record and its version, or no version when the claim was lost: a
    // concurrent claimant won, or the record no longer showed the epoch an
    // operator named. A lost claim is not retried (ADR 0136): retrying would
    // supersede the winner.
    private static async ValueTask<(WriterRecord? Record, ObjectVersion? Version)> ClaimAsync(
        IObjectStore store, ObjectNames names, WriterInstance instance, WriterAddress? address, ClaimTrigger trigger, WriterEpoch? from, TimeProvider clock, CancellationToken cancellationToken)
    {
        ObjectRead? read = await ConditionalWrites.ReadWholeAsync(store, names.Writer, cancellationToken).ConfigureAwait(false);
        WriterRecord? current = read is null ? null : WriterRecordFormat.Read(read.Bytes.Span);

        if (from is { } named && current?.Epoch != named)
        {
            return (current, null);
        }

        WriterEpoch epoch = current?.Epoch.Next ?? new WriterEpoch(1);
        List<EpochClaim> history = current is null ? [] : [.. current.History];
        history.Add(new EpochClaim { Epoch = epoch, Holder = instance, Trigger = trigger, ClaimedAt = new ClaimTime(clock.GetUtcNow()), Address = address });
        WriterRecord claimed = new() { Epoch = epoch, Holder = instance, History = history };
        byte[] bytes = WriterRecordFormat.Write(claimed);

        if (read is null)
        {
            (CreateOutcome outcome, ReadOnlyMemory<byte> existing) = await ConditionalWrites.CreateAsync(store, names.Writer, bytes, clock, cancellationToken).ConfigureAwait(false);

            if (outcome == CreateOutcome.Taken)
            {
                return (WriterRecordFormat.Read(existing.Span), null);
            }

            ObjectRead? written = await ConditionalWrites.ReadWholeAsync(store, names.Writer, cancellationToken).ConfigureAwait(false);
            return written is not null && written.Bytes.Span.SequenceEqual(bytes) ? (claimed, written.Version) : (current, null);
        }

        ObjectVersion? version = await ConditionalWrites.ReplaceAsync(store, names.Writer, read.Version, bytes, clock, cancellationToken).ConfigureAwait(false);
        return version is null ? (current, null) : (claimed, version);
    }

    // The probe (ADR 0134, ConditionalWritesProbedAtOpen): under a random key
    // of the reserved probe/, a create-if-absent, a second one
    // with other bytes, which must be refused, a replace against the version
    // the first returned, which must succeed, and the same replace again,
    // now against a stale version, which must be refused. The key is deleted.
    private static async ValueTask ProbeAsync(IObjectStore store, ObjectNames names, IProbeRandomness randomness, TimeProvider clock, CancellationToken cancellationToken)
    {
        // A random name per open: two probes on one key could make a correct
        // store look broken. A key a crash leaves behind stays; nothing reads it.
        byte[] random = new byte[16];
        randomness.Fill(random);
        ObjectName key = names.Probe(Convert.ToHexStringLower(random));

        try
        {
            await ConditionalWrites.CreateAsync(store, key, "1"u8.ToArray(), clock, cancellationToken).ConfigureAwait(false);
            ObjectRead first = await ConditionalWrites.ReadWholeAsync(store, key, cancellationToken).ConfigureAwait(false)
                ?? throw new System.IO.IOException("The object store at " + store + " lost '" + key + "' just after creating it.");
            (CreateOutcome second, _) = await ConditionalWrites.CreateAsync(store, key, "2"u8.ToArray(), clock, cancellationToken).ConfigureAwait(false);

            if (second == CreateOutcome.Ours)
            {
                throw NotEnforced(store, "a second create-if-absent of '" + key + "' succeeded");
            }

            if (await ConditionalWrites.ReplaceAsync(store, key, first.Version, "3"u8.ToArray(), clock, cancellationToken).ConfigureAwait(false) is null)
            {
                throw NotEnforced(store, "a replace-if-unchanged of '" + key + "' against its current version was refused");
            }

            if (await ConditionalWrites.ReplaceAsync(store, key, first.Version, "4"u8.ToArray(), clock, cancellationToken).ConfigureAwait(false) is not null)
            {
                throw NotEnforced(store, "a replace-if-unchanged of '" + key + "' against a stale version succeeded");
            }
        }
        finally
        {
            // Best effort: a probe key left behind is never read again.
            try
            {
                await store.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (System.Net.Http.HttpRequestException)
            {
            }
        }
    }

    // The reader id kept in the cache directory, written once with a random
    // value when there is none, so a restart that keeps its cache keeps its id.
    private static ReaderId KeptReaderId(string cache, IProbeRandomness? randomness)
    {
        string path = Path.Combine(cache, ReaderIdFile);

        if (File.Exists(path) && File.ReadAllText(path).Trim() is { } kept && ReaderId.IsValid(kept))
        {
            return new ReaderId(kept);
        }

        if (randomness is null)
        {
            throw new ArgumentException("A reader names its record: set ObjectStorageOptions.Reader, or ProbeRandomness for a random id kept in the cache directory (ADR 0160).", nameof(randomness));
        }

        byte[] random = new byte[16];
        randomness.Fill(random);
        string id = Convert.ToHexStringLower(random);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, id);
        File.Move(temporary, path, overwrite: true);
        return new ReaderId(id);
    }

    private static ConditionalWritesNotEnforcedException NotEnforced(IObjectStore store, string seen) =>
        new("The object store at " + store + " does not enforce conditional writes: " + seen + ". The epoch fence and the writer record rest on those refusals, so the object-storage backend refuses this store (ADR 0134). Garage is one such store; keep the dataset on the file backend instead.");

    private async ValueTask RecordFenceAsync(LogSlot fence, CancellationToken cancellationToken)
    {
        WriterRecord record = Writer!;
        List<EpochClaim> history = [.. record.History];
        history[^1] = history[^1] with { Fence = fence };
        await ReplaceRecordAsync(record with { History = history }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ReleaseAsync(ReleaseReason reason, CancellationToken cancellationToken)
    {
        WriterRecord record = Writer!;
        List<EpochClaim> history = [.. record.History];
        history[^1] = history[^1] with { Released = reason };
        await ReplaceRecordAsync(record with { Released = reason, History = history }, cancellationToken).ConfigureAwait(false);
        _recordVersion = null;
    }

    // A replace against the version this writer last wrote. Lost means a newer
    // claim was made since: that writer's fence stops this one, so nothing
    // more is needed here.
    private async ValueTask ReplaceRecordAsync(WriterRecord record, CancellationToken cancellationToken)
    {
        if (_recordVersion is not { } expected)
        {
            return;
        }

        ObjectVersion? version = await ConditionalWrites.ReplaceAsync(_store, _names.Writer, expected, WriterRecordFormat.Write(record), _clock, cancellationToken).ConfigureAwait(false);

        if (version is not null)
        {
            Writer = record;
        }

        _recordVersion = version;
    }
}
