// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the backend's contract names is the model: the dataset's name,
// which backend it is, and how it is opened (ADR 0084, after ADR 0069).
// Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Store.Browser.Model", typeof(TheBrowserBackend.BrowserStoreModel))]
