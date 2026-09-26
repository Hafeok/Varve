// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The algebra is the model: the node types the parser produces, the
// serialiser writes and the evaluator runs (ADR 0048, ADR 0064's table). The
// parser and the writer, in Varve.Sparql.Parsing and Varve.Sparql.Writing, are
// not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Sparql.Algebra", typeof(OptimiserAndEvaluatorOnePackageAlgebraInAlgebraOut.AlgebraNodesAreSealedRecords))]
