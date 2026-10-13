// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// The token for one request (ADR 0127): called for every request the client
/// sends, so one client serves many users when the provider reads the current
/// user from the caller's own context. Null sends the request unauthenticated.
/// Caching is the provider's, as it is MSAL's.
/// </summary>
[Contract(typeof(OnBehalfOfAndTheTokenProvider.TokenProviderPerRequest), Role = "the caller's source of the access token for each request, one user's or another's")]
public delegate ValueTask<BearerToken?> BearerTokenProvider(CancellationToken cancellationToken);

