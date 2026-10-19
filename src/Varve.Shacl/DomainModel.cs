// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The values a validation answers are the model: the report, its results and
// summary, and the problems of a refusal or a failure (ADR 0155). The
// validator, the options, the extension contract and the internal shapes
// model are not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Shacl.Model", typeof(AMinimalPublicShaclApi.ShaclModelNamespace))]
