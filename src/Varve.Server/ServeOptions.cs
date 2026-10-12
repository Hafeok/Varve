// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Server;

/// <summary>
/// What <c>varve serve</c> hands the host beyond the configuration itself
/// (ADR 0115): the files <c>--config</c> named, in order, and whether to
/// print the effective configuration and exit instead of serving.
/// </summary>
internal sealed class ServeOptions
{
    /// <summary>The host's command-line arguments, as the command-line configuration provider reads them.</summary>
    public required string[] Arguments { get; init; }

    /// <summary>The configuration files, lowest precedence first; <c>appsettings.json</c> in the working directory when none.</summary>
    public IReadOnlyList<string> ConfigurationFiles { get; init; } = [];

    /// <summary>Print the effective <c>Varve:</c> configuration, validate, and exit without serving.</summary>
    public bool PrintConfiguration { get; init; }
}
