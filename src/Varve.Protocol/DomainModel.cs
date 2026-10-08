// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The data the protocol's contracts and endpoints name is the model: dataset
// names, as-of selectors, limits, problem types and the change feed's records
// (ADR 0091). The endpoints, the options, the seams and the reader are not.
// Declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Protocol.Model", typeof(VarveProtocolAndVarveServer.ProtocolModelNamespace))]
