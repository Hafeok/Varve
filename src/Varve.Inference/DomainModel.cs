// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The values the engine takes and answers are the model: a fact, a profile,
// a clash (ADR 0197). The rules, the joins and the closure are not. Declared
// before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Inference.Model", typeof(Owl2RlAndRdfsAsRules.InferenceModelNamespace))]
