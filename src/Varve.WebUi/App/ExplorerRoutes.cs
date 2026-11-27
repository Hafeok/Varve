// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.WebUi.App;

/// <summary>
/// The explorer's routes and the assemblies they load (ADR 0252): the router
/// fetches them when a route of the explorer is first visited, deep links
/// included, by file name, so nothing in the host names a type of the
/// explorer and nothing loads it with the first screen (VARVE0006). The
/// host's project file marks the same set lazy, and eng/browser-size.json
/// measures it as its own row (ADR 0205).
/// </summary>
[Contract(typeof(TheWebUisPrivateAssemblies.ExplorerLoadedByItsRoutes), Role = "the explorer's routes and its lazy assemblies")]
internal static partial class ExplorerRoutes
{
    /// <summary>The lazy set, as the boot manifest names it.</summary>
    internal static ImmutableArray<string> Assemblies { get; } = ["Varve.WebUi.Explorer.wasm", "Varve.Sparql.wasm", "Varve.Sparql.Results.wasm"];

    /// <summary>Whether a path, relative to the Web UI's base, is one of the explorer's: <c>datasets/{d}/</c> then one of its screens.</summary>
    internal static bool Matches(string path) => Route().IsMatch(path);

    [GeneratedRegex("^datasets/[^/]+/(query|diff|graphs|resource|validation|statement)(/|\\?|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Route();
}
