// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.S3;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Where a fleet's server processes keep their datasets: a fresh container
/// or bucket per test, on a store CI starts beside the tests (ADR 0099). The
/// leg's variable unset, the test skips, or fails where
/// <c>VARVE_STORAGE_LEGS_REQUIRED</c> names the leg.
/// </summary>
internal abstract class FleetStore
{
    public static FleetStore Azurite { get; } = new AzuriteFleetStore();

    public static FleetStore RustFs { get; } = new RustFsFleetStore();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public abstract Task<FleetLocation> NewAsync();

    protected static string Require(string leg, string variable)
    {
        string value = Environment.GetEnvironmentVariable(variable) ?? string.Empty;

        if (value.Length == 0)
        {
            if ((Environment.GetEnvironmentVariable("VARVE_STORAGE_LEGS_REQUIRED") ?? string.Empty).Split(',').Contains(leg))
            {
                Assert.Fail("The " + leg + " leg is required here (VARVE_STORAGE_LEGS_REQUIRED) and " + variable + " is not set.");
            }

            Assert.Skip("The fleet's " + leg + " leg needs " + variable + " (ADR 0163).");
        }

        return value;
    }

    private sealed class AzuriteFleetStore : FleetStore
    {
        public override async Task<FleetLocation> NewAsync()
        {
            string account = Require("azurite", "VARVE_AZURITE");
            Uri container = new(account.TrimEnd('/') + "/f" + Guid.NewGuid().ToString("N")[..20]);
            using HttpClient http = new();
            using DevelopmentKeySigner signer = new(TimeProvider.System);
            using HttpRequestMessage create = new(HttpMethod.Put, new Uri(container + "?restype=container"));
            create.Headers.Add("x-ms-version", "2025-11-05");
            await signer.SignAsync(create, Ct);
            using HttpResponseMessage response = await http.SendAsync(create, Ct);
            Assert.True(response.IsSuccessStatusCode, "The container was not created: " + response.StatusCode);
            return new AzureLocation(container);
        }
    }

    /// <summary>
    /// RustFS, reached by the server processes by web identity alone (ADR 0137,
    /// #165): the bucket is made with the loopback test key in
    /// <c>VARVE_RUSTFS</c>, and a token minted from the issuer
    /// <c>VARVE_RUSTFS_WEB_IDENTITY</c> names is what each process exchanges at
    /// RustFS's STS (see <c>.github/storage/compose.yaml</c>).
    /// </summary>
    private sealed class RustFsFleetStore : FleetStore
    {
        private const string RoleArn = "arn:aws:iam::000000000000:role/varve";

        private readonly Lock _minting = new();
        private Task<string>? _tokenFile;

