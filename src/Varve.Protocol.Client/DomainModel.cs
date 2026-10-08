// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the client answers with is the model: a fetched document, a
// refusal, the admin API's responses (ADR 0102). The clients, the handler and
// the policy are not.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Protocol.Client.Model", typeof(VarveProtocolClient.ClientModelNamespace))]
