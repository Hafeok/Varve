// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Fixture.AmbientClock;

internal sealed class Marker
{
    /// <summary>
    /// Reads the machine's clock and a random source, which
    /// eng/BannedSymbols.Deterministic.txt bans (ADR 0011). This must not compile.
    /// </summary>
    internal static long Stamp() => DateTimeOffset.UtcNow.Ticks + Random.Shared.Next();
}
