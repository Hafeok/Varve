// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Browser.Model;

/// <summary>How a <see cref="BrowserStorage"/> is opened.</summary>
public sealed class BrowserStorageOptions
{
    /// <summary>
    /// The clock the lease's diagnostic timestamp is read from. Required: the
    /// store never reads the machine's clock itself (ADR 0011).
    /// </summary>
    public required TimeProvider Clock { get; init; }
}
