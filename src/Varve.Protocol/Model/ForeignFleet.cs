// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store;

namespace Varve.Protocol.Model;

/// <summary>
/// The fleet that wrote a commit of a copy, where it is not the fleet that
/// serves it (ADR 0185 decision 8): its system dataset, as shipped beside
/// the copy and followed read-only, and the name the dataset has in it. The
/// management position such a commit records is a position of that system
/// dataset, and means nothing in the serving fleet's.
/// </summary>
/// <param name="System">The source fleet's system dataset as this instance follows it, or none where it was not shipped or cannot be opened: what the commit's position meant then cannot be said.</param>
/// <param name="Dataset">The dataset's name in that fleet.</param>
[Contract(typeof(PromotionAcrossCopies.AManagementPositionBelongsToItsFleet), Role = "the fleet a copy's commit was written under")]
public sealed record ForeignFleet(Dataset? System, DatasetName Dataset);
