// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Management.Model;

/// <summary>
/// Who calls, as grants see it (ADR 0166): the agent IRI, the subject, and
/// the token's claims by type. A plain value: the host reads it out of the
/// token, so this package knows no claims principal.
/// </summary>
[DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed class Caller
{
    /// <summary>A caller.</summary>
    /// <param name="agent">The agent IRI, the token's issuer, a hash and its subject; none when anonymous.</param>
    /// <param name="subject">The subject claim's value, which a template's <c>{sub}</c> expands to.</param>
    /// <param name="claims">Every claim, by type, each with its values.</param>
    /// <param name="roleClaimType">The claim type a role's value is matched against.</param>
    public Caller(string? agent, string? subject, IReadOnlyDictionary<string, IReadOnlyList<string>> claims, string roleClaimType)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrEmpty(roleClaimType);
        Agent = agent;
        Subject = subject;
        Claims = claims;
        RoleClaimType = roleClaimType;
    }

    /// <summary>The agent IRI, or none.</summary>
    public string? Agent { get; }

    /// <summary>The subject claim's value, or none.</summary>
    public string? Subject { get; }

    /// <summary>Every claim, by type.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; }

    /// <summary>The claim type a role is matched against.</summary>
    public string RoleClaimType { get; }

    /// <summary>Whether a claim of the type holds the value.</summary>
    public bool Holds(string claimType, string value)
    {
        if (!Claims.TryGetValue(claimType, out IReadOnlyList<string>? values))
        {
            return false;
        }

        foreach (string held in values)
        {
            if (string.Equals(held, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A template variable's value, or none: <c>sub</c>, <c>agent</c>, or a single-valued <c>claim.&lt;name&gt;</c>.</summary>
    internal string? Variable(string name) => name switch
    {
        "sub" => Subject,
        "agent" => Agent,
        _ when name.StartsWith("claim.", StringComparison.Ordinal)
            && Claims.TryGetValue(name["claim.".Length..], out IReadOnlyList<string>? values)
            && values.Count == 1 => values[0],
        _ => null,
    };
}
