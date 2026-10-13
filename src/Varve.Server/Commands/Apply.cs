// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using Varve.Protocol.Client;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve apply &lt;setup.yaml&gt; &lt;server&gt;</c> (ADRs 0130, 0131): makes a
/// dataset on a server what its setup file declares, through the same
/// <see cref="SparqlHttpClient.ApplyAsync"/> an application's <c>AddVarve</c>
/// runs at start. Exit 1 on a refusal, 2 on a file outside the subset.
/// </summary>
internal static class Apply
{
    internal static Command Command(Target target, Io io)
    {
        Argument<string> file = new("setup") { Description = "The setup file: the YAML subset docs/operator/datasets.md specifies." };
        Command command = new("apply", "Make a dataset on a server what a setup file declares: the dataset, its settings, its baseline and its migrations; applying it again commits nothing (ADR 0130).");
        command.Arguments.Add(file);
        target.AddTo(command);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            DatasetSetup setup;

            try
            {
                setup = DatasetSetupFile.Read(parsed.GetValue(file)!);
            }
            catch (SetupFileException outside)
            {
                await io.Error.WriteLineAsync("varve: " + outside.Message).ConfigureAwait(false);
                return Cli.UsageError;
            }

            string server = parsed.GetValue(target.Dataset)!;

            if (!Target.IsUrl(server))
            {
                throw new Cli.CommandException("apply takes the URL of a server, or of the dataset on it (…/datasets/" + setup.Name + "/); a setup is applied over HTTP.");
            }

            await using Remote remote = await target.OpenRemoteAsync(parsed, io, DatasetAddress(server, setup.Name), cancellationToken).ConfigureAwait(false);
            SetupResult result;

            try
            {
                result = await remote.Client.ApplyAsync(setup, cancellationToken).ConfigureAwait(false);
            }
            catch (SetupException refused)
            {
                throw new Cli.CommandException(refused.Message);
            }

            await io.Out.WriteLineAsync(Describe(setup, result)).ConfigureAwait(false);
            return 0;
        }, parsed.InvocationConfiguration.Error));
        return command;
    }

    // A server's address, or the dataset's on it: …/datasets/{name}/ either way.
    private static Uri DatasetAddress(string url, string name)
    {
        string address = url.EndsWith('/') ? url : url + "/";
        string suffix = "datasets/" + Uri.EscapeDataString(name) + "/";
        return new Uri(address.EndsWith(suffix, StringComparison.Ordinal) ? address : address + suffix, UriKind.Absolute);
    }

    private static string Describe(DatasetSetup setup, SetupResult result)
    {
        if (!result.Created && !result.Committed)
        {
            return setup.Name + ": as declared already, at position " + result.Position + "; nothing committed";
        }

        System.Text.StringBuilder text = new();
        text.Append(setup.Name).Append(": ");

        if (result.Created)
        {
            text.Append("created; ");
        }

        if (result.SettingsChanged)
        {
            text.Append("settings changed; ");
        }

        foreach (string baseline in result.BaselinesCommitted)
        {
            text.Append("baseline ").Append(baseline).Append(" committed; ");
        }

        foreach (string migration in result.MigrationsApplied)
        {
            text.Append("migration ").Append(migration).Append(" applied; ");
        }

        return text.Append("at position ").Append(result.Position).ToString();
    }
}
