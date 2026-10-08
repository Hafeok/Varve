// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Protocol.Client;

/// <summary>
/// Reads a response body up to a cap (ADR 0103): the bytes, or null and the
/// reason when the body is longer than the cap. A body that is cut is never
/// handed on as whole.
/// </summary>
internal static class ResponseBodies
{
    internal static byte[]? Read(HttpContent content, ByteCount cap, CancellationToken cancellationToken, out string? failure)
    {
        if (content.Headers.ContentLength is long declared && declared > cap.Value)
        {
            failure = TooLong(cap);
            return null;
        }

        using Stream stream = content.ReadAsStream(cancellationToken);
        ArrayBufferWriter<byte> body = new(ContentLengthOr(content, 4096));

        while (true)
        {
            Span<byte> span = body.GetSpan(4096);
            int read = stream.Read(span);

            if (read == 0)
            {
                failure = null;
                return body.WrittenSpan.ToArray();
            }

            body.Advance(read);

            if (body.WrittenCount > cap.Value)
            {
                failure = TooLong(cap);
                return null;
            }
        }
    }

    internal static async ValueTask<(byte[]? Body, string? Failure)> ReadAsync(HttpContent content, ByteCount cap, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declared && declared > cap.Value)
        {
            return (null, TooLong(cap));
        }

        using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        ArrayBufferWriter<byte> body = new(ContentLengthOr(content, 4096));

        while (true)
        {
            Memory<byte> memory = body.GetMemory(4096);
            int read = await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return (body.WrittenSpan.ToArray(), null);
            }

            body.Advance(read);

            if (body.WrittenCount > cap.Value)
            {
                return (null, TooLong(cap));
            }
        }
    }

    private static int ContentLengthOr(HttpContent content, int fallback) =>
        content.Headers.ContentLength is long declared && declared > 0 && declared < int.MaxValue ? (int)declared : fallback;

    private static string TooLong(ByteCount cap) =>
        "the response is longer than the " + cap.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-byte cap";
}
