// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Server;

/// <summary>
/// The Web UI's settings as the server answers them at
/// <c>/ui/appsettings.json</c> when <c>Varve:WebUi:Settings</c> is
/// <c>Generated</c> (ADR 0245 point 3), in the shape the bundle's own file has
/// (ADR 0201 point 5).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Written field by field from <see cref="WebUiDocument"/>, an
///   allow-list: no section of the configuration is ever copied, so nothing
///   the server holds that is not named here can reach the answer. The Web
///   UI's client is public, and has no secret to carry.</item>
///   <item>An anonymous server is <c>Anonymous</c>. A server with OIDC and the
///   Web UI's client is <c>Oidc</c>, with the server's own authority, which is
///   also the origin the policy's <c>connect-src</c> names (ADR 0202). A
///   server with OIDC and no client is <c>NotConfigured</c>, naming the
///   settings to add: never <c>Anonymous</c>.</item>
///   <item>The document is <c>Varve</c> holding <c>Server</c> and
///   <c>Auth</c>; a second address, the management plane's (#297), is one
///   more field beside <c>Server</c>.</item>
/// </list>
/// </remarks>
[Contract(typeof(TheWebUiInTheContainerImage.GeneratedSettingsAreAnAllowList), Role = "the Web UI's settings, written field by field from a closed record and never from a configuration section")]
internal static class GeneratedWebUiSettings
{
    internal const string Anonymous = "Anonymous";

    internal const string Oidc = "Oidc";

    internal const string NotConfigured = "NotConfigured";

    /// <summary>The client settings without which the Web UI cannot sign in, as <c>Varve:…</c> names, in a stated order.</summary>
    internal static List<string> MissingClientSettings(WebUiAuthSettings auth)
    {
        List<string> missing = [];

        if (string.IsNullOrWhiteSpace(auth.ClientId))
        {
            missing.Add("Varve:WebUi:Auth:ClientId");
        }

        if (string.IsNullOrWhiteSpace(auth.DataScope))
        {
            missing.Add("Varve:WebUi:Auth:DataScope");
        }

        if (string.IsNullOrWhiteSpace(auth.ManageScope))
        {
            missing.Add("Varve:WebUi:Auth:ManageScope");
        }

        return missing;
    }

    /// <summary>What the server's configuration says the Web UI is to do.</summary>
    internal static WebUiDocument From(ServerSettings settings)
    {
        WebUiSettings ui = settings.WebUi;
        string server = string.IsNullOrWhiteSpace(ui.Server) ? string.Empty : ui.Server;

        if (settings.Auth.Mode == "Anonymous")
        {
            return new WebUiDocument(server, new WebUiDocumentAuth(Anonymous));
        }

        List<string> missing = MissingClientSettings(ui.Auth);

        if (missing.Count > 0)
        {
            return new WebUiDocument(server, new WebUiDocumentAuth(NotConfigured, Missing: missing));
        }

        WebUiAuthSettings client = ui.Auth;
        return new WebUiDocument(server, new WebUiDocumentAuth(
            Oidc,
            Authority: settings.Auth.Authority,
            ClientId: client.ClientId,
            DataScope: client.DataScope,
            ManageScope: client.ManageScope,
            ManageGrant: Blank(client.ManageGrant),
            DataAudience: Blank(client.DataAudience),
            ManageAudience: Blank(client.ManageAudience),
            RoleClaimType: settings.Auth.RoleClaimType));
    }

    /// <summary>The document as the Web UI reads it: UTF-8 JSON, unset fields left out.</summary>
    internal static byte[] Write(WebUiDocument document)
    {
        using MemoryStream bytes = new();

        using (Utf8JsonWriter json = new(bytes, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartObject("Varve");
            json.WriteString("Server", document.Server);
            json.WriteStartObject("Auth");
            WebUiDocumentAuth auth = document.Auth;
            json.WriteString("Mode", auth.Mode);
            Optional(json, "Authority", auth.Authority);
            Optional(json, "ClientId", auth.ClientId);
            Optional(json, "DataScope", auth.DataScope);
            Optional(json, "ManageScope", auth.ManageScope);
            Optional(json, "ManageGrant", auth.ManageGrant);
            Optional(json, "DataAudience", auth.DataAudience);
            Optional(json, "ManageAudience", auth.ManageAudience);
            Optional(json, "RoleClaimType", auth.RoleClaimType);

            if (auth.Missing is { } missing)
            {
                json.WriteStartArray("Missing");

                foreach (string name in missing)
                {
                    json.WriteStringValue(name);
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return bytes.ToArray();
    }

    private static void Optional(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>The generated settings: the server the Web UI calls, and how it signs in.</summary>
/// <param name="Server">The server's base address; empty for the origin that served the Web UI.</param>
/// <param name="Auth">The sign-in settings.</param>
internal sealed record WebUiDocument(string Server, WebUiDocumentAuth Auth);

/// <summary>The sign-in settings of ADR 0201 point 5, and the settings to add when sign-in is not configured.</summary>
[Contract(typeof(TheWebUiInTheContainerImage.NotConfiguredIsNeverAnonymous), Role = "the sign-in mode the server answers: NotConfigured, with the settings to add, for an OIDC server with no client for the Web UI; never Anonymous")]
internal sealed record WebUiDocumentAuth(
    string Mode,
    string? Authority = null,
    string? ClientId = null,
    string? DataScope = null,
    string? ManageScope = null,
    string? ManageGrant = null,
    string? DataAudience = null,
    string? ManageAudience = null,
    string? RoleClaimType = null,
    IReadOnlyList<string>? Missing = null);
