// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>
/// The compatibility page as this build knows it (ADR 0183, ADR 0184
/// decision 9): each provider's state, from
/// <c>object-store-providers.txt</c>, which <c>eng/object-stores.cs</c>
/// writes with the page and <c>eng/status.cs</c> holds to it.
/// </summary>
[Contract(typeof(LogShipping.ATargetIsAVerifiedStoreOrADirectory), Role = "the provider states compiled into the build")]
internal static class CompatibilityPage
{
    /// <summary>The state of a provider the page lists as one the suite passed against.</summary>
    internal const string Verified = "verified";

    private static readonly FrozenDictionary<string, string> States = Load();

    /// <summary>The page's state for <paramref name="provider"/>, or <c>unknown</c> for an id it does not list.</summary>
    internal static string StateOf(string provider) => States.TryGetValue(provider, out string? state) ? state : "unknown";

    /// <summary>The first store target of <paramref name="proposed"/> whose provider is not verified, or null. A directory target is exempt.</summary>
    internal static UnverifiedTarget? Check(Declarations proposed)
    {
        foreach (DatasetDeclaration dataset in proposed.Datasets.Values)
        {
            foreach (ShippingTargetDeclaration target in dataset.ShippingTargets)
            {
                if (target.Provider is { } provider && StateOf(provider) != Verified)
                {
                    return new UnverifiedTarget(dataset.Name, provider, StateOf(provider));
                }
            }
        }

        return null;
    }

    private static FrozenDictionary<string, string> Load()
    {
        Dictionary<string, string> states = new(StringComparer.Ordinal);
        using Stream stream = typeof(CompatibilityPage).Assembly.GetManifestResourceStream("Varve.Server.object-store-providers.txt")
            ?? throw new InvalidOperationException("The build holds no object-store-providers.txt.");
        using StreamReader reader = new(stream);

        while (reader.ReadLine() is { } line)
        {
            int space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space > 0)
            {
                states[line[..space]] = line[(space + 1)..];
            }
        }

        return states.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
