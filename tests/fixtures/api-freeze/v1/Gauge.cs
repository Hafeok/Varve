// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Fixture.ApiFreeze;

/// <summary>The fixture's surface at its baseline: two members.</summary>
public static class Gauge
{
    /// <summary>Reads the gauge.</summary>
    public static int Read() => 1;

    /// <summary>Resets the gauge.</summary>
    public static void Reset()
    {
    }
}
