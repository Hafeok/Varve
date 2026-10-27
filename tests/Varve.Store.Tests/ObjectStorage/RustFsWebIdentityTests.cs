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
using Varve.Store.S3;
using Varve.Store.S3.Model;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// RustFS's web identity leg (ADR 0137, S3WebIdentityOnly; #165): its
/// variable, <c>VARVE_RUSTFS_WEB_IDENTITY</c>, holds
/// <c>stsEndpoint,tokenEndpoint,issuerHost</c>. A token is minted once per
/// run from the issuer for the client <c>varve-storage</c>, with the request's
/// host set to the one RustFS discovered the issuer at, so that the token's
/// issuer is that one; it is written to a file, and
/// <c>AWS_WEB_IDENTITY_TOKEN_FILE</c> and <c>AWS_ROLE_ARN</c> name it, as a
/// workload's environment would. RustFS reads no role ARN, so it is a fixed,
/// unsecret name.
/// </summary>
internal static class RustFsWebIdentity
{
    private static readonly SemaphoreSlim Minting = new(1, 1);
    private static Uri? _sts;

    /// <summary>RustFS's STS endpoint, once this process's environment names a fresh token for it.</summary>
    public static async Task<Uri> StsAsync()
    {
        string[] parts = ServiceLegs.Require("rustfs-web-identity", "VARVE_RUSTFS_WEB_IDENTITY").Split(',');
        Assert.True(parts.Length == 3, "VARVE_RUSTFS_WEB_IDENTITY is stsEndpoint,tokenEndpoint,issuerHost.");
        await Minting.WaitAsync(T.Ct);

        try
        {
            if (_sts is null)
            {
                using HttpClient http = new(new SocketsHttpHandler { UseProxy = false });
                using HttpRequestMessage request = new(HttpMethod.Post, new Uri(parts[1]))
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = "varve-storage",
                        ["client_secret"] = "unchecked",
                        ["scope"] = "openid",
                    }),
                };
                request.Headers.Host = parts[2];
                using HttpResponseMessage response = await http.SendAsync(request, T.Ct);
                string body = await response.Content.ReadAsStringAsync(T.Ct);
                Assert.True(response.IsSuccessStatusCode, "The issuer refused the token request with " + (int)response.StatusCode + ": " + body);
                using JsonDocument token = JsonDocument.Parse(body);
                string file = Path.Combine(Directory.CreateTempSubdirectory("varve-web-identity-").FullName, "token");
                await File.WriteAllTextAsync(file, token.RootElement.GetProperty("access_token").GetString(), T.Ct);
                Environment.SetEnvironmentVariable("AWS_WEB_IDENTITY_TOKEN_FILE", file);
                Environment.SetEnvironmentVariable("AWS_ROLE_ARN", RoleArn);
                _sts = new Uri(parts[0]);
            }

            return _sts;
        }
        finally
        {
            Minting.Release();
        }
    }

    public const string RoleArn = "arn:aws:iam::000000000000:role/varve";
}

/// <summary>
/// RustFS reached by web identity alone, as a server process reaches it: the
/// bucket is made with the loopback test key, and every operation of the
/// contract, the race properties included, is then signed with the temporary
/// credentials RustFS's STS returns for the workload's token.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class RustFsWebIdentityObjectStoreContractTests
{
    public static TheoryData<string> Cases() => [.. ObjectStoreContractCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task the_server_keeps_the_object_store_contract_by_web_identity(string name)
    {
        Uri sts = await RustFsWebIdentity.StsAsync();
        (Uri endpoint, S3Bucket bucket, _) = await S3Server.RustFs.NewBucketAsync();
        using S3ObjectStore store = S3ObjectStore.Create(new S3ObjectStoreOptions
        {
            Endpoint = endpoint,
            Bucket = bucket,
            StsEndpoint = sts,
            Clock = TimeProvider.System,
        });

        await ObjectStoreContractCases.All().Single(c => c.Name == name).Run(store, "contract/", T.Ct);
    }
}
