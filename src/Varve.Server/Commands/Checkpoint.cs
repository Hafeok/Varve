// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary><c>varve checkpoint &lt;dataset&gt;</c>: a checkpoint at the head, or at a position.</summary>
internal static class Checkpoint
{
    internal static Command Command(Target target, Io io)
    {
        Option<long?> at = new("--at") { Description = "The position to checkpoint; the head by default." };
        Command command = new("checkpoint", "Take a checkpoint of a dataset (ADR 0078).");
        target.AddTo(command);
        command.Options.Add(at);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            long? requested = parsed.GetValue(at);
            await using Opened opened = await target.OpenAsync(parsed, io, cancellationToken).ConfigureAwait(false);

            if (opened is Remote remote)
            {
                using HttpResponseMessage response = await remote.Client.CheckpointAsync(requested, cancellationToken).ConfigureAwait(false);
                return await Commands.AnswerWriteAsync(response, io, cancellationToken).ConfigureAwait(false);
            }

            Dataset dataset = ((Local)opened).Dataset;
            Position position = requested is long value ? new Position(value) : dataset.Head;

            if (position.Value == 0 || position > dataset.Head)
            {
                throw new Cli.CommandException("no commit at " + position.Value.ToString(CultureInfo.InvariantCulture) + "; the head is " + dataset.Head.Value.ToString(CultureInfo.InvariantCulture) + ".");
            }

            await dataset.CheckpointAsync(position, cancellationToken).ConfigureAwait(false);
            await io.Out.WriteLineAsync("checkpoint at " + position.Value.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            return 0;
        }, parsed.InvocationConfiguration.Error));
        return command;
    }
}
