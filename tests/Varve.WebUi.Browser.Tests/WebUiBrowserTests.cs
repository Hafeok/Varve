// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace Varve.WebUi.Browser.Tests;

/// <summary>
/// The Web UI in Chrome against the server (ADRs 0201, 0202, 0204): every
/// screen and dialog audited by axe-core in both themes with no violation and
/// no CSP violation; the skip link first; anonymous mode and sign-in against
/// mock-oauth2-server, one token per plane; and a dual-audience token refused
/// by both planes.
/// </summary>
public sealed class WebUiBrowserTests
{
    private static readonly ImmutableArray<string> Wcag = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    private const string Declarations = """
        @prefix varve: <https://w3id.org/varve/ns#> .
        @prefix n: <https://notes.example/manage/> .
        varve:manage\/dataset\/notes {
          n:notes a varve:Dataset ; varve:datasetName "notes" ; varve:storage varve:Memory .
          n:users a varve:Role ; varve:claimValue "Notes.User" .
          n:users-shared a varve:Grant ; varve:grantee n:users ; varve:permission varve:read ; varve:graph <https://notes.example/shared> .
          n:own a varve:GraphTemplate ; varve:template "https://notes.example/users/{sub}/" ; varve:match varve:Prefix .
          n:users-own a varve:Grant ; varve:grantee n:users ; varve:permission varve:write ; varve:graphTemplate n:own .
          n:shapes a varve:ShapesBinding ; varve:boundGraph <https://notes.example/shapes> ; varve:validationMode varve:Report ; varve:graph <https://notes.example/shared> .
        }
        varve:manage\/dataset\/catalog { n:catalog a varve:Dataset ; varve:datasetName "catalog" ; varve:storage varve:Memory . }
        """;

    // Each screen, and what shows it has rendered.
    private static readonly ImmutableArray<(string Path, string Rendered)> Screens =
    [
        ("datasets", "td.vw-table__name"),
        ("health", "text=Readiness"),
        ("fleet", "text=This instance in the system dataset"),
        ("datasets/notes", "text=Recent commits"),
        ("datasets/notes?asof=1", "text=Reading as of"),
        ("datasets/notes/commits", ".vw-tail"),
        ("datasets/notes/commits/1", "text=Delta in"),
        ("datasets/notes/grants", "text=Who may read"),
        ("datasets/notes/declaration", ".vw-source"),
        ("datasets/notes/agents", "text=Who the grants name"),
        ("datasets/notes/templates", "text=Graphs expanded for each caller"),
        ("datasets/notes/shapes", ".vw-source"),
    ];

    [Fact]
    public async Task Every_screen_and_dialog_passes_axe_in_both_themes_with_no_csp_violation()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        await using WebUiServer server = await AnonymousAsync(bundle);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        List<string> findings = [];

        foreach (string theme in (string[])["light", "dark"])
        {
            IBrowserContext context = await browser.NewContextAsync();
            await context.AddInitScriptAsync("try { localStorage.setItem('varve.theme', '" + theme + "'); } catch (e) {}");
            IPage page = await context.NewPageAsync();
            List<string> violations = Watch(page);

            foreach ((string path, string rendered) in Screens)
            {
                await page.GotoAsync(server.Ui + path);
                await page.WaitForSelectorAsync(rendered, new() { Timeout = 60_000 });
                Assert.Equal(theme, await page.GetAttributeAsync("html", "data-theme"));
                findings.AddRange(await AuditAsync(page, theme + " " + path));
            }

            // The acts' dialogs, open.
            await page.GotoAsync(server.Ui + "datasets/notes");
            await page.ClickAsync("button:has-text('Close dataset')");
            await page.WaitForSelectorAsync("dialog[open]");
            findings.AddRange(await AuditAsync(page, theme + " close dialog"));
            await page.Keyboard.PressAsync("Escape");
            await page.ClickAsync("button:has-text('Take checkpoint')");
            await page.WaitForSelectorAsync("dialog[open]");
            findings.AddRange(await AuditAsync(page, theme + " checkpoint dialog"));

            findings.AddRange(violations.Select(violation => theme + ": " + violation));
            await context.CloseAsync();
        }

        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public async Task The_first_tab_reaches_the_skip_link_and_a_navigation_moves_focus_to_the_new_title()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        await using WebUiServer server = await AnonymousAsync(bundle);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync(server.Ui + "health");
        await page.WaitForSelectorAsync("text=Readiness", new() { Timeout = 60_000 });
        await page.Keyboard.PressAsync("Tab");

