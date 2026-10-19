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

    /// <summary>The container <c>WithAzureBlobStorage</c> put the datasets in, or null while they are files on the volume.</summary>
    internal ReferenceExpression? BlobContainer { get; set; }

    /// <summary>The datasets <c>WithDataset</c> declared, with the roles each grants.</summary>
    internal List<VarveDataset> Datasets { get; } = [];

    /// <summary>The bootstrap operators <c>WithOperators</c> named: agent IRIs or role values (ADR 0163 point 7).</summary>
    internal List<string> Operators { get; } = [];

    /// <summary>The client credentials <c>WithManagementClient</c> gave the declarations container, under <c>WithOidc</c>.</summary>
    internal VarveSetupClient? ManagementClient { get; set; }

    /// <summary>The container that applies the declarations with <c>varve apply</c>, once a dataset is declared.</summary>
    internal IResourceBuilder<ContainerResource>? Declare { get; set; }

    /// <summary>The directory the declarations file is written to, mounted into <see cref="Declare"/>.</summary>
    internal string? DeclarationsDirectory { get; set; }

    /// <summary>The client credentials <c>WithSetupClient</c> gave the setup containers, under <c>WithOidc</c>.</summary>
    internal VarveSetupClient? SetupClient { get; set; }

    /// <summary>The setup containers <c>WithDataset</c> added, by dataset (ADR 0131).</summary>
    internal Dictionary<string, IResourceBuilder<ContainerResource>> Setups { get; } = new(System.StringComparer.Ordinal);
}

/// <summary>The identity a setup container applies its file with (ADR 0131).</summary>
internal sealed record VarveSetupClient(string ClientId, ParameterResource Secret, string? Scope);

/// <summary>What <c>WithOidc</c> set (ADR 0037's bearer-token mode).</summary>
internal sealed record VarveOidc(ReferenceExpression Authority, string Audience, string? RoleClaimType, string? SubjectClaim, bool RequireHttpsMetadata, string ManagementAudience);

/// <summary>A dataset the server opens at start, and the roles that may read and write it (ADR 0107).</summary>
internal sealed record VarveDataset(string Name, IReadOnlyList<string> Read, IReadOnlyList<string> Write, IReadOnlyList<string> Admin);
