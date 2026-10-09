// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Server.Commands;

/// <summary>
/// The <c>varve</c> command line (ADR 0105): one executable with the server.
/// A dataset is a directory, opened directly with no authentication, or a
/// URL, reached through <c>Varve.Protocol.Client</c> with a bearer token.
/// </summary>
internal static class Cli
{
    /// <summary>Runs the command line; exit code 0 on success, 1 on a failure the command reports, 2 on a usage error.</summary>
    internal static async Task<int> RunAsync(string[] args, Io io, CancellationToken cancellationToken)
    {
        RootCommand root = new("Varve: an event-sourced RDF store. A dataset is a directory on this machine or the URL of one on a server.");
        Target target = new();
        root.Subcommands.Add(Create.Command(io));
        root.Subcommands.Add(Info.Command(target, io));
        root.Subcommands.Add(Load.Command(io));
        root.Subcommands.Add(Query.Command(target, io));
        root.Subcommands.Add(Update.Command(target, io));
        root.Subcommands.Add(Export.Command(target, io));
        root.Subcommands.Add(Checkpoint.Command(target, io));
        root.Subcommands.Add(Feed.Command(target, io));
        root.Subcommands.Add(Serve.Command());
        ParseResult parsed = root.Parse(args);
        InvocationConfiguration configuration = new() { Output = io.Out, Error = io.Error };
        int exit = await parsed.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        return parsed.Errors.Count > 0 ? UsageError : exit;
    }

    /// <summary>The exit code of a command line that did not parse; 1 is a command that ran and failed.</summary>
    internal const int UsageError = 2;

    /// <summary>A command's failure, reported on standard error with exit code 1.</summary>
    internal sealed class CommandException(string message) : Exception(message);

    internal static async Task<int> GuardAsync(Func<Task<int>> run, TextWriter error)
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (CommandException failure)
        {
            await error.WriteLineAsync("varve: " + failure.Message).ConfigureAwait(false);
            return 1;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or Varve.Store.DatasetLeasedException)
        {
            await error.WriteLineAsync("varve: " + failure.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
