// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Aspire.Hosting.ApplicationModel;

namespace Varve.Aspire;

/// <summary>
/// A Varve server in an Aspire application model (ADR 0117): the container
/// of ADR 0111, with its datasets volume, its <c>http</c> endpoint on 8080
/// and <c>GET /health/ready</c> as its health check.
/// </summary>
public sealed class VarveResource : ContainerResource
{
    /// <summary>The name of the endpoint the server listens on.</summary>
    public const string HttpEndpointName = "http";

    /// <summary>Creates the resource named <paramref name="name"/>.</summary>
    public VarveResource(string name)
        : base(name)
    {
        HttpEndpoint = new EndpointReference(this, HttpEndpointName);
    }

    /// <summary>The server's endpoint: <c>http://…:8080</c> in the container, whatever Aspire allocated on the host.</summary>
    public EndpointReference HttpEndpoint { get; }

    /// <summary>The authentication <c>WithOidc</c> configured, or null while the resource is anonymous.</summary>
    internal VarveOidc? Oidc { get; set; }

    /// <summary>The datasets <c>WithDataset</c> declared, with the roles each grants.</summary>
    internal List<VarveDataset> Datasets { get; } = [];
}

/// <summary>What <c>WithOidc</c> set (ADR 0037's bearer-token mode).</summary>
internal sealed record VarveOidc(ReferenceExpression Authority, string Audience, string? RoleClaimType, string? SubjectClaim, bool RequireHttpsMetadata);

/// <summary>A dataset the server opens at start, and the roles that may read and write it (ADR 0107).</summary>
internal sealed record VarveDataset(string Name, IReadOnlyList<string> Read, IReadOnlyList<string> Write, IReadOnlyList<string> Admin);
