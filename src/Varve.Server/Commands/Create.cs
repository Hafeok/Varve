// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.IO;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary><c>varve create &lt;dir&gt;</c>: a new file dataset in a directory that holds none.</summary>
internal static class Create
{
    internal static Command Command(Io io)
    {
        Argument<string> directory = new("directory") { Description = "The directory to create the dataset in; made if missing, refused if it already holds one." };
        Command command = new("create", "Create a dataset in a directory.");
        command.Arguments.Add(directory);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            string path = Path.GetFullPath(parsed.GetValue(directory)!);

            if (Directory.Exists(Path.Combine(path, "log")))
            {
                throw new Cli.CommandException(path + " already holds a dataset.");
            }

            TimeProvider clock = TimeProvider.System;
            FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(path), new FileStorageOptions { Clock = clock }, cancellationToken).ConfigureAwait(false);

            try
            {
                DatasetId id = new(Guid.NewGuid());
                await using Dataset dataset = await Dataset.CreateAsync(files, id, new DatasetOptions { Clock = clock }, cancellationToken).ConfigureAwait(false);
                parsed.InvocationConfiguration.Output.WriteLine("created " + id.Value.ToString("D") + " at " + path);
                return 0;
            }
            finally
            {
                await files.DisposeAsync().ConfigureAwait(false);
            }
        }, parsed.InvocationConfiguration.Error));
        return command;
    }
}
