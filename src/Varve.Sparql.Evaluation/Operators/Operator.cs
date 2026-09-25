// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>
/// An operator of the evaluator (<c>sparql-evaluation.md</c> §6), evaluated
/// given an incoming solution and the active graph. Its output extends the
/// incoming solution; each array it yields is new and is never changed again,
/// so a consumer may keep it.
/// </summary>
internal abstract class Operator
{
    /// <summary>
    /// Whether evaluating given a solution is the same as evaluating alone and
    /// keeping the compatible results merged with it — the property that lets
    /// <c>Join</c> bind the left side's values into the right side's scans (§6).
    /// </summary>
    internal virtual bool Substitutable => false;

    /// <summary>
    /// Whether every solution binds the graph variable of an enclosing
    /// <c>GRAPH ?g</c> through a scan, so the scans can bind it themselves
    /// (§6.11); false sends the <c>GRAPH</c> through the named graphs one by one.
    /// </summary>
    internal virtual bool ScansBindGraph => false;

    /// <summary>The slots every solution of this operator binds, for hash-join keys.</summary>
    internal int[] Certain { get; set; } = [];

    internal abstract IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph);
}
