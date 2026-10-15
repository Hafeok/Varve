// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Store.Azure;

/// <summary>
/// The three places an Azure token comes from, and nothing else (ADR 0137,
/// AzureTokenSources): the Container Apps managed-identity endpoint, a
/// workload-identity token file exchanged as a client assertion, and on a
/// workstation <c>az account get-access-token</c>.
/// </summary>
internal static class TokenSources
{
    public const string Resource = "https://storage.azure.com/";

    /// <summary>The first source the environment configures, in ADR 0137's order.</summary>
    public static ITokenSource FromEnvironment(Func<string, string?> environment, HttpClient http, TimeProvider clock, Func<CancellationToken, ValueTask<string>>? azureCli = null)
    {
        if (environment("IDENTITY_ENDPOINT") is { Length: > 0 } endpoint && environment("IDENTITY_HEADER") is { Length: > 0 } header)
        {
            return new ManagedIdentity(http, new Uri(endpoint), header, environment("AZURE_CLIENT_ID"));
        }

        if (environment("AZURE_FEDERATED_TOKEN_FILE") is { Length: > 0 } file
            && environment("AZURE_TENANT_ID") is { Length: > 0 } tenant
            && environment("AZURE_CLIENT_ID") is { Length: > 0 } client)
        {
            string authority = environment("AZURE_AUTHORITY_HOST") is { Length: > 0 } host ? host : "https://login.microsoftonline.com/";
            return new WorkloadIdentity(http, clock, file, new Uri(new Uri(authority.EndsWith('/') ? authority : authority + "/"), tenant + "/oauth2/v2.0/token"), client);
        }

        return new AzureCli(azureCli ?? RunAzureCliAsync);
    }

    private static async ValueTask<string> RunAzureCliAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("az")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in new[] { "account", "get-access-token", "--resource", Resource, "--output", "json" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new IOException("az could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            // Its error output names what is wrong, never a token.
            throw new IOException("az account get-access-token failed (" + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "): " + (await error.ConfigureAwait(false)).Trim());
        }

        return await output.ConfigureAwait(false);
    }

    /// <summary>The managed-identity endpoint Container Apps gives an app: <c>IDENTITY_ENDPOINT</c> with <c>X-IDENTITY-HEADER</c>.</summary>
    internal sealed class ManagedIdentity(HttpClient http, Uri endpoint, string header, string? clientId) : ITokenSource
    {
        public async ValueTask<AccessToken> FetchAsync(CancellationToken cancellationToken)
        {
            string query = "resource=" + Uri.EscapeDataString(Resource) + "&api-version=2019-08-01"
                + (string.IsNullOrEmpty(clientId) ? string.Empty : "&client_id=" + Uri.EscapeDataString(clientId));
            using HttpRequestMessage request = new(HttpMethod.Get, new UriBuilder(endpoint) { Query = query }.Uri);
            request.Headers.Add("X-IDENTITY-HEADER", header);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException("The managed-identity endpoint refused a token for Azure Storage with " + (int)response.StatusCode + ". Give the app an identity with access to the container (ADR 0137).");
            }

            using JsonDocument json = JsonDocument.Parse(body);
            string token = json.RootElement.GetProperty("access_token").GetString() ?? throw new IOException("The managed-identity endpoint answered without a token.");
            JsonElement expires = json.RootElement.GetProperty("expires_on");
            long seconds = expires.ValueKind == JsonValueKind.String
                ? long.Parse(expires.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
                : expires.GetInt64();
            return new AccessToken(token, DateTimeOffset.FromUnixTimeSeconds(seconds));
        }
    }

    /// <summary>A federated workload identity: the platform's token file, exchanged at the Entra token endpoint as a client assertion.</summary>
    internal sealed class WorkloadIdentity(HttpClient http, TimeProvider clock, string tokenFile, Uri tokenEndpoint, string clientId) : ITokenSource
    {
        public async ValueTask<AccessToken> FetchAsync(CancellationToken cancellationToken)
        {
            // Read on every exchange: the platform rotates the file.
            string assertion = (await File.ReadAllTextAsync(tokenFile, cancellationToken).ConfigureAwait(false)).Trim();
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = Resource + ".default",
                ["grant_type"] = "client_credentials",
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = assertion,
            });
            DateTimeOffset asked = clock.GetUtcNow();
            using HttpResponseMessage response = await http.PostAsync(tokenEndpoint, form, cancellationToken).ConfigureAwait(false);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException("The token endpoint refused the workload identity's assertion with " + (int)response.StatusCode + ". Check the federated credential's issuer and subject (ADR 0137).");
            }

            using JsonDocument json = JsonDocument.Parse(body);
            string token = json.RootElement.GetProperty("access_token").GetString() ?? throw new IOException("The token endpoint answered without a token.");
            return new AccessToken(token, asked.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt64()));
        }
    }

    /// <summary>A workstation's <c>az login</c>, through <c>az account get-access-token</c>.</summary>
    internal sealed class AzureCli(Func<CancellationToken, ValueTask<string>> run) : ITokenSource
    {
        public async ValueTask<AccessToken> FetchAsync(CancellationToken cancellationToken)
        {
            string output = await run(cancellationToken).ConfigureAwait(false);
            using JsonDocument json = JsonDocument.Parse(output);
            string token = json.RootElement.GetProperty("accessToken").GetString() ?? throw new IOException("az answered without a token.");
            DateTimeOffset expires = json.RootElement.TryGetProperty("expires_on", out JsonElement seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds.ValueKind == JsonValueKind.String ? long.Parse(seconds.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture) : seconds.GetInt64())
                : DateTimeOffset.Parse(json.RootElement.GetProperty("expiresOn").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
            return new AccessToken(token, expires);
        }
    }
}
