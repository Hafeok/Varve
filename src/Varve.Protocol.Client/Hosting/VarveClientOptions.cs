// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Hosting;

/// <summary>What <see cref="VarveServiceCollectionExtensions.AddVarve"/> registers (ADR 0131).</summary>
public sealed class VarveClientOptions
{
    /// <summary>The server's address, such as <c>https://varve.example/</c>; required.</summary>
    public Uri? Server { get; set; }

    /// <summary>The dataset's name; required, or taken from <see cref="Setup"/>.</summary>
    public string? Dataset { get; set; }

    /// <summary>
    /// The token provider (ADR 0127), made from the application's services:
    /// on behalf of the request's user, or the application's own client
    /// credentials. Null sends no token.
    /// </summary>
    public Func<IServiceProvider, BearerTokenProvider>? Tokens { get; set; }

    /// <summary>The clock a token's expiry is measured on; required with <see cref="Tokens"/>, since the client reads no clock of its own (ADR 0011).</summary>
    public TimeProvider? Clock { get; set; }

    /// <summary>How long a request may take to its headers, and how much response is read.</summary>
    public ClientLimits Limits { get; set; } = ClientLimits.Default;

    /// <summary>The dataset's setup (ADR 0130), or null for none.</summary>
    public DatasetSetup? Setup { get; set; }

    /// <summary>Whether the setup is applied when the host starts; on by default when there is a setup.</summary>
    public bool ApplySetupAtStart { get; set; } = true;
}
