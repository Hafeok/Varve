// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve serve</c>: the server of ADR 0101, unchanged. The program hands
/// its arguments to the host before the command line is parsed, so this
/// command exists for the help text; it is never invoked.
/// </summary>
internal static class Serve
{
    internal static Command Command()
    {
        Command command = new("serve", "Run the server. Every argument after `serve` is the host's configuration, as --Varve:Datasets:people:Storage=File; a bare `varve` serves too.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        command.SetAction(_ => 0);
        return command;
    }
}
