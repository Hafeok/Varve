// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The whole assembly is the RDF model: terms, quads, the handle, the quad
// source contract, the delta, the overlay, and InMemoryDataset as an immutable
// value assembled by a sealed builder (ADR 0067). The package matches
// sub-namespaces too, and DD0006 keeps every public type under the root, so
// nothing public here is outside it. Several ADRs define parts of it (0022,
// 0024, 0067); ADR 0064 is the one decision that declares the namespace a
// model, and it is declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Rdf", typeof(VarveConfigurationAndHotPathRules.DomainModelNamespaces))]
