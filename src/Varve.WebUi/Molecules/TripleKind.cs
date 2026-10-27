// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.WebUi.Molecules;

/// <summary>What a statement row is (docs/ui/components/Triple).</summary>
public enum TripleKind
{
    /// <summary>Asserted, with no change.</summary>
    Asserted,

    /// <summary>Asserted by a change, with a +.</summary>
    Added,

    /// <summary>Retracted by a change, with a −, struck through.</summary>
    Removed,

    /// <summary>Entailed: the predicate dashed, and the word "inferred" in the note.</summary>
    Inferred,
}
