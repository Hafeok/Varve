// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Fixture.UndeclaredApi;

/// <summary>
/// A public type that appears in neither PublicAPI.Shipped.txt nor
/// PublicAPI.Unshipped.txt, both of which sit beside this project and contain
/// only their header. This must not compile.
/// </summary>
public sealed class Marker
{
    /// <summary>A public member nobody declared.</summary>
    public int Value { get; }
}
