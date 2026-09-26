// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The whole assembly is the IRI model: IriRef, its components and its errors.
// No ADR defines the IRI model on its own; ADR 0064 is the decision that
// declares this namespace a model, and it is declared before any [Contract]
// in this assembly, as the package requires.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Iri", typeof(VarveConfigurationAndHotPathRules.DomainModelNamespaces))]
