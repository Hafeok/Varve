// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The format's public data is the model: a record, its changes, its header's
// position, timestamp and kind (ADR 0064, amended 2026-09-26). The reader and
// the writer are not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Delta.Model", typeof(VarveConfigurationAndHotPathRules.SyntaxPackagesHaveOneModelNamespace))]
