// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace Varve.Aspire;

/// <summary>
/// <c>AddVarve</c>, <c>WithOidc</c> and <c>WithDataset</c> on an AppHost
/// (ADR 0117). The integration references nothing from Varve: it names the
/// image of ADR 0111 and the configuration of ADRs 0101 and 0115, and
/// changes when they change.
/// </summary>
public static class VarveResourceBuilderExtensions
{
    /// <summary>The image, on GitHub Container Registry alone (ADR 0111).</summary>
    public const string Image = "ghcr.io/hafeok/varve";

    /// <summary>
    /// Adds a Varve server: <see cref="Image"/> at <paramref name="tag"/>, or
    /// at this build's own version, with the datasets volume at
    /// <c>/var/lib/varve</c>, the <c>http</c> endpoint on 8080, OTLP wired to
    /// the AppHost's dashboard, and <c>GET /health/ready</c> as the health
    /// check (ADR 0113). Without <see cref="WithOidc"/> the server runs
    /// anonymous in run mode and refuses to publish (ADR 0037 point 4).
    /// </summary>
    public static IResourceBuilder<VarveResource> AddVarve(this IDistributedApplicationBuilder builder, string name, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        VarveResource resource = new(name);

        IResourceBuilder<VarveResource> varve = builder.AddResource(resource)
            .WithImage(Image, tag ?? DefaultTag())
            .WithVolume(name + "-data", "/var/lib/varve")
            .WithHttpEndpoint(targetPort: 8080, name: VarveResource.HttpEndpointName)
            .WithHttpHealthCheck("/health/ready")
            .WithOtlpExporter()
            .WithArgs("serve")
            .WithEnvironment(context =>
            {
                // Decided when the environment is gathered, after the chain:
                // anonymous unless WithOidc said otherwise, and every dataset
                // and grant WithDataset declared.
                if (resource.Oidc is null)
                {
                    context.EnvironmentVariables["VARVE__AUTH__MODE"] = "Anonymous";
                }

                foreach (VarveDataset dataset in resource.Datasets)
                {
                    context.EnvironmentVariables["VARVE__DATASETS__" + dataset.Name + "__STORAGE"] = "File";
                    Grants(context.EnvironmentVariables, dataset.Name, "READ", dataset.Read);
                    Grants(context.EnvironmentVariables, dataset.Name, "WRITE", dataset.Write);
                    Grants(context.EnvironmentVariables, dataset.Name, "ADMIN", dataset.Admin);
                }
            });

        // Publishing an anonymous server is refused: the manifest step fails
        // naming WithOidc, which is ADR 0037 point 4 made executable.
        builder.Eventing.Subscribe<BeforePublishEvent>((_, _) => RefuseAnonymousPublish(builder, resource));
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) => RefuseAnonymousPublish(builder, resource));
        return varve;
    }

    /// <summary>
    /// Bearer-token authentication (ADR 0037): <paramref name="authority"/>
    /// is the issuer, as an expression so that it may be another resource's
    /// endpoint; <paramref name="audience"/> what a token must name;
    /// <paramref name="roleClaimType"/> and <paramref name="subjectClaim"/>
    /// the claims the server reads when they are not the defaults;
    /// <paramref name="requireHttpsMetadata"/> false only for an issuer over
    /// plain HTTP, such as a test issuer in the same application. A fixed
    /// issuer is <c>ReferenceExpression.Create($"https://login.example/")</c>.
    /// </summary>
    public static IResourceBuilder<VarveResource> WithOidc(
        this IResourceBuilder<VarveResource> builder,
        ReferenceExpression authority,
        string audience,
        string? roleClaimType = null,
        string? subjectClaim = null,
        bool requireHttpsMetadata = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        builder.Resource.Oidc = new VarveOidc(authority, audience, roleClaimType, subjectClaim, requireHttpsMetadata);

        builder
            .WithEnvironment("VARVE__AUTH__MODE", "Oidc")
            .WithEnvironment("VARVE__AUTH__AUTHORITY", authority)
            .WithEnvironment("VARVE__AUTH__AUDIENCES__0", audience)
            .WithEnvironment("VARVE__AUTH__REQUIREHTTPSMETADATA", requireHttpsMetadata ? "true" : "false");

        if (roleClaimType is not null)
        {
            builder.WithEnvironment("VARVE__AUTH__ROLECLAIMTYPE", roleClaimType);
        }

        if (subjectClaim is not null)
        {
            builder.WithEnvironment("VARVE__AUTH__SUBJECTCLAIM", subjectClaim);
        }

        return builder;
    }

    /// <summary>
    /// A dataset the server opens at start, creating it under its root when
    /// it is not there (ADR 0106), kept in file storage on the volume; and
    /// the roles that may read, write and administer it (ADR 0107). A
    /// dataset that exists is opened as it is, so declaring it again changes
    /// nothing.
    /// </summary>
    public static IResourceBuilder<VarveResource> WithDataset(
        this IResourceBuilder<VarveResource> builder,
        string name,
        IReadOnlyList<string>? read = null,
        IReadOnlyList<string>? write = null,
        IReadOnlyList<string>? admin = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        builder.Resource.Datasets.Add(new VarveDataset(name, read ?? [], write ?? [], admin ?? []));
        return builder;
    }

    // Grants as Varve:Auth:Datasets:{dataset}:{Read|Write|Admin}:{i} (ADR 0107).
    private static void Grants(Dictionary<string, object> environment, string dataset, string permission, IReadOnlyList<string> roles)
    {
        for (int i = 0; i < roles.Count; i++)
        {
            environment["VARVE__AUTH__DATASETS__" + dataset + "__" + permission + "__" + i.ToString(CultureInfo.InvariantCulture)] = roles[i];
        }
    }

    private static Task RefuseAnonymousPublish(IDistributedApplicationBuilder builder, VarveResource resource)
    {
        if (builder.ExecutionContext.IsPublishMode && resource.Oidc is null)
        {
            throw new DistributedApplicationException(
                "The Varve resource '" + resource.Name + "' has no authentication. A published server is never anonymous (ADR 0037): call WithOidc(authority, audience, …) on it.");
        }

        return Task.CompletedTask;
    }

    // This build's version as the image's tag: v0.1.0-preview.3 for the
    // release that ships this assembly, since the tag is the descriptor's
    // version (ADRs 0102, 0111). The informational version carries the
    // commit after '+', which a tag does not.
    private static string DefaultTag()
    {
        string? version = typeof(VarveResource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(version))
        {
            return "latest";
        }

        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return "v" + (plus < 0 ? version : version[..plus]);
    }
}
