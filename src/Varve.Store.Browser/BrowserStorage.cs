// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Browser.Model;

namespace Varve.Store.Browser;

/// <summary>
/// A dataset's storage in a browser (ADR 0084): the origin private file system
/// where .NET runs in a dedicated worker, and IndexedDB where it does not.
/// </summary>
/// <remarks>
/// <para>
/// **Derived blobs are read synchronously** (ADR 0071). On the origin private
/// file system that is <c>FileSystemSyncAccessHandle.read</c>, called through
/// <c>[JSImport]</c> into the caller's span; it exists only in a dedicated
/// worker, so that is where .NET has to run for this backend. IndexedDB has no
/// synchronous read at all, so its backend reads a blob into memory when the
/// blob is opened, and serves every read after that from the copy: an open
/// blob costs its length in memory there, and nowhere else.
/// </para>
/// <para>
/// **The two backends are two places.** A dataset written through one is not
/// visible through the other under the same name: a host that runs .NET in a
/// worker on one page and on the page's thread on another has two datasets.
/// </para>
/// <para>
/// **One opener at a time**, in every tab and worker of the origin (ADR 0075's
/// rule, held by the browser): the file system backend holds an exclusive
/// synchronous access handle on <c>derived/LOCK</c>, the IndexedDB backend a
/// Web Lock named after the dataset. The browser releases either when the
/// worker or page that took it goes away. A second opener is refused with
/// <see cref="DatasetLeasedException"/>. <see cref="DisposeAsync"/> releases it.
/// </para>
/// <para>
/// Maintenance is off by default in a browser (<see cref="DatasetOptions.Maintenance"/>,
/// ADR 0042's amendment); a host drives it with <see cref="Dataset.MaintainAsync"/>.
/// </para>
/// </remarks>
[Contract(typeof(TheBrowserBackend.OpfsInADedicatedWorker), Role = "a dataset's storage in a browser: the origin private file system in a worker, IndexedDB elsewhere")]
public abstract class BrowserStorage : IStorage, IAsyncDisposable
{
    private protected BrowserStorage(BrowserDatasetName name) => Name = name;

    /// <summary>The dataset's name in the origin's storage.</summary>
    public BrowserDatasetName Name { get; }

    /// <summary>Which storage this is.</summary>
    public abstract BrowserBackend Backend { get; }

    /// <inheritdoc />
    public abstract ISegmentStore Log { get; }

    /// <inheritdoc />
    public abstract IDerivedStore Derived { get; }

    /// <summary>
    /// Whether the origin private file system backend can open here: in a
    /// dedicated worker of a browser with synchronous access handles and
    /// <c>move()</c>.
    /// </summary>
    public static async ValueTask<bool> IsOriginPrivateFileSystemAvailableAsync(CancellationToken cancellationToken = default)
    {
        await Interop.EnsureImportedAsync(cancellationToken).ConfigureAwait(false);
        return Interop.OpfsAvailable();
    }

    /// <summary>
    /// Opens the dataset in the strongest storage this context has: the origin
    /// private file system when synchronous access handles exist, IndexedDB
    /// otherwise.
    /// </summary>
    /// <exception cref="DatasetLeasedException">The dataset is open elsewhere in this origin.</exception>
    /// <exception cref="PlatformNotSupportedException">Neither storage exists here.</exception>
    public static async ValueTask<BrowserStorage> OpenAsync(BrowserDatasetName name, BrowserStorageOptions options, CancellationToken cancellationToken = default) =>
        await IsOriginPrivateFileSystemAvailableAsync(cancellationToken).ConfigureAwait(false)
            ? await OpenOriginPrivateFileSystemAsync(name, options, cancellationToken).ConfigureAwait(false)
            : await OpenIndexedDbAsync(name, options, cancellationToken).ConfigureAwait(false);

    /// <summary>Opens the dataset in the origin private file system.</summary>
    /// <exception cref="DatasetLeasedException">The dataset is open elsewhere in this origin.</exception>
    /// <exception cref="PlatformNotSupportedException">Synchronous access handles do not exist here: not a dedicated worker, or a browser without them.</exception>
    public static async ValueTask<BrowserStorage> OpenOriginPrivateFileSystemAsync(BrowserDatasetName name, BrowserStorageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Clock);
        await Interop.EnsureImportedAsync(cancellationToken).ConfigureAwait(false);

        if (!Interop.OpfsAvailable())
        {
            throw new PlatformNotSupportedException("Synchronous access handles to the origin private file system exist only in a dedicated worker of a browser that has them; open IndexedDB instead, or run .NET in a worker (ADR 0084).");
        }

        return await OpfsStorage.OpenHereAsync(name, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens the dataset in IndexedDB.</summary>
    /// <exception cref="DatasetLeasedException">The dataset is open elsewhere in this origin.</exception>
    /// <exception cref="PlatformNotSupportedException">IndexedDB or Web Locks do not exist here.</exception>
    public static async ValueTask<BrowserStorage> OpenIndexedDbAsync(BrowserDatasetName name, BrowserStorageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Clock);
        await Interop.EnsureImportedAsync(cancellationToken).ConfigureAwait(false);

        if (!Interop.IndexedDbAvailable())
        {
            throw new PlatformNotSupportedException("IndexedDB and Web Locks are not available in this context.");
        }

        return await IndexedDbStorage.OpenHereAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the lease and every open handle.</summary>
    public abstract ValueTask DisposeAsync();
}
