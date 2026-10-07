// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Varve.Protocol.Http;

/// <summary>A request body read whole, or the reason it was not.</summary>
internal readonly record struct Body(byte[]? Bytes, bool TooLarge);

/// <summary>Request bodies, read whole under the host's limit (ADR 0095).</summary>
internal static class RequestBodies
{
    /// <summary>Reads the body, refusing one over <see cref="ProtocolLimits.MaxRequestBody"/>.</summary>
    internal static async Task<Body> ReadAsync(HttpContext context, ProtocolLimits limits)
    {
        long limit = limits.MaxRequestBody.Value;

        if (context.Request.ContentLength is long declared && declared > limit)
        {
            return new Body(null, TooLarge: true);
        }

        PipeReader reader = context.Request.BodyReader;

        while (true)
        {
            ReadResult read = await reader.ReadAsync(context.RequestAborted).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = read.Buffer;

            if (buffer.Length > limit)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                return new Body(null, TooLarge: true);
            }

            if (read.IsCompleted)
            {
                byte[] bytes = buffer.ToArray();
                reader.AdvanceTo(buffer.End);
                return new Body(bytes, TooLarge: false);
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    /// <summary>
    /// An <c>application/x-www-form-urlencoded</c> body as UTF-8, each name
    /// with every value it was given, so that a repeated <c>query</c> can be
    /// refused rather than one of them silently taken.
    /// </summary>
    internal static Dictionary<string, StringValues> ReadForm(byte[] body)
    {
        using FormReader reader = new(Encoding.UTF8.GetString(body))
        {
            ValueCountLimit = int.MaxValue,
            KeyLengthLimit = int.MaxValue,
            ValueLengthLimit = int.MaxValue,
        };

        return reader.ReadForm();
    }
}
