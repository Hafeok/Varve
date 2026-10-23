// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Derivation.Model;

/// <summary>
/// How a dependency's truth reaches its dependant (ADR 0150's polarities,
/// ADR 0172): a stratum may contain positive dependencies only.
/// </summary>
public enum Polarity
{
    /// <summary>More true below makes no less true above: monotone.</summary>
    Positive,

    /// <summary>More true below can make less true above: negation.</summary>
    Negative,

    /// <summary>Both at once, counted as a positive and a negative dependency.</summary>
    Both,

    /// <summary>The dependency decides what the dependant applies to, which must be settled before it.</summary>
    Target,
}
