// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Protocol;

/// <summary>
/// The authorisation policy names the endpoints require (ADRs 0037, 0091). The
/// host registers a policy under each and decides, per dataset, what satisfies
/// it; the dataset is the route's <c>dataset</c> value.
/// </summary>
public static class DatasetPermissions
{
    /// <summary>Queries, Graph Store reads, the service description, the feed and the diff.</summary>
    public const string Read = "varve:read";

    /// <summary>Updates and Graph Store writes.</summary>
    public const string Write = "varve:write";

    /// <summary>The dataset's status, settings, checkpoints and projections.</summary>
    public const string Admin = "varve:admin";

    /// <summary>
    /// Creating, opening, closing and deleting datasets (ADR 0105), decided on
    /// no dataset: the resource is null. A server admin administers every
    /// dataset too, which the host's policy for <see cref="Admin"/> decides.
    /// </summary>
    public const string ServerAdmin = "varve:server-admin";
}
