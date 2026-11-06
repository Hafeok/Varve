// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The values the rewriting takes and answers are the model: the regime
// (ADR 0192). The rewriting itself, the merge and the schema are not.
// Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Sparql.Entailment.Model", typeof(TheInferencePackagesAndTheirLayers.EntailmentModelNamespace))]
