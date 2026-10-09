// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The live tails each client holds open (ADR 0114): the client is the
/// token's subject when there is one, the remote address otherwise. A slot
/// is taken before a tail streams and given back when it ends.
/// </summary>
internal sealed class LiveTails
{
    private readonly Dictionary<string, int> _open = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Takes a slot for the caller, or answers null when the caller holds <paramref name="limit"/> already.</summary>
    internal IDisposable? TryOpen(HttpContext context, ProtocolOptions options, int limit)
    {
        string client = ClientOf(context, options);

        lock (_gate)
        {
            int held = _open.GetValueOrDefault(client);

            if (held >= limit)
            {
                return null;
            }

            _open[client] = held + 1;
        }

        return new Slot(this, client);
    }

    private void Close(string client)
    {
        lock (_gate)
        {
            int held = _open.GetValueOrDefault(client) - 1;

            if (held <= 0)
            {
                _open.Remove(client);
            }
            else
            {
                _open[client] = held;
            }
        }
    }

    private static string ClientOf(HttpContext context, ProtocolOptions options)
    {
        RequestTerm agent = options.Identity.AgentOf(context.User);

        if (!agent.IsNone && agent.Term is { } term)
        {
            return "agent:" + Encoding.UTF8.GetString(term.Lexical);
        }

        return "address:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private sealed class Slot(LiveTails tails, string client) : IDisposable
    {
        private LiveTails? _tails = tails;

        public void Dispose()
        {
            _tails?.Close(client);
            _tails = null;
        }
    }
}
