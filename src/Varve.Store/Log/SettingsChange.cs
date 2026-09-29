// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Log;

/// <summary>A change to the settings: every field left null is unchanged.</summary>
public sealed class SettingsChange
{
    /// <summary>A new default access scope, or null to leave it.</summary>
    public AccessScope? DefaultAccessScope { get; init; }
}
