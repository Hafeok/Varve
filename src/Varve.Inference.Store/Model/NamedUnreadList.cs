// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference.Store.Model;

/// <summary>
/// A cell of an asserted list a binding's rules do not read (ADR 0197,
/// amended 2026-11-24), by name: the graph whose statements make it unread,
/// the cell, and why. What the binding's status reports.
/// </summary>
/// <param name="Graph">The data graph, or the binding's TBox graph.</param>
/// <param name="Cell">The subject of the asserted <c>rdf:first</c> or <c>rdf:rest</c>.</param>
/// <param name="Reason">Why it is not read.</param>
public sealed record NamedUnreadList(RdfTerm Graph, RdfTerm Cell, UnreadListReason Reason);
