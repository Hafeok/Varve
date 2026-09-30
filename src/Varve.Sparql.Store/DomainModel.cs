// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the integration's contracts name is the model: the document a
// LOAD source answers with (ADR 0069). The request execution in
// Varve.Sparql.Store is not. Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Sparql.Store.Model", typeof(ModelNamespacesForLayers3To5.SparqlStoreModel))]
