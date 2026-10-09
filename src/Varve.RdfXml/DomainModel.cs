// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The parser's public data is the model: a rejected document, its kind and
// position (ADR 0064, amended 2026-09-26). The parser and the writer in
// Varve.RdfXml are not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.RdfXml.Model", typeof(VarveConfigurationAndHotPathRules.SyntaxPackagesHaveOneModelNamespace))]
