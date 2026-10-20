// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Management.Model;

/// <summary>Whom a grant names (ADR 0166): an agent by its IRI, or a role or a group by a claim's value.</summary>
/// <param name="Kind">An agent, a role or a group.</param>
/// <param name="ClaimType">A group's claim type; empty for an agent or a role.</param>
/// <param name="Value">An agent's IRI, or the role's or group's claim value.</param>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public readonly record struct Principal(PrincipalKind Kind, string ClaimType, string Value)
{
    /// <summary>One agent, by its IRI: the token's issuer, a hash and its subject.</summary>
    public static Principal Agent(string iri) => new(PrincipalKind.Agent, string.Empty, iri);

    /// <summary>Every caller whose role claim holds the value.</summary>
    public static Principal Role(string value) => new(PrincipalKind.Role, string.Empty, value);

    /// <summary>Every caller whose claim of the type holds the value.</summary>
    public static Principal Group(string claimType, string value) => new(PrincipalKind.Group, claimType, value);
}
