// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// A classifier binding is the model: the value a host maps a declaration
// to (ADR 0234). The classifier and the projection are not. Declared
// before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Classification.Model", typeof(TheShapeDerivedClassifier.VarveClassificationAtLayerFive))]
