// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>The one definition of "this quad matches that pattern" the layer's sources share.</summary>
internal static class QuadPatterns
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool Matches(in Quad quad, TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        (subject.IsNone || subject.Equals(quad.Subject))
        && (predicate.IsNone || predicate.Equals(quad.Predicate))
        && (@object.IsNone || @object.Equals(quad.Object))
        && graph.Matches(quad.Graph);
}
