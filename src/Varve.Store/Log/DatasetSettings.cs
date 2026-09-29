// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Log;

/// <summary>
/// The dataset's settings: a fold over its <see cref="CommitKind.Settings"/>
/// commits, so every copy of the dataset agrees on them (ADR 0021).
/// </summary>
/// <remarks>
/// Erasure mode is part of the fold and is always off before milestone 9; it
/// is deliberately not public until something can turn it on.
/// </remarks>
public sealed class DatasetSettings
{
    internal DatasetSettings(AccessScope defaultAccessScope) => DefaultAccessScope = defaultAccessScope;

    internal static DatasetSettings Default { get; } = new(AccessScope.AllHistory);

    /// <summary>What an access request covers when it does not say.</summary>
    public AccessScope DefaultAccessScope { get; }

    internal static bool ErasureMode => false;
}
