// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the backend's contract names is the model: how it is opened, by
// whom, and the writer record it keeps (ADR 0135, after ADR 0069). Declared
// before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Store.ObjectStorage.Model", typeof(TheLogAsChunkObjects.ObjectStorageModel))]
