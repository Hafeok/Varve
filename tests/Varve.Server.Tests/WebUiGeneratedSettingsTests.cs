// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The Web UI in the image (ADR 0245): the off switch, the settings the
/// server answers from its own configuration, when it answers them and when
/// the bundle's file is served, and that nothing but the allow-listed fields
/// can ever reach the answer.
/// </summary>
public sealed class WebUiGeneratedSettingsTests : IDisposable
{
    private readonly string _bundle = Directory.CreateTempSubdirectory("varve-webui-").FullName;

    public WebUiGeneratedSettingsTests()
    {
        DateTime later = DateTime.UtcNow.AddMinutes(5);
        Write("index.html", "<!DOCTYPE html><title>Varve</title>");
        Write("appsettings.json", "{\"Varve\":{\"Auth\":{\"Mode\":\"Anonymous\"}}}");
        Write("appsettings.json.br", "brotli of the bundle's settings", later);
        Write("appsettings.json.gz", "gzip of the bundle's settings", later);
    }

    public void Dispose() => Directory.Delete(_bundle, recursive: true);

    [Fact]
    public async Task Switched_off_there_is_no_ui_even_with_a_path()
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:WebUi:Enabled"] = "false";
        settings["Varve:WebUi:Settings"] = "Generated";
        await using RunningServer server = await RunningServer.StartAsync(settings);

