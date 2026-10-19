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
public sealed class ObjectStorage : ITailingStorage, IStorageMaintenance, IAsyncDisposable
{
    private const string SegmentCopies = "segments";
    private const string LocalStorage = "local";
    private const string ExtentPages = "pages";

    private readonly IObjectStore _store;
    private readonly ObjectNames _names;
    private readonly TimeProvider _clock;
    private readonly ObjectSegmentStore _log;
    private readonly FileStorage _local;
    private readonly ObjectDerivedStore _derived;
    private ObjectVersion? _recordVersion;

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

    /// <summary>Test seam: the catalog version a follower holds (ADR 0164, #110's hook).</summary>
    internal ObjectVersion? HeldCatalogForTests => _derived.HeldCatalog;

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

    /// <inheritdoc />
    public ValueTask<bool> MaintainStorageAsync(CancellationToken cancellationToken) => _log.ConsolidateAsync(cancellationToken);

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
        finally
        {
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
            ObjectDerivedStore derived = new(options.Store, names, options.Clock, local.Derived, Path.Combine(options.Cache.Value, ExtentPages), options.CacheBytes.Value);
            ObjectStorage storage = new(options.Store, names, options.Clock, log, local, derived, record, version, write && lost);

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
