// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The values the algorithms take and answer are the model: a polarity, an
// edge, a refused cycle (ADR 0172). The algorithms and the graph a path walks
// are not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Derivation.Model", typeof(VarveDerivation.DerivationModelNamespace))]
