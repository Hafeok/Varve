// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// A binding and its mode are the model: the values a host maps a
// declaration to (ADR 0170). The gate and its options are not. Declared
// before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Shacl.Store.Model", typeof(CommitTimeGatingUnderAShapesBinding.ShaclStoreAtLayerFive))]
