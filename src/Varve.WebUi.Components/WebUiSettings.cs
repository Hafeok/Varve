// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.WebUi.Components;

/// <summary>What the composition root decided at boot: the server, and how the Web UI signs in.</summary>
/// <param name="Server">The server's base address (ADR 0202).</param>
/// <param name="Anonymous">No sign-in: the server runs with <c>Auth:Mode</c> <c>Anonymous</c>.</param>
[Contract(typeof(TheWebUiInTheContainerImage.NotConfiguredIsNeverAnonymous), Role = "only Anonymous is anonymous; any mode but Oidc and Anonymous is not configured")]
public sealed record WebUiSettings(Uri Server, bool Anonymous)
{
    /// <summary>
    /// Set when sign-in is not configured (ADR 0245): the settings the server
    /// names as missing, empty when the mode itself is missing or unknown.
    /// The Web UI then neither signs in nor reads the server, and says so.
    /// </summary>
    public IReadOnlyList<string>? SignInNotConfigured { get; init; }

    /// <summary>
    /// The settings from <c>Varve:Auth:Mode</c>: only <c>Anonymous</c> is
    /// anonymous and only <c>Oidc</c> signs in. <c>NotConfigured</c>, which
    /// a server answers when it has OIDC and no client for the Web UI, and a
    /// mode missing or unknown, are not configured: never anonymous.
    /// </summary>
    public static WebUiSettings From(Uri server, string? mode, IReadOnlyList<string> missing) => mode switch
    {
        "Anonymous" => new WebUiSettings(server, Anonymous: true),
        "Oidc" => new WebUiSettings(server, Anonymous: false),
        _ => new WebUiSettings(server, Anonymous: false) { SignInNotConfigured = missing },
    };
}
