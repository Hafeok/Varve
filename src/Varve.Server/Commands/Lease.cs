// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve lease &lt;dir&gt;</c> (ADR 0116): who holds a dataset's lease and
/// whether the lock is held; <c>--break</c> removes the owner file of a lock
/// that proves free, and refuses one that is held, naming the holder.
/// Nothing here ever breaks a lock the operating system still holds.
/// </summary>
internal static class Lease
{
    internal static Command Command(Io io)
    {
        Argument<string> directory = new("dataset") { Description = "A dataset directory." };
        Option<bool> @break = new("--break") { Description = "Remove the owner file when the lock proves free; refuse when it is held." };
        Command command = new("lease", "Inspect a dataset's lease, and clear a stale owner file (ADR 0116).");
        command.Arguments.Add(directory);
        command.Options.Add(@break);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(() => RunAsync(parsed.GetValue(directory)!, parsed.GetValue(@break), io, cancellationToken), parsed.InvocationConfiguration.Error));
        return command;
    }

    private static async Task<int> RunAsync(string directory, bool breakIt, Io io, CancellationToken cancellationToken)
    {
        string derived = Path.Combine(Path.GetFullPath(directory), "derived");
        string ownerPath = Path.Combine(derived, "LOCK.owner");
        string lockPath = Path.Combine(derived, "LOCK");

        if (!Directory.Exists(Path.Combine(Path.GetFullPath(directory), "log")))
        {
            throw new Cli.CommandException("'" + directory + "' is not a dataset directory: it has no log/.");
        }

        string? owner = File.Exists(ownerPath) ? (await File.ReadAllTextAsync(ownerPath, cancellationToken).ConfigureAwait(false)).Trim() : null;
        await io.Out.WriteLineAsync("owner: " + (owner ?? "none recorded")).ConfigureAwait(false);

        // The probe takes the lock, which proves it free, and releases it at
        // once; a held lock refuses with the holder.
        try
        {
            FileStorage probe = await FileStorage.OpenAsync(new DatasetDirectory(Path.GetFullPath(directory)), new FileStorageOptions { Clock = TimeProvider.System }, cancellationToken).ConfigureAwait(false);
            await probe.DisposeAsync().ConfigureAwait(false);
        }
        catch (DatasetLeasedException)
        {
            await io.Out.WriteLineAsync("lock: held").ConfigureAwait(false);

            if (owner is not null && OwnerIsAnEndedProcessHere(owner))
            {
                await io.Out.WriteLineAsync("note: the owner is a process of this machine that is no longer running, yet the lock is held: the filesystem may not support locks; a dataset belongs on one that does.").ConfigureAwait(false);
            }

            if (breakIt)
            {
                throw new Cli.CommandException("the lock is held" + (owner is null ? string.Empty : " by " + owner) + "; a held lock is never broken (ADR 0116).");
            }

            return 1;
        }

        await io.Out.WriteLineAsync("lock: free").ConfigureAwait(false);

        if (breakIt)
        {
            // The probe wrote its own owner line; the stale file and the lock
            // file go, and the next opener writes its own.
            File.Delete(ownerPath);
            File.Delete(lockPath);
            await io.Out.WriteLineAsync("cleared the owner file" + (owner is null ? string.Empty : " of " + owner)).ConfigureAwait(false);
        }

        return 0;
    }

    // "process <pid> on <machine>, since <when>" (ADR 0075): a pid of this
    // machine that is not running tells a lock that should have been released.
    private static bool OwnerIsAnEndedProcessHere(string owner)
    {
        const string Prefix = "process ";
        int on = owner.IndexOf(" on ", StringComparison.Ordinal);

        if (!owner.StartsWith(Prefix, StringComparison.Ordinal) || on < 0 || !int.TryParse(owner.AsSpan(Prefix.Length, on - Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return false;
        }

        int comma = owner.IndexOf(',', on);
        string machine = owner[(on + 4)..(comma < 0 ? owner.Length : comma)];

        if (!string.Equals(machine, Environment.MachineName, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
