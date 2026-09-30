// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Evaluation.Model;

/// <summary>What a query form answers.</summary>
public enum QueryResultKind : byte
{
    /// <summary>A <c>SELECT</c>: a sequence of solutions.</summary>
    Solutions,

    /// <summary>An <c>ASK</c>: a boolean.</summary>
    Boolean,

    /// <summary>A <c>CONSTRUCT</c> or <c>DESCRIBE</c>: triples.</summary>
    Triples,
}
