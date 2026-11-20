// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The values the projection takes and answers are the model: a binding, its
// options, the rows read at a position, the projection's state (ADR 0195).
// Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Inference.Store.Model", typeof(TheInferenceProjection.OneProjectionPerMaterialisingBinding))]
