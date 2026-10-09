// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using Varve.Sparql;
using Varve.Sparql.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary><c>varve update &lt;dataset&gt;</c>: one update request, one commit or none.</summary>
internal static class Update
{
    internal static Command Command(Target target, Io io)
    {
        Option<string?> text = new("--update", "-u") { Description = "The update text." };
        Option<string?> file = new("--file") { Description = "A file holding the update." };
        Option<long?> ifMatch = new("--if-match") { Description = "The position the request expects; a conflict otherwise." };
        Command command = new("update", "Execute a SPARQL Update request against a dataset as one commit.");
        target.AddTo(command);
        command.Options.Add(text);
        command.Options.Add(file);
        command.Options.Add(ifMatch);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            byte[] update = await Commands.TextAsync(parsed.GetValue(text), parsed.GetValue(file), "update", cancellationToken).ConfigureAwait(false);
            long? expected = parsed.GetValue(ifMatch);
            await using Opened opened = await target.OpenAsync(parsed, io, cancellationToken).ConfigureAwait(false);

            if (opened is Remote remote)
            {
                using HttpResponseMessage response = await remote.Client.UpdateAsync(System.Text.Encoding.UTF8.GetString(update), expected, cancellationToken).ConfigureAwait(false);
                return await Commands.AnswerWriteAsync(response, io, cancellationToken).ConfigureAwait(false);
            }

            Local local = (Local)opened;
            Varve.Sparql.Algebra.Update parsedUpdate;

            try
            {
                parsedUpdate = SparqlParser.ParseUpdate(update);
            }
            catch (SparqlParseException error)
            {
                throw new Cli.CommandException("the update does not parse: " + error.Message);
            }

            CommitResult result;

            try
            {
                result = await SparqlUpdate.ExecuteAsync(local.Dataset, parsedUpdate, local.UpdateOptions(expected is long e ? new Position(e) : null), cancellationToken).ConfigureAwait(false);
            }
            catch (SparqlUpdateException failed)
            {
                throw new Cli.CommandException("an operation failed; nothing was committed: " + failed.Message);
            }

            await io.Out.WriteLineAsync(result.Outcome switch
            {
                CommitOutcome.Committed => "committed, position " + result.Position.Value.ToString(CultureInfo.InvariantCulture),
                CommitOutcome.NoChange => "no change, head " + result.Position.Value.ToString(CultureInfo.InvariantCulture),
                CommitOutcome.Conflict => "conflict: another commit came first; the head is " + result.Position.Value.ToString(CultureInfo.InvariantCulture),
                CommitOutcome.Rejected => "rejected: " + result.Reason,
                _ => "unavailable: " + result.Reason,
            }).ConfigureAwait(false);
            return result.Outcome is CommitOutcome.Committed or CommitOutcome.NoChange ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));
        return command;
    }
}
