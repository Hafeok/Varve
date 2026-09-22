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
