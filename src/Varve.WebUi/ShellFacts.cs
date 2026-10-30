// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;

namespace Varve.WebUi;

/// <summary>
/// What the shell shows that is not the screen's (review F01, F22): the
/// instance that answered, with its role in the system dataset, and the
/// datasets the navigation's selector offers. A session service the layout
/// reads, as it reads the sign-in: read again on every navigation, since
/// behind one address each read may reach another instance. No component
/// below a page calls the API; this does, for the shell.
/// </summary>
public sealed class ShellFacts(VarveManagementClient management)
{
    /// <summary>Raised when a read changed what the shell shows.</summary>
    public event Action? Changed;

    /// <summary>The instance that answered: "varve-1 · system writer", or the role alone while the server does not name the instance (#193).</summary>
    public string? Instance { get; private set; }

    /// <summary>The datasets the selector offers, by name.</summary>
    public IReadOnlyList<string> Datasets { get; private set; } = [];

    /// <summary>The dataset last viewed, which the selector defaults to.</summary>
    public string? LastDataset { get; set; }

    /// <summary>Reads <c>/manage/system</c> and the dataset list. What cannot be read is not shown; the screen says why.</summary>
    public async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Task<VarveResult<SystemPlane>> system = management.SystemAsync(cancellationToken);
            Task<VarveResult<DatasetList>> list = management.DatasetsAsync(cancellationToken);
            Instance = (await system.ConfigureAwait(false)).Value is { } plane ? Describe(plane) : null;

            if ((await list.ConfigureAwait(false)).Value is { } datasets)
            {
                Datasets = [.. datasets.Datasets.Select(dataset => dataset.Name.Value).Order(StringComparer.Ordinal)];
            }
        }
        catch (HttpRequestException)
        {
            Instance = null;
        }

        Changed?.Invoke();
    }

    /// <summary>The instance's name, when the server serves it, and its role in the system dataset.</summary>
    internal static string? Describe(SystemPlane plane)
    {
        if (plane.Role is not { } role)
        {
            return null;
        }

        string system = "system " + role.Value;
        return plane.Members.TryGetValue("instance", out JsonElement name) && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } instance
            ? instance + " · " + system
            : system;
    }
}