        foreach (string path in (string[])["ui/", "ui/appsettings.json", "ui/index.html"])
        {
            using HttpResponseMessage response = await server.Client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
        }
    }

    [Fact]
    public async Task With_File_the_bundles_own_settings_are_served_as_today()
    {
        await using RunningServer server = await RunningServer.StartAsync(Anonymous());

        using HttpResponseMessage plain = await server.Client.GetAsync("ui/appsettings.json", TestContext.Current.CancellationToken);
        Assert.Equal("{\"Varve\":{\"Auth\":{\"Mode\":\"Anonymous\"}}}", await plain.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using HttpRequestMessage request = new(HttpMethod.Get, "ui/appsettings.json");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");
        using HttpResponseMessage compressed = await server.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("br", Assert.Single(compressed.Content.Headers.ContentEncoding));
    }

    [Fact]
    public async Task Generated_on_an_anonymous_server_says_anonymous_and_never_sends_the_bundles_file()
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:WebUi:Settings"] = "Generated";
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpRequestMessage request = new(HttpMethod.Get, "ui/appsettings.json");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
        using HttpResponseMessage response = await server.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.StartsWith("default-src 'none';", Single(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));

        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement varve = json.RootElement.GetProperty("Varve");
        Assert.Equal(string.Empty, varve.GetProperty("Server").GetString());
        Assert.Equal("Anonymous", varve.GetProperty("Auth").GetProperty("Mode").GetString());
        Assert.Single(varve.GetProperty("Auth").EnumerateObject());

        // Revalidated by its tag.
        using HttpRequestMessage again = new(HttpMethod.Get, "ui/appsettings.json");
        again.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag!.Tag);
        using HttpResponseMessage unchanged = await server.Client.SendAsync(again, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);

        // The bundle's compressed copies are never served either.
        foreach (string path in (string[])["ui/appsettings.json.br", "ui/appsettings.json.gz"])
        {
            using HttpResponseMessage sibling = await server.Client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, sibling.StatusCode);
        }
    }

    [Fact]
    public async Task Generated_with_oidc_and_the_web_uis_client_carries_the_authority_the_client_and_the_scopes()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        Dictionary<string, string> settings = Oidc(issuer);
        settings["Varve:WebUi:Server"] = "https://varve.example/";
        settings["Varve:WebUi:Auth:ClientId"] = "varve-webui";
        settings["Varve:WebUi:Auth:DataScope"] = "api://varve/access_as_user";
        settings["Varve:WebUi:Auth:ManageScope"] = "api://varve-manage/access_as_user";
        settings["Varve:WebUi:Auth:ManageGrant"] = "exchange";
        settings["Varve:WebUi:Auth:ManageAudience"] = "varve-manage";
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpResponseMessage response = await server.Client.GetAsync("ui/appsettings.json", TestContext.Current.CancellationToken);
        Assert.Contains(new Uri(issuer.Issuer).GetLeftPart(UriPartial.Authority), Single(response, "Content-Security-Policy"), StringComparison.Ordinal);

        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement varve = json.RootElement.GetProperty("Varve");
        Assert.Equal("https://varve.example/", varve.GetProperty("Server").GetString());
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Mode"] = "Oidc",
                ["Authority"] = issuer.Issuer,
                ["ClientId"] = "varve-webui",
                ["DataScope"] = "api://varve/access_as_user",
                ["ManageScope"] = "api://varve-manage/access_as_user",
                ["ManageGrant"] = "exchange",
                ["ManageAudience"] = "varve-manage",
                ["RoleClaimType"] = "roles",
            },
            varve.GetProperty("Auth").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
    }

    [Fact]
    public async Task Generated_with_oidc_and_no_client_is_not_configured_and_names_the_settings_never_anonymous()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        Dictionary<string, string> settings = Oidc(issuer);
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpResponseMessage response = await server.Client.GetAsync("ui/appsettings.json", TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        JsonElement auth = json.RootElement.GetProperty("Varve").GetProperty("Auth");
        Assert.Equal("NotConfigured", auth.GetProperty("Mode").GetString());
        Assert.Equal(
            ["Varve:WebUi:Auth:ClientId", "Varve:WebUi:Auth:DataScope", "Varve:WebUi:Auth:ManageScope"],
            auth.GetProperty("Missing").EnumerateArray().Select(e => e.GetString()!));
        Assert.Equal(["Mode", "Missing"], auth.EnumerateObject().Select(p => p.Name));
    }

    // Every string the server's settings hold is set to a value of its own,
    // the secret-shaped ones among them; only the allow-listed fields' values
    // may appear in the answer, with the client configured or not. A setting
    // added later is covered without touching this test.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Nothing_but_the_allow_listed_settings_ever_reaches_the_answer(bool client)
    {
        ServerSettings settings = new();
        Dictionary<string, string> planted = [];
        Plant(settings, "Varve", planted);
        settings.Auth.Mode = "Oidc";
        settings.WebUi.Settings = WebUiSettingsSource.Generated;

        if (!client)
        {
            settings.WebUi.Auth = new WebUiAuthSettings();
        }

        string answer = Encoding.UTF8.GetString(GeneratedWebUiSettings.Write(GeneratedWebUiSettings.From(settings)));
        string[] allowed =
        [
            "Varve:Auth:Authority", "Varve:Auth:RoleClaimType", "Varve:WebUi:Server",
            "Varve:WebUi:Auth:ClientId", "Varve:WebUi:Auth:DataScope", "Varve:WebUi:Auth:ManageScope",
            "Varve:WebUi:Auth:ManageGrant", "Varve:WebUi:Auth:DataAudience", "Varve:WebUi:Auth:ManageAudience",
        ];

        Assert.True(planted.Count > 30, "the walk planted " + planted.Count + " values");
        Assert.Contains(planted, p => p.Key.EndsWith("ContainerUrl", StringComparison.Ordinal));
        Assert.Empty(planted.Where(p => !allowed.Contains(p.Key) && answer.Contains(p.Value, StringComparison.Ordinal)).Select(p => p.Key));
        Assert.Equal(client, answer.Contains(planted["Varve:WebUi:Auth:ClientId"], StringComparison.Ordinal));
    }

    [Fact]
    public void The_web_uis_settings_are_refused_where_they_would_do_nothing_or_are_half_set()
    {
        ServerSettings file = Checked(s => s.WebUi.Auth.ClientId = "varve-webui");
        Assert.Contains(SettingsCheck.Errors(file), e => e.StartsWith("Varve:WebUi:Auth:ClientId is set, and is read only when Varve:WebUi:Settings is Generated", StringComparison.Ordinal));

        ServerSettings server = Checked(s => s.WebUi.Server = "https://varve.example/");
        Assert.Contains(SettingsCheck.Errors(server), e => e.StartsWith("Varve:WebUi:Server is set, and is read only when", StringComparison.Ordinal));

        ServerSettings unknown = Checked(s => s.WebUi.Settings = "Bundle");
        Assert.Contains(SettingsCheck.Errors(unknown), e => e.StartsWith("Varve:WebUi:Settings is File or Generated", StringComparison.Ordinal));

        ServerSettings half = Checked(s =>
        {
            s.WebUi.Settings = WebUiSettingsSource.Generated;
            s.WebUi.Auth.ClientId = "varve-webui";
        });
        Assert.Contains(SettingsCheck.Errors(half), e => e == "The Web UI's client is half configured: Varve:WebUi:Auth:DataScope, Varve:WebUi:Auth:ManageScope are not set (ADR 0245, ADR 0201 point 5).");

        ServerSettings grant = Checked(s =>
        {
            s.WebUi.Settings = WebUiSettingsSource.Generated;
            s.WebUi.Auth = new WebUiAuthSettings { ClientId = "c", DataScope = "d", ManageScope = "m", ManageGrant = "implicit" };
        });
        Assert.Contains(SettingsCheck.Errors(grant), e => e.StartsWith("Varve:WebUi:Auth:ManageGrant is refresh or exchange", StringComparison.Ordinal));

        ServerSettings address = Checked(s =>
        {
            s.WebUi.Settings = WebUiSettingsSource.Generated;
            s.WebUi.Server = "varve.example";
        });
        Assert.Contains(SettingsCheck.Errors(address), e => e.StartsWith("Varve:WebUi:Server is the server's absolute http or https address", StringComparison.Ordinal));

        ServerSettings anonymous = Checked(s =>
        {
            s.Auth = new AuthSettings { Mode = "Anonymous" };
            s.WebUi.Settings = WebUiSettingsSource.Generated;
            s.WebUi.Auth.ClientId = "varve-webui";
        });
        Assert.Contains(SettingsCheck.Errors(anonymous), e => e.StartsWith("Varve:WebUi:Auth:ClientId is set on an anonymous server", StringComparison.Ordinal));

        ServerSettings whole = Checked(s =>
        {
            s.WebUi.Settings = WebUiSettingsSource.Generated;
            s.WebUi.Auth = new WebUiAuthSettings { ClientId = "c", DataScope = "d", ManageScope = "m", ManageGrant = "refresh" };
        });
        Assert.DoesNotContain(SettingsCheck.Errors(whole), e => e.Contains("WebUi", StringComparison.Ordinal));

        ServerSettings none = Checked(s => s.WebUi.Settings = WebUiSettingsSource.Generated);
        Assert.DoesNotContain(SettingsCheck.Errors(none), e => e.Contains("WebUi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_web_ui_setting_that_would_do_nothing_refuses_the_start()
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:WebUi:Server"] = "https://varve.example/";
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => RunningServer.StartAsync(settings));
        Assert.Contains("exit 2", refused.Message, StringComparison.Ordinal);
    }

    // A valid OIDC server's settings, changed by the case.
    private static ServerSettings Checked(Action<ServerSettings> change)
    {
        ServerSettings settings = new()
        {
            Auth = new AuthSettings { Mode = "Oidc", Authority = "https://login.example/", Audiences = ["api://varve"] },
        };
        change(settings);
        return settings;
    }

    // Sets every string property under the object to a value of its own,
    // named by its configuration path, and every list of strings to one.
    private static void Plant(object target, string path, Dictionary<string, string> planted)
    {
        foreach (PropertyInfo property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string name = path + ":" + property.Name;

            if (property.PropertyType == typeof(string) && property.CanWrite)
            {
                string value = "planted-" + Guid.NewGuid().ToString("N");
                property.SetValue(target, value);
                planted[name] = value;
            }
            else if (property.PropertyType == typeof(List<string>))
            {
                string value = "planted-" + Guid.NewGuid().ToString("N");
                ((List<string>)property.GetValue(target)!).Add(value);
                planted[name] = value;
            }
            else if (property.PropertyType.IsClass && property.PropertyType.Namespace == typeof(ServerSettings).Namespace && property.GetValue(target) is { } child)
            {
                Plant(child, name, planted);
            }
        }
    }

    private Dictionary<string, string> Anonymous() => new()
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:WebUi:Path"] = _bundle,
    };

    private Dictionary<string, string> Oidc(TestIssuer issuer) => new()
    {
        ["Varve:Auth:Mode"] = "Oidc",
        ["Varve:Auth:Authority"] = issuer.Issuer,
        ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
        ["Varve:Auth:RequireHttpsMetadata"] = "false",
        ["Varve:Manage:BootstrapOperators:0"] = "operator",
        ["Varve:WebUi:Path"] = _bundle,
        ["Varve:WebUi:Settings"] = "Generated",
    };

    private void Write(string relative, string text, DateTime? modified = null)
    {
        string path = Path.Combine(_bundle, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modified ?? DateTime.UtcNow);
    }

    private static string Single(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : throw new InvalidOperationException("No " + name + " header.");
}
