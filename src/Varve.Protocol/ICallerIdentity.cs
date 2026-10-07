// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// Who a write's commit names as its agent. The host's, from the token it
/// validated: the issuer's IRI, <c>#</c>, and the subject (ADR 0094). In
/// anonymous mode, <see cref="RequestTerm.None"/>.
/// </summary>
[Contract(typeof(VarveProtocolAndVarveServer.CallerIdentitySeam), Role = "the host's naming of the authenticated caller as a commit agent")]
public interface ICallerIdentity
{
    /// <summary>The agent for a commit made by <paramref name="caller"/>.</summary>
    RequestTerm AgentOf(ClaimsPrincipal caller);
}
