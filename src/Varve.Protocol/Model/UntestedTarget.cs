// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Model;

/// <summary>
/// A declared shipping target whose provider the compatibility page compiled
/// into the build does not list as verified (ADR 0184 decision 9). It is a
/// notice and never a refusal: the declaration is applied, and shipping
/// starts once the target's store holds a verification record.
/// </summary>
/// <param name="Dataset">The dataset whose declaration names the target.</param>
/// <param name="Provider">The <c>varve:provider</c> it names.</param>
/// <param name="State">The page's state for it: <c>untested</c>, <c>unsupported</c>, <c>no adapter</c>, or <c>unknown</c> for an id the page does not list.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed record UntestedTarget(string Dataset, string Provider, string State)
{
    /// <summary>The response header an apply carries one value of per such target (ADR 0184 decision 9).</summary>
    public const string Header = "Varve-Notice";

    /// <summary>The header's value: <c>shipping-provider-untested; dataset=…; provider=…; state=…</c>.</summary>
    public string HeaderValue => "shipping-provider-untested; dataset=" + Dataset + "; provider=" + Provider + "; state=" + State.Replace(' ', '-');
}
