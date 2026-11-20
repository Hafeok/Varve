// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Model;

/// <summary>
/// A declared shipping target whose provider is not verified (ADR 0184
/// decision 9): the dataset that declares it, the provider id it names, and
/// what the compatibility page compiled into the build says of that id.
/// </summary>
/// <param name="Dataset">The dataset whose declaration names the target.</param>
/// <param name="Provider">The <c>varve:provider</c> it names.</param>
/// <param name="State">The page's state for it: <c>untested</c>, <c>unsupported</c>, <c>no adapter</c>, or <c>unknown</c> for an id the page does not list.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record UnverifiedTarget(string Dataset, string Provider, string State);
