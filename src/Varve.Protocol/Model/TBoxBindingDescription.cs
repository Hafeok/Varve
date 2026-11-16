// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Management.Model;
using Varve.Sparql.Entailment.Model;

namespace Varve.Protocol.Model;

/// <summary>
/// A TBox binding as <c>GET /manage/datasets/{name}/bindings/{binding}</c>
/// serves it (ADR 0194): its declaration at the applied management position,
/// and — where this instance holds the dataset — the graphs, among its TBox
/// graphs and the graphs it covers, whose content makes a schema read its
/// scoping graph whole (ADR 0193 point 2).
/// </summary>
/// <param name="Declaration">The binding as declared.</param>
/// <param name="Position">The management position it is declared at.</param>
/// <param name="FullReads">The graphs that force a full read; null when the dataset is not held here.</param>
public sealed record TBoxBindingDescription(TBoxBindingDeclaration Declaration, ManagementPosition Position, FullReadGraphs? FullReads);
