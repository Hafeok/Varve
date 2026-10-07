// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The configured datasets, opened before the server listens and closed after
/// it stops (ADRs 0093, 0101). The name is the host's: a File dataset is the
/// directory of that name under the root, created on first open.
/// </summary>
internal sealed class OpenDatasets : IDatasetResolver, IAsyncDisposable
{
    private readonly Dictionary<string, (Dataset Dataset, FileStorage? Files)> _open = new(StringComparer.Ordinal);

    private OpenDatasets()
    {
    }

    /// <summary>The datasets by name, for readiness.</summary>
    internal IEnumerable<(string Name, Dataset Dataset)> All
    {
        get
        {
            foreach ((string name, (Dataset dataset, _)) in _open)
            {
                yield return (name, dataset);
            }
        }
    }

    internal static async Task<OpenDatasets> OpenAsync(ServerSettings settings, TimeProvider clock, CancellationToken cancellationToken)
    {
        OpenDatasets opened = new();

        try
        {
            foreach ((string name, DatasetSettings configured) in settings.Datasets)
            {
                DatasetOptions options = new() { Clock = clock };

                if (configured.Storage == "Memory")
                {
                    opened._open[name] = (await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), null);
                    continue;
                }

                string directory = Path.Combine(settings.DatasetsRoot!, name);
                bool exists = Directory.Exists(Path.Combine(directory, "log"));
                FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = clock }, cancellationToken).ConfigureAwait(false);

                try
                {
                    Dataset dataset = exists
                        ? await Dataset.OpenAsync(files, options, cancellationToken).ConfigureAwait(false)
                        : await Dataset.CreateAsync(files, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
                    opened._open[name] = (dataset, files);
                }
                catch
                {
                    await files.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        catch
        {
            await opened.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return opened;
    }

    public bool TryResolve(DatasetName name, [NotNullWhen(true)] out Dataset? dataset)
    {
        if (_open.TryGetValue(name.Value, out (Dataset Dataset, FileStorage? Files) entry))
        {
            dataset = entry.Dataset;
            return true;
        }

        dataset = null;
        return false;
    }

    /// <summary>
    /// Closes every dataset: each drains its sequencer, so a commit in progress
    /// finishes, then releases its lease (ADR 0101). The active segment is not
    /// sealed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach ((Dataset dataset, FileStorage? files) in _open.Values)
        {
            await dataset.DisposeAsync().ConfigureAwait(false);

            if (files is not null)
            {
                await files.DisposeAsync().ConfigureAwait(false);
            }
        }

        _open.Clear();
    }
}
