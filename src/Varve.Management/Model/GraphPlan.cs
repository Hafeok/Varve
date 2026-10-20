// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Management.Model;

/// <summary>One owned graph's part of a plan: its version, its desired content and the diff.</summary>
/// <param name="Graph">The graph.</param>
/// <param name="Version">The graph's version the plan was made against: the position of the last commit that touched it, or 0.</param>
/// <param name="Content">The desired content, as canonical N-Quads lines.</param>
/// <param name="Added">The lines the apply adds.</param>
/// <param name="Removed">The lines the apply removes.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record GraphPlan(RdfTerm Graph, ManagementPosition Version, IReadOnlyList<string> Content, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

