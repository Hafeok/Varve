// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The reader's public data is the model: an error, its kind and its position
// (ADR 0064, amended 2026-09-26). The readers and writers in
// Varve.Sparql.Results are not.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Sparql.Results.Model", typeof(VarveConfigurationAndHotPathRules.SyntaxPackagesHaveOneModelNamespace))]
