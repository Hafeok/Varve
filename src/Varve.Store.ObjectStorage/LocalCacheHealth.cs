// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// Which local caches still work (#146: the cache is a miss, never a stop). A
/// failure writing one turns its kind off for the life of the storage, and
/// what it would have held is read from the store instead. The test seam
/// <see cref="Fault"/> fails a kind's next write as the disk would.
/// </summary>
internal sealed class LocalCacheHealth
{
    private int _off;

    /// <summary>A test's injected fault: true fails the kind's next local write.</summary>
    internal Func<LocalCache, bool>? Fault { get; init; }

    /// <summary>The kinds turned off.</summary>
    internal LocalCache Off => (LocalCache)Volatile.Read(ref _off);

    internal bool IsOff(LocalCache kind) => (Off & kind) != 0;

    /// <summary>Turns <paramref name="kind"/> off.</summary>
    internal void TurnOff(LocalCache kind)
    {
        int current;

        do
        {
            current = Volatile.Read(ref _off);
        }
        while (Interlocked.CompareExchange(ref _off, current | (int)kind, current) != current);
    }

    /// <summary>Throws as a failed local write would, when the test seam says to.</summary>
    /// <exception cref="IOException">The injected fault.</exception>
    internal void Check(LocalCache kind)
    {
        if (Fault is { } fault && fault(kind))
        {
            throw new IOException("An injected fault of the local " + kind + " cache.");
        }
    }

    /// <summary>Whether <paramref name="error"/> is a local file's failure, as a full or failing disk gives.</summary>
    internal static bool IsLocal(Exception error) => error is IOException or UnauthorizedAccessException;
}
