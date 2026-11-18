// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Fixture.ApiFreeze;

/// <summary>The fixture's surface with <c>Peek</c> added: not a breaking change.</summary>
public static class Gauge
{
    /// <summary>Reads the gauge.</summary>
    public static int Read() => 1;

    /// <summary>Resets the gauge.</summary>
    public static void Reset()
    {
    }

    /// <summary>Reads the gauge without moving it.</summary>
    public static int Peek() => 1;
}
