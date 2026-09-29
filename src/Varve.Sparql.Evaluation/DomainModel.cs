// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the evaluator's contracts name is the model: what a query form
// answers, and what a SERVICE handler is asked and answers (ADR 0069). The
// optimiser, the compiler and the operators in Varve.Sparql.Evaluation are
// not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Sparql.Evaluation.Model", typeof(ModelNamespacesForLayers3To5.EvaluationModel))]
