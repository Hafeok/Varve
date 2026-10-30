// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Seeds a fresh Zitadel for auth test layer (c) (ADR 0100).
//
//   docker compose -f .github/zitadel/compose.yaml up -d --wait
//   dotnet run eng/zitadel-seed.cs -- [--address http://localhost:8081]
//       [--bootstrap .github/zitadel/bootstrap] [--output artifacts/zitadel-seed.json]
//       [--webui http://127.0.0.1:5081/ui/]
//
// Through the management API, with the administrator's token that Zitadel's
// first-instance setup wrote to the bootstrap directory:
// - one project, `varve`, asserting its roles;
// - the project roles `read`, `write` and `admin`;
// - one API application, the server;
// - one service user with a client secret and JWT access tokens, granted
//   `write`: the client-credentials flow;
// - one human user with a fresh random password, granted `read`, and one
//   native application allowed the device-code grant: the device-code flow;
// - for the Web UI (ADR 0201, amended 2026-10-28): a second project,
//   `varve-manage`, the management plane's audience, with the role
//   `operator` granted to the human user; a public user-agent application
//   in a project of its own, auth method none, allowed the code, refresh and
//   token-exchange grants, redirecting to `--webui`; and the instance's
//   `oidcTokenExchange` feature.
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
string webUi = "http://127.0.0.1:5081/ui/";

for (int i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--address": address = args[i + 1].TrimEnd('/'); break;
        case "--bootstrap": bootstrap = args[i + 1]; break;
        case "--output": output = args[i + 1]; break;
        case "--webui": webUi = args[i + 1].TrimEnd('/') + "/"; break;
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

    // The Web UI: the management plane's project, and the Web UI's own
    // public client, which exchanges its sign-in token for each plane's.
    JsonNode manage = await CallAsync(HttpMethod.Post, "management/v1/projects", new JsonObject { ["name"] = "varve-manage", ["projectRoleAssertion"] = true });
    string manageId = (string)manage["id"]!;
    await CallAsync(HttpMethod.Post, $"management/v1/projects/{manageId}/roles/_bulk", new JsonObject
    {
        ["roles"] = new JsonArray(new JsonObject { ["key"] = "operator", ["displayName"] = "operator" }),
    });
    await GrantAsync((string)human["userId"]!, manageId, "operator");

    JsonNode webUiProject = await CallAsync(HttpMethod.Post, "management/v1/projects", new JsonObject { ["name"] = "varve-webui" });
    JsonNode webUiApp = await CallAsync(HttpMethod.Post, $"management/v1/projects/{(string)webUiProject["id"]!}/apps/oidc", new JsonObject
    {
        ["name"] = "varve-webui",
        ["redirectUris"] = new JsonArray(webUi + "signin"),
        ["postLogoutRedirectUris"] = new JsonArray(webUi + "signedout"),
        ["responseTypes"] = new JsonArray("OIDC_RESPONSE_TYPE_CODE"),
        ["grantTypes"] = new JsonArray("OIDC_GRANT_TYPE_AUTHORIZATION_CODE", "OIDC_GRANT_TYPE_REFRESH_TOKEN", "OIDC_GRANT_TYPE_TOKEN_EXCHANGE"),
        ["appType"] = "OIDC_APP_TYPE_USER_AGENT",
        ["authMethodType"] = "OIDC_AUTH_METHOD_TYPE_NONE",
        ["accessTokenType"] = "OIDC_TOKEN_TYPE_JWT",
        ["accessTokenRoleAssertion"] = true,
        ["devMode"] = true,
    });

    // Already on is "no changes", a 400; any other refusal fails the seed.
    using (HttpRequestMessage feature = new(HttpMethod.Put, "v2/features/instance") { Content = new StringContent(new JsonObject { ["oidcTokenExchange"] = true }.ToJsonString(), System.Text.Encoding.UTF8, "application/json") })
    using (HttpResponseMessage answered = await http.SendAsync(feature))
    {
        string text = await answered.Content.ReadAsStringAsync();

        if (!answered.IsSuccessStatusCode && !text.Contains("No changes", StringComparison.Ordinal))
        {
            throw new SeedException($"PUT v2/features/instance is {(int)answered.StatusCode}: {text}");
        }
    }

    JsonObject seed = new()
    {
        ["issuer"] = address,
        ["audience"] = projectId,
        ["roleClaim"] = $"urn:zitadel:iam:org:project:{projectId}:roles",
        ["scope"] = $"openid urn:zitadel:iam:org:project:id:{projectId}:aud urn:zitadel:iam:org:projects:roles",
        ["writer"] = new JsonObject { ["clientId"] = (string)secret["clientId"]!, ["clientSecret"] = (string)secret["clientSecret"]! },
        ["deviceClientId"] = (string)device["clientId"]!,
        ["reader"] = new JsonObject { ["loginName"] = "varve-reader", ["password"] = password, ["userId"] = (string)human["userId"]! },
        ["loginClientToken"] = (await File.ReadAllTextAsync(loginPat)).Trim(),
        ["webUi"] = new JsonObject
        {
            ["address"] = webUi,
            ["clientId"] = (string)webUiApp["clientId"]!,
            ["manageAudience"] = manageId,
            ["dataScope"] = $"urn:zitadel:iam:org:project:id:{projectId}:aud",
            ["manageScope"] = $"urn:zitadel:iam:org:project:id:{manageId}:aud",
        },
    };

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output, seed.ToJsonString());
    Console.WriteLine($"zitadel-seed: project {projectId} with roles read, write, admin; a writer service user; a reader human user; a device-code client; the Web UI's management project {manageId} and exchanging client. Written to {output}.");
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
