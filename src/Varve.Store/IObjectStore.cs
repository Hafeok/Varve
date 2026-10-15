// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// An object store with conditional writes: the five operations the
/// object-storage backend is written against, and all it asks of a provider
/// (ADR 0134).
/// </summary>
/// <remarks>
/// <para>
/// Every mainstream object store offers exactly these: Azure Blob Storage by
/// <c>If-None-Match: *</c> and <c>If-Match</c>, Amazon S3 by its conditional
/// writes, Google Cloud Storage by generation preconditions. A provider is an
/// adapter in a package of its own; the backend never knows which it has.
/// </para>
/// <para>
/// **A write's outcome may be unknown.** An exception from
/// <see cref="CreateIfAbsentAsync"/> or <see cref="ReplaceIfUnchangedAsync"/>
/// says nothing about whether the store applied it; the caller finds out by
/// reading. A lost race is not an exception: it is the <c>null</c> these
/// return.
/// </para>
/// <para>
/// Reads, lists and the read that follows a write are strongly consistent:
/// once a write has returned, every later read and list sees it. A provider
/// that cannot promise that cannot be adapted.
/// </para>
/// </remarks>
[Contract(typeof(TheObjectStoreContract.FiveOperations), Role = "an object store with conditional writes, as the object-storage backend sees one")]
public interface IObjectStore
{
    /// <summary>
    /// Writes an object only if no object of that name exists. Returns the new
    /// object's version, or <c>null</c> when one already existed and nothing
    /// was written.
    /// </summary>
    ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an object only if its current version is
    /// <paramref name="expected"/>. Returns the new version, or <c>null</c>
    /// when the object has another version or none, and nothing was written.
    /// </summary>
    ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="length"/> bytes of an object from
    /// <paramref name="offset"/>, with the version they belong to. Returns
    /// fewer only at the object's end, and <c>null</c> when there is no object
    /// of that name.
    /// </summary>
    ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken);

    /// <summary>Every object whose name begins with <paramref name="prefix"/>, in ordinal order.</summary>
    ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName prefix, CancellationToken cancellationToken);

    /// <summary>Deletes an object. Returns <c>false</c> when there was none.</summary>
    ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken);
}
