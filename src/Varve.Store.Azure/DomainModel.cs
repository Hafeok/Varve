// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the adapter's contract names is the model: how it is opened
// (ADR 0137, after ADR 0069). Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Store.Azure.Model", typeof(StorageCredentialsAreWorkloadIdentity.AdapterModels))]
