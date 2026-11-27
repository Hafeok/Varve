// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.WebUi.Components;

/// <summary>Who a token names: the server's agent IRI for it, and its roles.</summary>
/// <param name="Agent">The agent IRI, as the server mints it from the token's issuer and subject.</param>
/// <param name="Roles">The roles the token's role claim names.</param>
public sealed record Caller(string Agent, IReadOnlyList<string> Roles);

/// <summary>
/// Who each plane's token names, as the layout passes it down to the parts
/// that say it (a 403's "Signed in as"): values only, so a part reads who is
/// signed in without reaching the sign-in session, which holds the client's
/// token providers and stays in the host (ADR 0206 point 4, amended
/// 2026-10-30; ADR 0252).
/// </summary>
/// <param name="Data">Who the data plane's token names.</param>
/// <param name="Management">Who the management plane's token names.</param>
[Contract(typeof(TheWebUisPrivateAssemblies.ThreeAssembliesThreeLayers), Role = "what the host's sign-in passes down to the parts both page sets share")]
public sealed record SignedIn(Caller? Data, Caller? Management);
