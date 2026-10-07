// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;

namespace Varve.Server;

/// <summary>The Varve server's entry point.</summary>
internal static class Program
{
    /// <summary>Runs the server until it is stopped. Exit code 2 is a configuration that does not validate.</summary>
    internal static Task<int> Main(string[] args) => ServerHost.RunAsync(args, ready: null);
}
