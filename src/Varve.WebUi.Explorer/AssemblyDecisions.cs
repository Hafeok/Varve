// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

// DD0001: the explorer references the shared components within layer 6, both
// private assemblies of one host's composition root (ADR 0252).
[assembly: DesignDecision(typeof(TheWebUisPrivateAssemblies.ExplorerReferencesComponentsWithinLayerSix), Scope = ExceptionScope.Boundary)]
