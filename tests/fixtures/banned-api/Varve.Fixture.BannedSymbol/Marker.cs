// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Fixture.BannedSymbol;

internal sealed class Marker
{
    /// <summary>
    /// Uses <c>System.Uri</c>, which eng/BannedSymbols.txt bans (ADR 0004).
    /// This must not compile.
    /// </summary>
    internal static bool IsAbsolute(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out Uri? _);
}
