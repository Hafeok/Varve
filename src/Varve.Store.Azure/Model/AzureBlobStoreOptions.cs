// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;

namespace Varve.Store.Azure.Model;

/// <summary>Where an Azure Blob Storage container is, and how to reach it.</summary>
public sealed class AzureBlobStoreOptions
{
    /// <summary>
    /// The container's address: <c>https://{account}.blob.core.windows.net/{container}</c>,
    /// or Azurite's <c>http://127.0.0.1:10000/devstoreaccount1/{container}</c>.
    /// No query: a SAS token is refused (ADR 0137).
    /// </summary>
    public required Uri ContainerUrl { get; init; }

    /// <summary>
    /// The clock a token's expiry and a retry's wait are read from. Required:
    /// the adapter never reads the machine's clock itself (ADR 0011).
    /// </summary>
    public required TimeProvider Clock { get; init; }

    /// <summary>
    /// The handler requests go through, when the host has one of its own; a
    /// plain <see cref="SocketsHttpHandler"/> otherwise. The caller disposes it.
    /// </summary>
    public HttpMessageHandler? Handler { get; init; }
}
