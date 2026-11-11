// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.WebUi;

/// <summary>
/// The server the Web UI reads (ADR 0202): <c>Varve:Server</c> from the
/// bundle's <c>appsettings.json</c> when a static host serves it, or else the
/// origin that served the bundle, which is the server itself under
/// <c>/ui/</c>.
/// </summary>
internal static class ServerAddress
{
    internal static Uri From(string? configured, Uri served)
    {
        if (!string.IsNullOrEmpty(configured))
        {
            return Uri.TryCreate(configured, UriKind.Absolute, out Uri? address) && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
                ? address
                : throw new InvalidOperationException("Varve:Server is an absolute http or https URL; it is '" + configured + "'.");
        }

        return new Uri(served.GetLeftPart(UriPartial.Authority) + "/");
    }
}
