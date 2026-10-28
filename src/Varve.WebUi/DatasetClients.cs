// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;

namespace Varve.WebUi;

/// <summary>
/// A data-plane client per dataset (ADR 0203), with the data plane's token when
/// signed in, so a page reads one dataset without composing a client itself.
/// </summary>
public sealed class DatasetClients(HttpClient http, Uri server, BearerTokenProvider? tokens, TimeProvider clock)
{
    /// <summary>The client of <paramref name="dataset"/>.</summary>
    public SparqlHttpClient For(DatasetName dataset)
    {
        Uri address = new(server, "datasets/" + Uri.EscapeDataString(dataset.Value) + "/");
        return tokens is null ? new SparqlHttpClient(http, address, ClientLimits.Default) : new SparqlHttpClient(http, address, ClientLimits.Default, tokens, clock);
    }
}
