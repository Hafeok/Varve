// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Seeds a fresh Zitadel for auth test layer (c) (ADR 0100).
//
//   docker compose -f .github/zitadel/compose.yaml up -d --wait
//   dotnet run eng/zitadel-seed.cs -- [--address http://localhost:8081]
//       [--bootstrap .github/zitadel/bootstrap] [--output artifacts/zitadel-seed.json]
//
// Through the management API, with the administrator's token that Zitadel's
// first-instance setup wrote to the bootstrap directory:
// - one project, `varve`, asserting its roles;
// - the project roles `read`, `write` and `admin`;
// - one API application, the server;
// - one service user with a client secret and JWT access tokens, granted
//   `write`: the client-credentials flow;
// - one human user with a fresh random password, granted `read`, and one
//   native application allowed the device-code grant: the device-code flow.
//
// It writes what the tests need to the output file: the issuer, the audience
// (the project), the role claim, the two clients, the human user, and the
// login client's token, which approves a device code through the OIDC
// service's API as a custom login UI does. The instance and every credential
// in it are throwaway; the file is under artifacts/, which is not committed.
//
// It seeds a fresh instance once. To seed again, `docker compose ... down -v`
// first. Exit 0 seeded, 1 a call failed, 2 it could not start.

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

string address = "http://localhost:8081";
string bootstrap = ".github/zitadel/bootstrap";
string output = "artifacts/zitadel-seed.json";

for (int i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--address": address = args[i + 1].TrimEnd('/'); break;
        case "--bootstrap": bootstrap = args[i + 1]; break;
        case "--output": output = args[i + 1]; break;
        default:
            Console.Error.WriteLine("zitadel-seed: unknown option " + args[i]);
            return 2;
    }
}

string adminPat = Path.Combine(bootstrap, "admin.pat");
string loginPat = Path.Combine(bootstrap, "login-client.pat");

if (!File.Exists(adminPat) || !File.Exists(loginPat))
{
    Console.Error.WriteLine("zitadel-seed: no tokens in " + bootstrap + "; is Zitadel up (docker compose -f .github/zitadel/compose.yaml up -d --wait)?");
    return 2;
}

// Loopback only: a proxy configured for the runner must not see this traffic.
using HttpClient http = new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address + "/") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await File.ReadAllTextAsync(adminPat)).Trim());

// Healthy is not yet serving: `zitadel ready` can pass while the API gateway
// still answers 503 for the gRPC server behind it. Wait for an authenticated
// call to succeed, for up to a minute.
for (int attempt = 0; ; attempt++)
{
    using HttpResponseMessage me = await http.GetAsync("management/v1/orgs/me");

    if (me.IsSuccessStatusCode)
    {
        break;
    }

    if (attempt == 60)
    {
        Console.Error.WriteLine($"zitadel-seed: the API still answers {(int)me.StatusCode} after a minute: {await me.Content.ReadAsStringAsync()}");
        return 1;
    }

    await Task.Delay(TimeSpan.FromSeconds(1));
}

try
{
    JsonNode project = await CallAsync(HttpMethod.Post, "management/v1/projects", new JsonObject { ["name"] = "varve", ["projectRoleAssertion"] = true });
    string projectId = (string)project["id"]!;

    await CallAsync(HttpMethod.Post, $"management/v1/projects/{projectId}/roles/_bulk", new JsonObject
    {
        ["roles"] = new JsonArray(
            new JsonObject { ["key"] = "read", ["displayName"] = "read" },
            new JsonObject { ["key"] = "write", ["displayName"] = "write" },
            new JsonObject { ["key"] = "admin", ["displayName"] = "admin" }),
    });

    await CallAsync(HttpMethod.Post, $"management/v1/projects/{projectId}/apps/api", new JsonObject
    {
        ["name"] = "varve-server",
        ["authMethodType"] = "API_AUTH_METHOD_TYPE_PRIVATE_KEY_JWT",
    });

    JsonNode machine = await CallAsync(HttpMethod.Post, "management/v1/users/machine", new JsonObject
    {
        ["userName"] = "varve-writer",
        ["name"] = "Varve writer",
        ["accessTokenType"] = "ACCESS_TOKEN_TYPE_JWT",
    });
    string machineId = (string)machine["userId"]!;
    JsonNode secret = await CallAsync(HttpMethod.Put, $"management/v1/users/{machineId}/secret", new JsonObject());
    await GrantAsync(machineId, projectId, "write");

    string password = "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');
    JsonNode human = await CallAsync(HttpMethod.Post, "v2/users/human", new JsonObject
    {
        ["username"] = "varve-reader",
        ["profile"] = new JsonObject { ["givenName"] = "Varve", ["familyName"] = "Reader" },
        ["email"] = new JsonObject { ["email"] = "reader@varve.example", ["isVerified"] = true },
        ["password"] = new JsonObject { ["password"] = password, ["changeRequired"] = false },
    });
    await GrantAsync((string)human["userId"]!, projectId, "read");

    JsonNode device = await CallAsync(HttpMethod.Post, $"management/v1/projects/{projectId}/apps/oidc", new JsonObject
    {
        ["name"] = "varve-cli",
        ["responseTypes"] = new JsonArray("OIDC_RESPONSE_TYPE_CODE"),
        ["grantTypes"] = new JsonArray("OIDC_GRANT_TYPE_DEVICE_CODE"),
        ["appType"] = "OIDC_APP_TYPE_NATIVE",
        ["authMethodType"] = "OIDC_AUTH_METHOD_TYPE_NONE",
        ["accessTokenType"] = "OIDC_TOKEN_TYPE_JWT",
        ["accessTokenRoleAssertion"] = true,
    });

    JsonObject seed = new()
    {
        ["issuer"] = address,
        ["audience"] = projectId,
        ["roleClaim"] = $"urn:zitadel:iam:org:project:{projectId}:roles",
        ["scope"] = $"openid urn:zitadel:iam:org:project:id:{projectId}:aud urn:zitadel:iam:org:projects:roles",
        ["writer"] = new JsonObject { ["clientId"] = (string)secret["clientId"]!, ["clientSecret"] = (string)secret["clientSecret"]! },
        ["deviceClientId"] = (string)device["clientId"]!,
        ["reader"] = new JsonObject { ["loginName"] = "varve-reader", ["password"] = password },
        ["loginClientToken"] = (await File.ReadAllTextAsync(loginPat)).Trim(),
    };

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, seed.ToJsonString());
    Console.WriteLine($"zitadel-seed: project {projectId} with roles read, write, admin; a writer service user; a reader human user; a device-code client. Written to {output}.");
    return 0;
}
catch (SeedException error)
{
    Console.Error.WriteLine("zitadel-seed: " + error.Message);
    return 1;
}

async Task GrantAsync(string userId, string projectId, string role) =>
    await CallAsync(HttpMethod.Post, $"management/v1/users/{userId}/grants", new JsonObject { ["projectId"] = projectId, ["roleKeys"] = new JsonArray(role) });

async Task<JsonNode> CallAsync(HttpMethod method, string path, JsonObject body)
{
    using HttpRequestMessage request = new(method, path) { Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };
    using HttpResponseMessage response = await http.SendAsync(request);
    string text = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new SeedException($"{method} {path} is {(int)response.StatusCode}: {text}");
    }

    return JsonNode.Parse(text) ?? new JsonObject();
}

sealed class SeedException(string message) : Exception(message);
