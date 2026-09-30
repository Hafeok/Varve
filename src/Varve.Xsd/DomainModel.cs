// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The whole assembly is the XSD value model: the value types, their parsing
// and formatting, and their comparison. ADR 0051 defines it (ADR 0064's
// table), and it is declared before any [Contract] in this assembly.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

[assembly: DomainModel("Varve.Xsd", typeof(VarveXsdScopeAndPrecision.XsdScope))]
