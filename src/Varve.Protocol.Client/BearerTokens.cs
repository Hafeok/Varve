// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// Providers of <see cref="BearerToken"/>s (ADR 0127, amended 2026-10-27). They
/// sit beside <see cref="BearerTokenProvider"/>, not in the model, which holds
/// values only (ADR 0206).
/// </summary>
public static class BearerTokens
{
    /// <summary>
    /// A provider that answers <paramref name="value"/> for every request, as
    /// one that never expires: a token given on the command line, or a test's.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    [DesignDecision(typeof(OnBehalfOfAndTheTokenProvider.TokenIsOpaqueAtTheBoundary), Scope = ExceptionScope.Boundary)]
    public static BearerTokenProvider Fixed(string value)
    {
        BearerToken token = new(value, DateTimeOffset.MaxValue);
        return _ => ValueTask.FromResult<BearerToken?>(token);
    }
}
