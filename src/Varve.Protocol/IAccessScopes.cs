// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Claims;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Rdf;

namespace Varve.Protocol;

/// <summary>
/// The graphs a caller reads and writes of a dataset, and whether it
/// administers it (ADR 0106): resolved by the host from its grants, after
/// the dataset-level policy allowed the request. The protocol takes the
/// answer as a value and knows no claim; a host without graph-level grants
/// answers <see cref="CallerScope.Everything"/>.
/// </summary>
[Contract(typeof(GraphLevelAuthorisation.AccessScopesSeam), Role = "the host's resolution of a caller's graph-level scope on a dataset")]
public interface IAccessScopes
{
    /// <summary>What <paramref name="caller"/> may do with <paramref name="dataset"/>.</summary>
    CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset);
}

/// <summary>Every caller reads, writes and administers every graph: anonymous mode's and the conformance host's answer.</summary>
public sealed class EveryoneEverything : IAccessScopes
{
    /// <summary>The one instance.</summary>
    public static EveryoneEverything Instance { get; } = new();

    /// <inheritdoc />
    public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => CallerScope.Everything;
}
