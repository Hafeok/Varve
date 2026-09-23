// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Threading;
using RepoStandard.Cli;
using RepoStandard.GitHub;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using SocketsHttpHandler gitHub = new() { AllowAutoRedirect = true };
using SocketsHttpHandler fetch = new() { AllowAutoRedirect = true };

Host host = new(Console.Out, Console.Error, Environment.GetEnvironmentVariable, gitHub, fetch, new RealDelay(), TimeProvider.System);
return await App.RunAsync(args, host, cancellation.Token).ConfigureAwait(false);