        Assert.Equal("Skip to main content", await page.EvaluateAsync<string>("document.activeElement.textContent"));
        Assert.True(await page.EvaluateAsync<double>("document.activeElement.getBoundingClientRect().width") > 1);

        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("main", await page.EvaluateAsync<string>("document.activeElement.id"));
        Assert.EndsWith("/ui/health", page.Url, StringComparison.Ordinal);

        // A navigation inside the Web UI moves focus to the new page's title.
        await page.ClickAsync("nav a:has-text('Fleet')");
        await page.WaitForSelectorAsync("text=This instance in the system dataset", new() { Timeout = 60_000 });
        await page.WaitForFunctionAsync("document.activeElement && document.activeElement.tagName === 'H1'");
        Assert.Equal("Fleet", await page.EvaluateAsync<string>("document.activeElement.textContent"));
    }

    [Fact]
    public async Task The_commits_stamp_advances_with_each_commit_the_live_tail_shows()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        await using WebUiServer server = await AnonymousAsync(bundle);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync(server.Ui + "datasets/notes/commits");
        await page.WaitForSelectorAsync(".vw-tail", new() { Timeout = 60_000 });
        string before = await page.InnerTextAsync(".vw-viewhead .vw-position__value");

        using HttpClient http = new() { BaseAddress = server.Address };
        using HttpResponseMessage written = await http.PostAsync("datasets/notes/sparql", new StringContent("INSERT DATA { <https://notes.example/43> <http://purl.org/dc/terms/title> \"Retro\"@en }", new MediaTypeHeaderValue("application/sparql-update")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        await page.WaitForFunctionAsync("p => document.querySelector('.vw-viewhead .vw-position__value').textContent !== p", before, new() { Timeout = 30_000 });
        long stamp = long.Parse(await page.InnerTextAsync(".vw-viewhead .vw-position__value"), System.Globalization.CultureInfo.InvariantCulture);
        IReadOnlyList<string> shown = await page.Locator(".vw-commit__pos a").AllInnerTextsAsync();
        Assert.Equal(shown.Max(position => long.Parse(position, System.Globalization.CultureInfo.InvariantCulture)), stamp);
        Assert.True(stamp > long.Parse(before, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Anonymous_mode_shows_both_planes_anonymous_and_no_sign_out()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        await using WebUiServer server = await AnonymousAsync(bundle);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();

        await page.GotoAsync(server.Ui.AbsoluteUri);
        await page.WaitForSelectorAsync("td.vw-table__name", new() { Timeout = 60_000 });

        Assert.Equal(["Data plane anonymous", "Management anonymous"], await Planes(page));
        Assert.Equal(0, await page.Locator("text=Sign out").CountAsync());
    }

    [Fact]
    public async Task Sign_in_takes_one_token_per_plane_and_each_is_refused_on_the_other_plane()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        string issuer = Mock();
        await using WebUiServer server = await OidcAsync(bundle, issuer);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();
        List<string> violations = Watch(page);
        List<IRequest> authorizations = [];
        List<(string Form, string Body)> tokens = [];
        page.Request += (_, request) =>
        {
            if (request.Url.StartsWith(issuer + "/authorize", StringComparison.Ordinal))
            {
                authorizations.Add(request);
            }
        };
        page.Response += async (_, response) =>
        {
            if (response.Url == issuer + "/token" && response.Request.Method == "POST")
            {
                tokens.Add((response.Request.PostData ?? "", await response.TextAsync()));
            }
        };

        await page.GotoAsync(server.Ui + "datasets");
        await page.FillAsync("input[name=username]", "alice", new() { Timeout = 60_000 });
        await page.ClickAsync("input[type=submit], button[type=submit]");
        await page.WaitForSelectorAsync("text=Sign out", new() { Timeout = 60_000 });
        await page.WaitForSelectorAsync("td.vw-table__name", new() { Timeout = 60_000 });

        Assert.Equal(["Data plane signed in", "Management signed in"], await Planes(page));
        string scope = Query(authorizations[0].Url)["scope"];
        Assert.Equal("openid offline_access varve-webui-data", scope);
        Assert.Equal("none", Query(authorizations[0].Url)["prompt"]);
        Assert.Contains(tokens, token => token.Form.Contains("grant_type=refresh_token", StringComparison.Ordinal) && token.Form.Contains("scope=varve-webui-manage", StringComparison.Ordinal));

        string data = Access(tokens.First(token => token.Form.Contains("grant_type=authorization_code", StringComparison.Ordinal)).Body);
        string manage = Access(tokens.First(token => token.Form.Contains("scope=varve-webui-manage", StringComparison.Ordinal)).Body);
        using HttpClient http = new() { BaseAddress = server.Address };
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(http, "manage/system", manage));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, "manage/system", data));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, "datasets/notes/sparql?query=ASK%7B%7D", manage));
        Assert.Empty(violations);
    }

    // Zitadel by exchange (ADR 0201, amended 2026-10-28): the sign-in asks for
    // both projects, each plane's token is exchanged from the answer, and the
    // token naming both is sent to the token endpoint alone. Zitadel has no
    // login UI here, so the test completes its auth request through the API a
    // login UI uses, as the provider tests do.
    [Fact]
    public async Task Zitadel_signs_both_planes_in_by_exchange_and_the_token_naming_both_never_reaches_the_server()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        JsonNode seed = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(Zitadel(), TestContext.Current.CancellationToken))!;
        string issuer = (string)seed["issuer"]!;
        JsonNode webUi = seed["webUi"]!;
        const string Roles = " urn:zitadel:iam:org:projects:roles";
        Uri address = new((string)webUi["address"]!);

        await using WebUiServer server = await WebUiServer.StartAsync(
            bundle,
            new Dictionary<string, string>
            {
                ["Varve:Auth:Mode"] = "Oidc",
                ["Varve:Auth:Authority"] = issuer,
                ["Varve:Auth:RequireHttpsMetadata"] = "false",
                ["Varve:Auth:Audiences:0"] = (string)seed["audience"]!,
                ["Varve:Auth:Management:Audience"] = (string)webUi["manageAudience"]!,
                ["Varve:Auth:RoleClaimType"] = (string)seed["roleClaim"]!,
                // Zitadel names its role claim per project, and the server
                // reads one role claim: the operator is named by agent IRI.
                ["Varve:Manage:BootstrapOperators:0"] = issuer + "#" + (string)seed["reader"]!["userId"]!,
            },
            new JsonObject
            {
                ["Mode"] = "Oidc",
                ["Authority"] = issuer,
                ["ClientId"] = (string)webUi["clientId"]!,
                ["DataScope"] = (string)webUi["dataScope"]! + Roles,
                ["ManageScope"] = (string)webUi["manageScope"]! + Roles,
                ["ManageGrant"] = "exchange",
                ["DataAudience"] = (string)seed["audience"]!,
                ["ManageAudience"] = (string)webUi["manageAudience"]!,
            },
            "http://" + address.Authority);
        await server.DeclareAsync(Declarations);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IPage page = await browser.NewPageAsync();
        List<string> violations = Watch(page);
        List<string> sentToServer = [];
        List<(string Form, string Body)> tokens = [];
        page.Request += (_, request) =>
        {
            if (request.Url.StartsWith(server.Address.AbsoluteUri, StringComparison.Ordinal) && request.Headers.TryGetValue("authorization", out string? bearer))
            {
                sentToServer.Add(bearer["Bearer ".Length..]);
            }
        };
        page.Response += async (_, response) =>
        {
            if (response.Url == issuer + "/oauth/v2/token" && response.Request.Method == "POST")
            {
                tokens.Add((response.Request.PostData ?? "", await response.TextAsync()));
            }
        };

        await page.GotoAsync(new Uri(address, "datasets").AbsoluteUri);
        await page.WaitForURLAsync(url => url.Contains("authRequest=", StringComparison.Ordinal) && !url.Contains("prompt", StringComparison.Ordinal), new() { Timeout = 60_000 });
        string request = Query(page.Url).First(pair => pair.Key is "authRequest" or "authRequestID").Value;
        await page.GotoAsync(await ApproveAsync(issuer, seed, request));
        await page.WaitForSelectorAsync("text=Sign out", new() { Timeout = 60_000 });
        await page.WaitForSelectorAsync("td.vw-table__name", new() { Timeout = 60_000 });

        Assert.Equal(["Data plane signed in", "Management signed in"], await Planes(page));
        string combined = Access(tokens.Single(token => token.Form.Contains("grant_type=authorization_code", StringComparison.Ordinal)).Body);
        Assert.Equal(2, tokens.Count(token => token.Form.Contains("token-exchange", StringComparison.Ordinal)));
        Assert.NotEmpty(sentToServer);
        Assert.DoesNotContain(combined, sentToServer);
        Assert.All(sentToServer, token => Assert.Single(Audiences(token)));
        Assert.Empty(violations);
    }

    [Fact]
    public async Task A_token_for_both_planes_is_refused_by_both()
    {
        WebUiServer.Required();
        string issuer = Mock();
        await using WebUiServer server = await OidcAsync(WebUiServer.Required().Bundle, issuer);
        using HttpClient provider = new();
        using HttpResponseMessage minted = await provider.PostAsync(new Uri(issuer + "/token"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "varve-dual",
            ["client_secret"] = "not-checked-by-the-mock",
            ["scope"] = "api://varve",
        }), TestContext.Current.CancellationToken);
        string dual = Access(await minted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using HttpClient http = new() { BaseAddress = server.Address };

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, "manage/system", dual));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(http, "datasets/notes/sparql?query=ASK%7B%7D", dual));
    }

    private static async Task<WebUiServer> AnonymousAsync(string bundle)
    {
        WebUiServer server = await WebUiServer.StartAsync(bundle, new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous" }, new JsonObject { ["Mode"] = "Anonymous" });
        await server.DeclareAsync(Declarations);

        // One commit, so the commits and the delta have something to show.
        using HttpClient http = new() { BaseAddress = server.Address };
        using HttpResponseMessage written = await http.PostAsync("datasets/notes/sparql", new StringContent("INSERT DATA { <https://notes.example/42> <http://purl.org/dc/terms/title> \"Weekly sync\"@en }", new MediaTypeHeaderValue("application/sparql-update")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        // And the shapes graph the binding names, for the Shapes screen.
        using HttpResponseMessage shapes = await http.PostAsync("datasets/notes/sparql", new StringContent("INSERT DATA { GRAPH <https://notes.example/shapes> { <https://notes.example/TitleShape> a <http://www.w3.org/ns/shacl#NodeShape> ; <http://www.w3.org/ns/shacl#targetNode> <https://notes.example/42> } }", new MediaTypeHeaderValue("application/sparql-update")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, shapes.StatusCode);
        return server;
    }

    private static async Task<WebUiServer> OidcAsync(string bundle, string issuer)
    {
        WebUiServer server = await WebUiServer.StartAsync(
            bundle,
            new Dictionary<string, string>
            {
                ["Varve:Auth:Mode"] = "Oidc",
                ["Varve:Auth:Authority"] = issuer,
                ["Varve:Auth:RequireHttpsMetadata"] = "false",
                // mock-oauth2-server gives the ID token the mapping's aud
                // too, so the Web UI's sign-in token names the client, as an
                // ID token must: one audience, which this data plane accepts
                // beside api://varve, the audience the dual token names.
                ["Varve:Auth:Audiences:0"] = "api://varve",
                ["Varve:Auth:Audiences:1"] = "varve-webui",
                ["Varve:Manage:BootstrapOperators:0"] = "varve.operator",
            },
            new JsonObject
            {
                ["Mode"] = "Oidc",
                ["Authority"] = issuer,
                ["ClientId"] = "varve-webui",
                ["DataScope"] = "varve-webui-data",
                ["ManageScope"] = "varve-webui-manage",
            });
        await server.DeclareAsync(Declarations);
        return server;
    }

    // mock-oauth2-server's issuer (ADR 0100 layer b): VARVE_MOCK_OAUTH2 is its address.
    // Zitadel (ADR 0100 layer c): VARVE_ZITADEL_SEED is the file eng/zitadel-seed.cs wrote.
    private static string Zitadel()
    {
        string? seed = Environment.GetEnvironmentVariable("VARVE_ZITADEL_SEED");

        if (string.IsNullOrEmpty(seed))
        {
            if ((Environment.GetEnvironmentVariable("VARVE_AUTH_LEGS_REQUIRED") ?? "").Split(',').Contains("zitadel", StringComparer.Ordinal))
            {
                Assert.Fail("The zitadel leg is required here and VARVE_ZITADEL_SEED is not set.");
            }

            Assert.Skip("The zitadel leg needs VARVE_ZITADEL_SEED (ADR 0100).");
        }

        return seed;
    }

    // The person signs in through the API a login UI uses, and the issuer
    // answers where to send the browser back to.
    private static async Task<string> ApproveAsync(string issuer, JsonNode seed, string request)
    {
        using HttpClient login = new(new HttpClientHandler { UseProxy = false });
        login.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)seed["loginClientToken"]!);
        using HttpResponseMessage created = await login.PostAsync(new Uri(issuer + "/v2/sessions"), Body(new JsonObject
        {
            ["checks"] = new JsonObject
            {
                ["user"] = new JsonObject { ["loginName"] = (string)seed["reader"]!["loginName"]! },
                ["password"] = new JsonObject { ["password"] = (string)seed["reader"]!["password"]! },
            },
        }), TestContext.Current.CancellationToken);
        JsonNode session = JsonNode.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        using HttpResponseMessage callback = await login.PostAsync(new Uri(issuer + "/v2/oidc/auth_requests/" + request), Body(new JsonObject
        {
            ["session"] = new JsonObject { ["sessionId"] = (string)session["sessionId"]!, ["sessionToken"] = (string)session["sessionToken"]! },
        }), TestContext.Current.CancellationToken);
        return (string)JsonNode.Parse(await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["callbackUrl"]!;
    }

    private static StringContent Body(JsonObject json) => new(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    // The audiences a JWT names, read without validating.
    private static List<string> Audiences(string jwt)
    {
        string part = jwt.Split('.')[1];
        JsonNode? aud = JsonNode.Parse(Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/').PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=')))!["aud"];
        return aud is JsonArray many ? [.. many.Select(value => (string)value!)] : aud is null ? [] : [(string)aud!];
    }

    private static string Mock()
    {
        string? address = Environment.GetEnvironmentVariable("VARVE_MOCK_OAUTH2");

        if (string.IsNullOrEmpty(address))
        {
            if (Environment.GetEnvironmentVariable("VARVE_BROWSER_TESTS_REQUIRED") == "1")
            {
                Assert.Fail("VARVE_MOCK_OAUTH2 is needed for sign-in.");
            }

            Assert.Skip("VARVE_MOCK_OAUTH2 is needed for sign-in.");
        }

        return address.TrimEnd('/') + "/varve";
    }

    private static List<string> Watch(IPage page)
    {
        List<string> violations = [];
        page.PageError += (_, error) => violations.Add("page error: " + error);
        page.ExposeFunctionAsync("reportViolation", (string violation) => violations.Add("CSP: " + violation)).GetAwaiter().GetResult();
        page.AddInitScriptAsync("document.addEventListener('securitypolicyviolation', e => window.reportViolation(e.violatedDirective + ' ' + e.blockedURI))").GetAwaiter().GetResult();
        return violations;
    }

    private static async Task<IEnumerable<string>> AuditAsync(IPage page, string where)
    {
        AxeResult result = await page.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Wcag] } });
        return result.Violations.Select(violation => where + ": " + violation.Id + " (" + violation.Impact + ") " + string.Join(", ", violation.Nodes.Select(node => string.Join(" ", node.Target))));
    }

    private static async Task<List<string>> Planes(IPage page) =>
        [.. (await page.Locator(".vw-plane").AllInnerTextsAsync()).Select(text => string.Join(" ", text.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries)))];

    private static Dictionary<string, string> Query(string url) =>
        new Uri(url).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));

    private static string Access(string body) => (string)JsonNode.Parse(body)!["access_token"]!;

    private static async Task<HttpStatusCode> StatusAsync(HttpClient http, string path, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
