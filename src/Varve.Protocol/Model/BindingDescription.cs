// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Management.Model;
using Varve.Shacl.Model;

namespace Varve.Protocol.Model;

/// <summary>
/// A shapes binding as <c>GET /manage/datasets/{name}/bindings/{binding}</c>
/// serves it (ADR 0169): its declaration at the applied management position,
/// and — where this instance holds the dataset — its shapes' classification
/// (ADR 0171), or the problems that refuse them.
/// </summary>
/// <param name="Declaration">The binding as declared.</param>
/// <param name="Position">The management position it is declared at.</param>
/// <param name="Classification">The shapes' classification; null when the dataset is not held here, or its shapes are refused.</param>
/// <param name="Problems">Why its shapes graph is refused; empty otherwise.</param>
public sealed record BindingDescription(BindingDeclaration Declaration, ManagementPosition Position, ShapesClassification? Classification, IReadOnlyList<ShaclProblem> Problems);
