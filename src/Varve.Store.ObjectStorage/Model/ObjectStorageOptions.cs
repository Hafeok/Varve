// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>How to open a dataset in an object store.</summary>
public sealed class ObjectStorageOptions
{
    /// <summary>A test's seam: fails a local cache's next write when it answers true (#146).</summary>
    internal System.Func<LocalCache, bool>? CacheFault { get; init; }

    /// <summary>The object store: an adapter for one provider (ADR 0134).</summary>
    public required IObjectStore Store { get; init; }

    /// <summary>
    /// The prefix every name of this dataset begins with, ending in <c>/</c>;
    /// none when the dataset has the container to itself.
    /// </summary>
    public ObjectName? Prefix { get; init; }

    /// <summary>The local directory to cache in. One process uses it at a time.</summary>
    public required CacheDirectory Cache { get; init; }

    /// <summary>
    /// The most the cache holds of extents' pages (ADR 0139); past it the
    /// least recently read extent's pages are dropped and fetched again when
    /// read. 8 GiB unless set.
    /// </summary>
    public ByteCount CacheBytes { get; init; } = new(8L << 30);

    /// <summary>Write, which claims the writer epoch, or read-only, which claims nothing.</summary>
    public ObjectStorageAccess Access { get; init; } = ObjectStorageAccess.Write;

    /// <summary>Who this process is, as the writer record names it. Required for write.</summary>
    public WriterInstance? Instance { get; init; }

    /// <summary>
    /// Where the other instances of a fleet reach this one, recorded with its
    /// claim (ADR 0168). Optional: a fleet of one forwards nothing.
    /// </summary>
    public WriterAddress? Address { get; init; }

    /// <summary>
    /// Where a writer's open draws the random name of its conditional-writes
    /// probe (ADR 0134). Required for write; a read-only open does not probe.
    /// </summary>
    public IProbeRandomness? ProbeRandomness { get; init; }

    /// <summary>
    /// This instance's reader id, the name of its reader record (ADR 0160).
    /// It survives a restart of the same instance, so a restart replaces the
    /// record the last run left: set it where the cache directory does not
    /// survive one — a container's — and leave it unset to keep a random one
    /// in the cache directory.
    /// </summary>
    public ReaderId? Reader { get; init; }

    /// <summary>
    /// Whether the open writes a reader record. <see cref="ReaderAnnouncing.Required"/>
    /// unless a read-only open asks explicitly for <see cref="ReaderAnnouncing.Unannounced"/>.
    /// </summary>
    public ReaderAnnouncing Announcing { get; init; } = ReaderAnnouncing.Required;

    /// <summary>
    /// The most one object may be written with in one request: the provider's
    /// single-upload limit (ADR 0158). 5,000 MiB, Azure Blob Storage's, unless
    /// set; Amazon S3's is 5 GiB.
    /// </summary>
    public ByteCount MaxObjectBytes { get; init; } = new(5000L << 20);

    /// <summary>
    /// The clock the writer record's timestamps are read from, and retries
    /// wait on. Required: the store never reads the machine's clock itself
    /// (ADR 0011). Nothing is decided from it (ADR 0136).
    /// </summary>
    public required TimeProvider Clock { get; init; }
}
