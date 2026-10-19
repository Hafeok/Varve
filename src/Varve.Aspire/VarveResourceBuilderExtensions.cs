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
/// image of ADR 0111, the configuration of ADRs 0101 and 0115, and the
/// management declarations of ADR 0166, and changes when they change.
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
                // anonymous unless WithOidc said otherwise, and the bootstrap
                // operators WithOperators named. Datasets and grants are not
                // configuration: WithDataset declares them (ADR 0163).
                if (resource.Oidc is null)
                {
                    context.EnvironmentVariables["VARVE__AUTH__MODE"] = "Anonymous";
                }

                for (int i = 0; i < resource.Operators.Count; i++)
                {
                    context.EnvironmentVariables["VARVE__MANAGE__BOOTSTRAPOPERATORS__" + i.ToString(CultureInfo.InvariantCulture)] = resource.Operators[i];
                }
            });

        // Publishing an anonymous server is refused: the manifest step fails
        // naming WithOidc, which is ADR 0037 point 4 made executable.
        builder.Eventing.Subscribe<BeforePublishEvent>((_, _) => RefuseAnonymousPublish(builder, resource));
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) => RefuseAnonymousPublish(builder, resource));
        builder.Eventing.Subscribe<BeforeStartEvent>((_, _) => WriteDeclarationsAsync(resource));
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
    /// <paramref name="managementAudience"/> is the management plane's, which
    /// the data plane refuses (ADR 0163 point 1).
    /// </summary>
    public static IResourceBuilder<VarveResource> WithOidc(
        this IResourceBuilder<VarveResource> builder,
        ReferenceExpression authority,
        string audience,
        string? roleClaimType = null,
        string? subjectClaim = null,
        bool requireHttpsMetadata = true,
        string managementAudience = "api://varve-manage")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(managementAudience);
        builder.Resource.Oidc = new VarveOidc(authority, audience, roleClaimType, subjectClaim, requireHttpsMetadata, managementAudience);

        builder
            .WithEnvironment("VARVE__AUTH__MODE", "Oidc")
            .WithEnvironment("VARVE__AUTH__AUTHORITY", authority)
            .WithEnvironment("VARVE__AUTH__AUDIENCES__0", audience)
            .WithEnvironment("VARVE__AUTH__MANAGEMENT__AUDIENCE", managementAudience)
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
    /// The server's datasets in an Azure Blob Storage container (ADRs 0134,
    /// 0139, 0142) instead of files on the volume: <paramref name="containerUrl"/>
    /// is the container's address, as an expression so that it may come from
    /// another resource, and each <see cref="WithDataset"/> dataset lives
    /// under its own prefix in it. The server reaches the container as its
    /// workload identity — on Azure Container Apps, its managed identity,
    /// which needs Storage Blob Data Contributor on the container. No key,
    /// SAS token or connection string is passed, or accepted (ADR 0137). The
    /// volume holds the page cache, at most <paramref name="cacheBytes"/>.
    /// </summary>
    /// <remarks>
    /// The server reaches a development emulator only on loopback with its
    /// well-known key (ADR 0137), and a container reaches another container
    /// by name, never on loopback: so in run mode, an emulator such as
    /// Azurite in a neighbouring container is refused by design. Run mode
    /// keeps file datasets; this is for a deployed container.
    /// </remarks>
    public static IResourceBuilder<VarveResource> WithAzureBlobStorage(this IResourceBuilder<VarveResource> builder, ReferenceExpression containerUrl, long cacheBytes = 8L << 30)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(containerUrl);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheBytes, 64L << 20);
        builder.Resource.BlobContainer = containerUrl;

        // The address another instance forwards a write to, recorded when
        // this one claims a dataset (ADR 0168).
        return builder
            .WithEnvironment("VARVE__FLEET__INTERNALURL", builder.Resource.HttpEndpoint)
            .WithEnvironment("VARVE__OBJECTSTORAGE__PROVIDER", "Azure")
            .WithEnvironment("VARVE__OBJECTSTORAGE__CONTAINERURL", containerUrl)
            .WithEnvironment("VARVE__OBJECTSTORAGE__CACHEPATH", "/var/lib/varve/cache")
            .WithEnvironment("VARVE__OBJECTSTORAGE__CACHEBYTES", cacheBytes.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A dataset declared in the server's management plane (ADR 0163), in
    /// file storage on the volume or in the blob container; and the roles
    /// that may read, write and administer it (ADRs 0107, 0166). The
    /// declarations of every dataset are one TriG file, which a container
    /// from the server's own image, <c>{server}-declare</c>, applies with
    /// <c>varve apply</c> once the server is healthy: applying it again
    /// changes nothing. Under <see cref="WithOidc"/> it authenticates as
    /// <see cref="WithManagementClient"/> says.
    /// </summary>
    /// <remarks>
    /// With <paramref name="setup"/>, a setup file (ADR 0130), the AppHost adds
    /// a container from the server's own image, <c>{server}-setup-{dataset}</c>,
    /// that runs <c>varve setup apply</c> once the server is healthy, with the file's
    /// directory mounted read-only (ADR 0131). Under <see cref="WithOidc"/> it
    /// authenticates as <see cref="WithSetupClient"/> says. A resource that
    /// needs the setup applied waits for it with <see cref="WaitForDatasetSetup"/>.
    /// A setup applies the dataset's data, after its declaration.
    /// </remarks>
    public static IResourceBuilder<VarveResource> WithDataset(
        this IResourceBuilder<VarveResource> builder,
        string name,
        IReadOnlyList<string>? read = null,
        IReadOnlyList<string>? write = null,
        IReadOnlyList<string>? admin = null,
        string? setup = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        builder.Resource.Datasets.Add(new VarveDataset(name, read ?? [], write ?? [], admin ?? []));
        builder.Resource.Declare ??= AddDeclare(builder);

        if (setup is not null)
        {
            builder.Resource.Setups[name] = AddSetup(builder, name, setup).WaitForCompletion(builder.Resource.Declare);
        }

        return builder;
    }

    /// <summary>
    /// The bootstrap operators (ADR 0163 point 7): agent IRIs or role values
    /// that are always operators, and that the first system writer records.
    /// A server under <see cref="WithOidc"/> needs at least one.
    /// </summary>
    public static IResourceBuilder<VarveResource> WithOperators(this IResourceBuilder<VarveResource> builder, params string[] principals)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(principals);

        foreach (string principal in principals)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(principal);
            builder.Resource.Operators.Add(principal);
        }

        return builder;
    }

    /// <summary>
    /// The client credentials the declarations container applies the
    /// datasets' declarations with, under <see cref="WithOidc"/>: an operator
    /// of the same issuer, and the scope that names the management audience.
    /// A deployment authenticates by workload identity federation instead
    /// (ADR 0163 point 5); this is for an issuer in the application.
    /// </summary>
    public static IResourceBuilder<VarveResource> WithManagementClient(
        this IResourceBuilder<VarveResource> builder,
        string clientId,
        IResourceBuilder<ParameterResource> clientSecret,
        string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);
        builder.Resource.ManagementClient = new VarveSetupClient(clientId, clientSecret.Resource, scope);
        return builder;
    }

    /// <summary>
    /// The client credentials the setup containers of <see cref="WithDataset"/>
    /// apply their files with, under <see cref="WithOidc"/>: a client of the
    /// same issuer, granted write on the datasets it sets up (admin too, for a
    /// setup that changes settings), and the scope to ask for.
    /// </summary>
    public static IResourceBuilder<VarveResource> WithSetupClient(
        this IResourceBuilder<VarveResource> builder,
        string clientId,
        IResourceBuilder<ParameterResource> clientSecret,
        string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(clientSecret);
        builder.Resource.SetupClient = new VarveSetupClient(clientId, clientSecret.Resource, scope);
        return builder;
    }

    /// <summary>Waits, before <paramref name="builder"/>'s resource starts, for <paramref name="dataset"/>'s setup on <paramref name="varve"/> to have been applied.</summary>
    /// <exception cref="InvalidOperationException">The dataset was declared with no setup.</exception>
    public static IResourceBuilder<T> WaitForDatasetSetup<T>(this IResourceBuilder<T> builder, IResourceBuilder<VarveResource> varve, string dataset)
        where T : IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(varve);
        return varve.Resource.Setups.TryGetValue(dataset, out IResourceBuilder<ContainerResource>? setup)
            ? builder.WaitForCompletion(setup)
            : throw new InvalidOperationException("The dataset " + dataset + " on " + varve.Resource.Name + " was declared with no setup.");
    }

    // The declarations container (ADR 0163): the server's image, `varve
    // apply` of the file WriteDeclarationsAsync wrote, after the server is
    // healthy.
    private static IResourceBuilder<ContainerResource> AddDeclare(IResourceBuilder<VarveResource> varve)
    {
        VarveResource server = varve.Resource;
        server.DeclarationsDirectory = System.IO.Directory.CreateTempSubdirectory("varve-aspire-" + server.Name + "-").FullName;

        // A temporary directory is the owner's alone on Unix (0700), and the
        // image runs as a non-root user, so it could not read the file. The
        // declarations hold no secret: the credentials go as arguments.
        if (!OperatingSystem.IsWindows())
        {
            System.IO.File.SetUnixFileMode(server.DeclarationsDirectory, ReadableDirectory);
        }

        IResourceBuilder<ContainerResource> declare = varve.ApplicationBuilder.AddContainer(server.Name + "-declare", Image, DefaultTag())
            .WithBindMount(server.DeclarationsDirectory, "/declarations", isReadOnly: true)
            .WithArgs(context =>
            {
                context.Args.Add("apply");
                context.Args.Add("/declarations/" + DeclarationsFile);
                context.Args.Add("--server");
                context.Args.Add(server.HttpEndpoint);

                if (server.Oidc is { } oidc)
                {
                    VarveSetupClient client = server.ManagementClient
                        ?? throw new DistributedApplicationException("The Varve resource '" + server.Name + "' authenticates by OIDC and declares datasets: give the declarations their identity with WithManagementClient(clientId, clientSecret).");
                    context.Args.Add("--authority");
                    context.Args.Add(oidc.Authority);
                    context.Args.Add("--client-id");
                    context.Args.Add(client.ClientId);
                    context.Args.Add("--client-secret");
                    context.Args.Add(client.Secret);
                    context.Args.Add("--scope");
                    context.Args.Add(client.Scope ?? oidc.ManagementAudience);
                }
            })
            .WaitFor(varve);
        FollowServerImage(varve, declare);
        return declare;
    }

    /// <summary>The declarations file's name in its directory.</summary>
    internal const string DeclarationsFile = "datasets.trig";

    /// <summary>The TriG every <see cref="WithDataset"/> declared (ADR 0166): each dataset's graph, its storage, and its roles' grants.</summary>
    internal static string Declarations(VarveResource resource)
    {
        System.Text.StringBuilder trig = new("@prefix varve: <https://w3id.org/varve/ns#> .\n");
        string storage = resource.BlobContainer is null ? "File" : "ObjectStorage";

        foreach (VarveDataset dataset in resource.Datasets)
        {
            string node = "<urn:varve:aspire:" + dataset.Name;
            trig.Append("\n<https://w3id.org/varve/ns#manage/dataset/").Append(dataset.Name).Append("> {\n");
            trig.Append("  ").Append(node).Append("> a varve:Dataset ; varve:datasetName \"").Append(dataset.Name).Append("\" ; varve:storage varve:").Append(storage).Append(" .\n");
            int n = 0;

            foreach ((string permission, IReadOnlyList<string> roles) in (ReadOnlySpan<(string, IReadOnlyList<string>)>)[("read", dataset.Read), ("write", dataset.Write), ("admin", dataset.Admin)])
            {
                foreach (string role in roles)
                {
                    string at = (n++).ToString(CultureInfo.InvariantCulture);
                    trig.Append("  ").Append(node).Append(":role:").Append(at).Append("> a varve:Role ; varve:claimValue \"").Append(role.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\" .\n");
                    trig.Append("  ").Append(node).Append(":grant:").Append(at).Append("> a varve:Grant ; varve:grantee ").Append(node).Append(":role:").Append(at).Append("> ; varve:permission varve:").Append(permission);
                    trig.Append(permission == "admin" ? " .\n" : " ; varve:allGraphs true .\n");
                }
            }

            trig.Append("}\n");
        }

        return trig.ToString();
    }

    private static async Task WriteDeclarationsAsync(VarveResource resource)
    {
        if (resource.DeclarationsDirectory is not { } directory)
        {
            return;
        }

        string path = System.IO.Path.Combine(directory, DeclarationsFile);
        await System.IO.File.WriteAllTextAsync(path, Declarations(resource)).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            System.IO.File.SetUnixFileMode(path, ReadableFile);
        }
    }

    private const System.IO.UnixFileMode ReadableFile =
        System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.GroupRead | System.IO.UnixFileMode.OtherRead;

    private const System.IO.UnixFileMode ReadableDirectory =
        ReadableFile | System.IO.UnixFileMode.UserExecute | System.IO.UnixFileMode.GroupExecute | System.IO.UnixFileMode.OtherExecute;

    // A container of the server's image follows the server's image as it
    // stands when the application starts, so a later WithImageTag moves it too.
    private static void FollowServerImage(IResourceBuilder<VarveResource> varve, IResourceBuilder<ContainerResource> container) =>
        varve.ApplicationBuilder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
        {
            if (varve.Resource.TryGetLastAnnotation(out ContainerImageAnnotation? image) && container.Resource.TryGetLastAnnotation(out ContainerImageAnnotation? own))
            {
                own.Registry = image.Registry;
                own.Image = image.Image;
                own.Tag = image.Tag;
            }

            return Task.CompletedTask;
        });

    // The setup container (ADR 0131): the server's image, `varve setup apply`, the
    // file's directory at /setup, after the server is healthy. Its image is
    // the server's as it stands when the application starts, so a later
    // WithImageTag on the server moves it too.
    private static IResourceBuilder<ContainerResource> AddSetup(IResourceBuilder<VarveResource> varve, string dataset, string file)
    {
        string path = System.IO.Path.GetFullPath(file, varve.ApplicationBuilder.AppHostDirectory);
        string directory = System.IO.Path.GetDirectoryName(path)!;
        VarveResource server = varve.Resource;

        IResourceBuilder<ContainerResource> setup = varve.ApplicationBuilder.AddContainer(server.Name + "-setup-" + dataset, Image, DefaultTag())
            .WithBindMount(directory, "/setup", isReadOnly: true)
            .WithArgs(context =>
            {
                context.Args.Add("setup");
                context.Args.Add("apply");
                context.Args.Add("/setup/" + System.IO.Path.GetFileName(path));
                context.Args.Add(server.HttpEndpoint);

                if (server.Oidc is { } oidc)
                {
                    VarveSetupClient client = server.SetupClient
                        ?? throw new DistributedApplicationException("The Varve resource '" + server.Name + "' authenticates by OIDC and sets up " + dataset + ": give the setup its identity with WithSetupClient(clientId, clientSecret).");
                    context.Args.Add("--authority");
                    context.Args.Add(oidc.Authority);
                    context.Args.Add("--client-id");
                    context.Args.Add(client.ClientId);
                    context.Args.Add("--client-secret");
                    context.Args.Add(client.Secret);
                    context.Args.Add("--no-store");

                    if (client.Scope is not null)
                    {
                        context.Args.Add("--scope");
                        context.Args.Add(client.Scope);
                    }
                }
            })
            .WaitFor(varve);
        FollowServerImage(varve, setup);
        return setup;
    }

    private static Task RefuseAnonymousPublish(IDistributedApplicationBuilder builder, VarveResource resource)
    {
        if (builder.ExecutionContext.IsPublishMode && resource.Oidc is null)
        {
            throw new DistributedApplicationException(
                "The Varve resource '" + resource.Name + "' has no authentication. A published server is never anonymous (ADR 0037): call WithOidc(authority, audience, …) on it.");
        }

        if (resource.Oidc is not null && resource.Operators.Count == 0)
        {
            throw new DistributedApplicationException(
                "The Varve resource '" + resource.Name + "' authenticates by OIDC and names no operator: with none, no one can manage it (ADR 0163 point 7). Call WithOperators(…) on it.");
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