        public override async Task<FleetLocation> NewAsync()
        {
            string[] key = Require("rustfs", "VARVE_RUSTFS").Split(',');
            string[] identity = Require("rustfs-web-identity", "VARVE_RUSTFS_WEB_IDENTITY").Split(',');
            Assert.True(key.Length == 3 && identity.Length == 3, "VARVE_RUSTFS is endpoint,accessKeyId,secret and VARVE_RUSTFS_WEB_IDENTITY stsEndpoint,tokenEndpoint,issuerHost.");
            Uri endpoint = new(key[0]);
            string bucket = "f" + Guid.NewGuid().ToString("N")[..20];

            using (HttpClient http = new(new SocketsHttpHandler { UseProxy = false }))
            using (HttpRequestMessage create = new(HttpMethod.Put, new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/" + bucket)))
            {
                SigV4.Sign(create, [], new SigningCredentials(key[1], key[2], null, DateTimeOffset.MaxValue), "us-east-1", DateTimeOffset.UtcNow);
                using HttpResponseMessage response = await http.SendAsync(create, Ct);
                Assert.True(response.IsSuccessStatusCode, "RustFS refused CreateBucket with " + (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync(Ct));
            }

            return new S3Location(endpoint, bucket, new Uri(identity[0]), await TokenFileAsync(new Uri(identity[1]), identity[2]), RoleArn);
        }

        // One token per run, minted by the first test that needs it.
        private Task<string> TokenFileAsync(Uri tokenEndpoint, string issuerHost)
        {
            lock (_minting)
            {
                return _tokenFile ??= MintAsync(tokenEndpoint, issuerHost);
            }
        }

        private static async Task<string> MintAsync(Uri tokenEndpoint, string issuerHost)
        {
            using HttpClient http = new(new SocketsHttpHandler { UseProxy = false });
            using HttpRequestMessage request = new(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "varve-storage",
                    ["client_secret"] = "unchecked",
                    ["scope"] = "openid",
                }),
            };

            // The token's issuer is the host it was asked of, so it is asked of the one RustFS discovered.
            request.Headers.Host = issuerHost;
            using HttpResponseMessage response = await http.SendAsync(request, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            Assert.True(response.IsSuccessStatusCode, "The issuer refused the token request with " + (int)response.StatusCode + ": " + body);
            using JsonDocument token = JsonDocument.Parse(body);
            string file = Path.Combine(Directory.CreateTempSubdirectory("varve-web-identity-").FullName, "token");
            await File.WriteAllTextAsync(file, token.RootElement.GetProperty("access_token").GetString(), CancellationToken.None);

            // varve lease, run in this process, reads the workload's identity from the environment too.
            Environment.SetEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE", file);
            Environment.SetEnvironmentVariable("AWS_ROLE_ARN", RoleArn);
            return file;
        }
    }
}

/// <summary>One test's container or bucket: what a server process and <c>varve lease</c> are told of it.</summary>
internal abstract class FleetLocation
{
    /// <summary>The store's own address, which a counting proxy forwards to.</summary>
    public abstract Uri Upstream { get; }

    /// <summary>The server's object-storage settings.</summary>
    public abstract IEnumerable<string> ServerArguments { get; }

    /// <summary>What the server process's environment adds.</summary>
    public abstract IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>The same location, reached through a proxy at <paramref name="proxy"/>.</summary>
    public abstract FleetLocation Through(Uri proxy);

    /// <summary><c>varve lease</c> of a dataset here.</summary>
    public abstract string[] Lease(string dataset);
}

internal sealed class AzureLocation(Uri container) : FleetLocation
{
    public override Uri Upstream => new(container.GetLeftPart(UriPartial.Authority) + "/");

    public override IEnumerable<string> ServerArguments =>
    [
        "--Varve:ObjectStorage:Provider=Azure",
        "--Varve:ObjectStorage:ContainerUrl=" + container,
    ];

    public override IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string>();

    public override FleetLocation Through(Uri proxy) => new AzureLocation(new Uri(proxy, container.AbsolutePath.TrimStart('/')));

    public override string[] Lease(string dataset) => ["lease", container + "/" + dataset];
}

internal sealed class S3Location(Uri endpoint, string bucket, Uri sts, string tokenFile, string roleArn) : FleetLocation
{
    public override Uri Upstream => new(endpoint.GetLeftPart(UriPartial.Authority) + "/");

    public override IEnumerable<string> ServerArguments =>
    [
        "--Varve:ObjectStorage:Provider=S3Compatible",
        "--Varve:ObjectStorage:Endpoint=" + endpoint,
        "--Varve:ObjectStorage:Bucket=" + bucket,
        "--Varve:ObjectStorage:Region=us-east-1",
        "--Varve:ObjectStorage:StsEndpoint=" + sts,
    ];

    public override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["AWS_WEB_IDENTITY_TOKEN_FILE"] = tokenFile,
        ["AWS_ROLE_ARN"] = roleArn,
    };

    public override FleetLocation Through(Uri proxy) => new S3Location(new Uri(proxy.GetLeftPart(UriPartial.Authority)), bucket, sts, tokenFile, roleArn);

    public override string[] Lease(string dataset) =>
        ["lease", "s3://" + bucket + "/" + dataset, "--endpoint", endpoint.ToString(), "--region", "us-east-1", "--sts-endpoint", sts.ToString()];
}
