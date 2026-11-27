// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Client;

/// <summary>
/// A streamed body as batches of whole lines: every batch ends at a line
/// feed, so a parser of a line format (SPARQL 1.1 TSV, N-Triples) never sees
/// half a row. A partial last line waits for the next read; at the end of the
/// body whatever is left is the last batch. Held to the chunk-boundary oracle
/// (docs/testing.md §2).
/// </summary>
[Contract(typeof(TheDataExplorersReads.LineFormatsReadALineAtATime), Role = "a streamed body as whole lines")]
internal static class LineBatches
{
    /// <summary>The read size, and so the largest batch before a line too long for it grows the buffer.</summary>
    private const int ReadSize = 32 * 1024;

    internal static async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync(Stream body, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReadSize];
        int held = 0;

        while (true)
        {
            if (held == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            int read = await body.ReadAsync(buffer.AsMemory(held), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                if (held > 0)
                {
                    yield return buffer.AsMemory(0, held).ToArray();
                }

                yield break;
            }

            held += read;
            int end = buffer.AsSpan(0, held).LastIndexOf((byte)'\n') + 1;

            if (end > 0)
            {
                yield return buffer.AsMemory(0, end).ToArray();
                buffer.AsSpan(end, held - end).CopyTo(buffer);
                held -= end;
            }
        }
    }
}
