// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Entailment;

/// <summary>Whether a rewriting drops the branches it proves add nothing (ADR 0193 point 3).</summary>
internal enum Pruning : byte
{
    /// <summary>Drops them: the rewriting every query gets.</summary>
    On,

    /// <summary>Keeps every branch: the oracle of the property that pruning changes no answer.</summary>
    Off,
}
