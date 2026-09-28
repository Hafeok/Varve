// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The log is the model: the values a commit, its outcome, the settings and a
// segment are made of (ADR 0065). The engine that sequences, reads, indexes
// and serves the log, in Varve.Store itself, is not. Declared before any
// [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Store.Log", typeof(WrapperTypesAndTheStoreLogNamespace.LogIsTheModel))]
