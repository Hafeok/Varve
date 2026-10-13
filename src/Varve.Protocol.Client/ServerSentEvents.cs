// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Protocol.Client;

/// <summary>One server-sent event: its name, its id and its data lines joined by line feeds.</summary>
internal readonly record struct ServerSentEvent(string Name, string? Id, string Data);

/// <summary>
/// Reads <c>text/event-stream</c> as the WHATWG HTML standard's event-stream
/// interpretation does, for the fields the commits resource writes
/// (<c>change-feed.md</c> §3): <c>id</c>, <c>event</c> and <c>data</c>; a line
/// starting with <c>:</c> is a comment, the heartbeat; an empty line
/// dispatches. A stream that ends dispatches nothing more.
/// </summary>
internal sealed class ServerSentEvents(Stream body) : System.IDisposable
{
    private readonly StreamReader _reader = new(body, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);

    internal async Task<ServerSentEvent?> NextAsync(CancellationToken cancellationToken)
    {
        string name = "message";
        string? id = null;
        StringBuilder? data = null;

        while (await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (line.Length == 0)
            {
                if (data is null)
                {
                    name = "message";
                    continue;
                }

                return new ServerSentEvent(name, id, data.ToString());
            }

            if (line[0] == ':')
            {
                continue;
            }

            int colon = line.IndexOf(':', System.StringComparison.Ordinal);
            string field = colon < 0 ? line : line[..colon];
            string value = colon < 0 ? string.Empty : line[(colon + 1)..];

            if (value.Length > 0 && value[0] == ' ')
            {
                value = value[1..];
            }

            switch (field)
            {
                case "event":
                    name = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    data = data is null ? new StringBuilder(value) : data.Append('\n').Append(value);
                    break;
                default:
                    break;
            }
        }

        return null;
    }

    public void Dispose() => _reader.Dispose();
}
