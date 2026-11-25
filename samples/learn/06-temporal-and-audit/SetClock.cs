// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Learn;

// region: clock
/// <summary>A clock the sample sets by hand, so that every run records the same commit times.</summary>
internal sealed class SetClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    /// <summary>Moves the clock to <paramref name="now"/>.</summary>
    internal void Set(DateTimeOffset now) => _now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}
// endregion: clock
