// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Server.Commands;

namespace Varve.Server;

/// <summary>
/// The entry point of the one executable (ADR 0105): <c>varve serve</c> and
/// a bare <c>varve</c> run the server of ADR 0101; every other command is the
/// CLI's.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the server or a command. The server is a bare <c>varve</c>, or a
    /// line of nothing but host configuration (<c>--Varve:…</c>, <c>--urls=…</c>),
    /// as the host took before the CLI existed, straight to the host;
    /// <c>varve serve</c> goes through the command line, whose options map
    /// onto the same configuration (ADR 0115). Exit code 2 is a
    /// configuration that does not validate, or a usage error.
    /// </summary>
    internal static async Task<int> Main(string[] args)
    {
        if (Serves(args))
        {
            return await ServerHost.RunAsync(args, ready: null).ConfigureAwait(false);
        }

        using CancellationTokenSource stopping = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };
        return await Cli.RunAsync(args, new Io(Console.Out, Console.Error, Console.OpenStandardOutput()), stopping.Token).ConfigureAwait(false);
    }

    private static bool Serves(string[] args) =>
        args.Length == 0
        || (args[0].StartsWith("--", StringComparison.Ordinal) && args[0] is not ("--help" or "--version"));
}
