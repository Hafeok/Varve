// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.AspNetCore.Components;
using Varve.WebUi.Explorer.Atoms;

namespace Varve.WebUi.Explorer.Organisms;

/// <summary>One graph's group in a <c>GraphGroup</c>: the graph, a count and the page's body for it.</summary>
/// <param name="Graph">The graph, or null for the default graph.</param>
/// <param name="Count">How many changes or results, in words.</param>
/// <param name="Body">The group's table, which the page passes (a TripleList).</param>
public sealed record GraphSection(TermValue? Graph, string Count, RenderFragment Body);
