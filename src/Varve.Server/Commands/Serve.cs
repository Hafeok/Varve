// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Threading.Tasks;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve serve</c>: the server of ADR 0101, its common settings as
/// options mapped onto configuration keys (ADR 0115), <c>--set Key=Value</c>
/// for any key under <c>Varve:</c>, <c>--config</c> for the files, and
/// <c>--print-config</c> for the effective configuration. The generic
/// <c>--Varve:Key=Value</c> form still passes through to the host.
/// </summary>
internal static class Serve
{
    internal static Command Command()
    {
        Option<string?> datasetsRoot = new("--datasets-root") { Description = "The directory file datasets live under (Varve:DatasetsRoot)." };
        Option<string[]> dataset = new("--dataset") { Description = "A dataset to serve, as name or name=File|Memory (Varve:Datasets:{name}:Storage). Repeatable." };
        Option<string?> authMode = new("--auth-mode") { Description = "Oidc or Anonymous (Varve:Auth:Mode)." };
        Option<bool> anonymous = new("--anonymous") { Description = "Shorthand for --auth-mode Anonymous: development only (ADR 0037)." };
        Option<string?> authority = new("--authority") { Description = "The OIDC issuer (Varve:Auth:Authority)." };
        Option<string[]> audience = new("--audience") { Description = "An audience a token may carry (Varve:Auth:Audiences). Repeatable." };
        Option<string?> urls = new("--urls") { Description = "What to listen on, as ASP.NET Core takes it; http://localhost:5000 by default." };
        Option<string[]> config = new("--config") { Description = "A configuration file, read below the environment and the command line. Repeatable, in order." };
        Option<string[]> set = new("--set") { Description = "Any setting as Key=Value under Varve:, as Limits:QueryTimeout=00:01:00. Repeatable." };
        Option<bool> print = new("--print-config") { Description = "Print the effective configuration with secrets redacted, validate it, and exit." };
        Command command = new("serve", "Run the server (ADRs 0101, 0115). Configuration comes from the files, then the environment, then the command line, later winning; an unknown key refuses to start.")
        {
            // The generic --Varve:Key=Value form, and the host's own flags.
            TreatUnmatchedTokensAsErrors = false,
        };
        command.Options.Add(datasetsRoot);
        command.Options.Add(dataset);
        command.Options.Add(authMode);
        command.Options.Add(anonymous);
        command.Options.Add(authority);
        command.Options.Add(audience);
        command.Options.Add(urls);
        command.Options.Add(config);
        command.Options.Add(set);
        command.Options.Add(print);
        command.SetAction((parsed, cancellationToken) =>
        {
            List<string> arguments = [.. parsed.UnmatchedTokens];

            void Add(string key, string? value)
            {
                if (value is not null)
                {
                    arguments.Add("--Varve:" + key + "=" + value);
                }
            }

            Add("DatasetsRoot", parsed.GetValue(datasetsRoot));
            Add("Auth:Mode", parsed.GetValue(anonymous) ? "Anonymous" : parsed.GetValue(authMode));
            Add("Auth:Authority", parsed.GetValue(authority));
            string[] audiences = parsed.GetValue(audience) ?? [];

            for (int i = 0; i < audiences.Length; i++)
            {
                Add("Auth:Audiences:" + i, audiences[i]);
            }

            foreach (string entry in parsed.GetValue(dataset) ?? [])
            {
                int at = entry.IndexOf('=', StringComparison.Ordinal);
                Add("Datasets:" + (at < 0 ? entry : entry[..at]) + ":Storage", at < 0 ? "File" : entry[(at + 1)..]);
            }

            foreach (string entry in parsed.GetValue(set) ?? [])
            {
                int at = entry.IndexOf('=', StringComparison.Ordinal);

                if (at <= 0)
                {
                    parsed.InvocationConfiguration.Error.WriteLine("varve: --set takes Key=Value, as Limits:QueryTimeout=00:01:00.");
                    return Task.FromResult(Cli.UsageError);
                }

                Add(entry[..at], entry[(at + 1)..]);
            }

            if (parsed.GetValue(urls) is { } listen)
            {
                arguments.Add("--urls=" + listen);
            }

            return ServerHost.RunAsync(new ServeOptions
            {
                Arguments = [.. arguments],
                ConfigurationFiles = parsed.GetValue(config) ?? [],
                PrintConfiguration = parsed.GetValue(print),
            }, ready: null);
        });
        return command;
    }
}
