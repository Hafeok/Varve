// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The parser's public data is the model: a rejected line, its kind and
// position, and the answer an error handler gives (ADR 0064, amended
// 2026-09-26). The parsers, readers and writers in Varve.Turtle are not.
// Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Turtle.Model", typeof(VarveConfigurationAndHotPathRules.SyntaxPackagesHaveOneModelNamespace))]
