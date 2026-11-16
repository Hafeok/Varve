// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.CompilerServices;

namespace Varve.Server.Tests;

/// <summary>
/// No host built in this assembly watches <c>appsettings.json</c> for
/// changes. A web application builder adds that file with reload on change,
/// and each watcher holds one inotify instance on Linux until the garbage
/// collector finalizes it, not until its server stops. This assembly starts
/// a server per test, and a runner allows a user 128 instances, so the suite
/// ran out of them once it had grown past that many starts between
/// collections. No test changes a settings file under a running server, and
/// the server never reads a reload: its own configuration files are added
/// with <c>ReloadOnChange = false</c>.
/// </summary>
internal static class NoConfigurationReload
{
    /// <summary>The host setting a builder reads before it adds its settings files.</summary>
    internal const string Variable = "DOTNET_hostBuilder__reloadConfigOnChange";

    [ModuleInitializer]
    internal static void Apply() => Environment.SetEnvironmentVariable(Variable, "false");
}
