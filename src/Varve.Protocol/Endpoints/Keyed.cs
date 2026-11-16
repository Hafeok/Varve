// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// A write's client idempotency key (ADR 0246): read from
/// <c>Idempotency-Key</c>, scoped to the caller's agent, looked up before
/// any precondition, and either answered from the commit that holds it or
/// claimed for this request until it is answered.
/// </summary>
internal static class Keyed
{
    /// <summary>The request header, as draft-ietf-httpapi-idempotency-key-header names it.</summary>
    internal const string Header = "Idempotency-Key";

    /// <summary>The response header that marks an answer replayed from the commit that holds the key.</summary>
    internal const string ReplayedHeader = "Varve-Replayed";

    /// <summary>The longest key accepted, in characters.</summary>
    internal const int MaxLength = 255;

    /// <summary>
    /// Begins a keyed write on <paramref name="exchange"/>'s dataset, at its
    /// writer. True when the write proceeds — with no key, or holding its
    /// key's claim; false when it was answered here: a malformed key, the
    /// original's outcome replayed, a key reused with another request, or a
    /// key a request in flight holds.
    /// </summary>
    internal static async Task<bool> BeginAsync(Exchange exchange)
    {
        HttpContext context = exchange.Context;

        if (exchange.Claim is not null)
        {
            return true;
        }

        if (!TryRead(context.Request.Headers[Header], out string? key))
        {
            await HttpProblems.BadRequest(context, "Idempotency-Key is one quoted string of 1 to 255 printable ASCII characters (draft-ietf-httpapi-idempotency-key-header).").ConfigureAwait(false);
            return false;
        }

        if (key is null || exchange.Options.Idempotency is not { } keys)
        {
            return true;
        }

        KeyIndex index = keys.IndexOf(exchange.Dataset);
        await index.CatchUpAsync(exchange.Dataset, context.RequestAborted).ConfigureAwait(false);

        // The body is read whole once and kept for the endpoint (ADR 0095's
        // limit still applies: one over it is refused by the endpoint, and
        // nothing is claimed for it).
        Body body = await RequestBodies.ReadAsync(context, exchange.Options.Limits).ConfigureAwait(false);

        if (body.TooLarge)
        {
            return true;
        }

        RequestTerm agent = exchange.Options.Identity.AgentOf(context.User);
        AgentKey agentKey = new(agent.Term is { } who ? Encoding.UTF8.GetString(who.Lexical) : string.Empty, key);
        string fingerprint = Fingerprint(context.Request, body.Bytes ?? []);
        KeyLookup lookup = index.Lookup(agentKey, fingerprint);

        if (lookup.InFlight)
        {
            context.Response.Headers.RetryAfter = "1";
            await HttpProblems.WriteAsync(context, ProblemType.IdempotencyKeyInFlight, "A request with this key is being answered; send it again once it has been.").ConfigureAwait(false);
            return false;
        }

        if (lookup.Committed is { } committed)
        {
            if (!string.Equals(committed.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                await HttpProblems.WriteAsync(context, ProblemType.IdempotencyKeyReused, "The key was sent with another request, committed at position " + committed.Position.ToString(CultureInfo.InvariantCulture) + "; nothing was committed.",
                    members => members.Number("position", committed.Position)).ConfigureAwait(false);
                return false;
            }

            Replay(context, committed);
            return false;
        }

        KeyClaim claim = lookup.Claim!;
        context.Response.RegisterForDispose(claim);
        exchange.Claim = claim;
        return true;
    }

    /// <summary>
    /// What the keyed write will answer if it commits, recorded with the key
    /// so a retry is answered the same: the status and the <c>Location</c>.
    /// </summary>
    internal static void Answering(Exchange exchange, KeyedAnswer answer)
    {
        if (exchange.Claim is not { } claim)
        {
            return;
        }

        claim.Answer = answer;

        if (exchange.KeyAttachmentAt is int at && exchange.Attachments is { } attachments)
        {
            attachments[at] = RequestTerm.FromTerm(KeyAttachment.Of(claim));
        }
    }

    /// <summary>Settles the key with the write's outcome: kept with the commit, or released.</summary>
    internal static void Settle(Exchange exchange, CommitResult result)
    {
        if (exchange.Claim is not { } claim)
        {
            return;
        }

        if (result.Outcome == CommitOutcome.Committed)
        {
            claim.Committed(new KeyedCommit(result.Position.Value, claim.Fingerprint, claim.Answer, exchange.Context.TraceIdentifier));
        }
        else
        {
            claim.Dispose();
        }
    }

    // The original's outcome, without committing again (ADR 0246): its
    // status, Location, position and request id.
    private static void Replay(HttpContext context, KeyedCommit committed)
    {
        HttpResponse response = context.Response;
        Preconditions.Describe(response, new Position(committed.Position));
        response.StatusCode = committed.Answer.Status;
        response.Headers[ReplayedHeader] = "true";

        if (committed.RequestId is { } id)
        {
            response.Headers[Preconditions.RequestIdHeader] = id;
        }

        if (committed.Answer.Location is { } location)
        {
            response.Headers.Location = location;
        }
    }

    /// <summary>
    /// SHA-256, in lowercase hex, over the method, the path and query, the
    /// <c>Content-Type</c> and the body: what a forward leaves unchanged.
    /// </summary>
    internal static string Fingerprint(HttpRequest request, ReadOnlySpan<byte> body)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(request.Method + "\n" + request.Path.ToUriComponent() + request.QueryString.ToUriComponent() + "\n" + (request.ContentType ?? string.Empty) + "\n"));
        hash.AppendData(body);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Reads <c>Idempotency-Key</c>: absent is a null key; present, it is one
    /// Structured Field string (RFC 9651 §3.3.3) of 1 to 255 characters.
    /// </summary>
    internal static bool TryRead(StringValues values, out string? key)
    {
        key = null;

        if (StringValues.IsNullOrEmpty(values))
        {
            return true;
        }

        if (values.Count != 1 || values[0] is not { } raw)
        {
            return false;
        }

        ReadOnlySpan<char> text = raw.AsSpan().Trim(' ');

        if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
        {
            return false;
        }

        StringBuilder unquoted = new(text.Length);

        for (int i = 1; i < text.Length - 1; i++)
        {
            char c = text[i];

            if (c == '\\')
            {
                if (++i >= text.Length - 1 || text[i] is not ('"' or '\\'))
                {
                    return false;
                }

                c = text[i];
            }
            else if (c is < ' ' or > '~' or '"')
            {
                return false;
            }

            unquoted.Append(c);
        }

        if (unquoted.Length is 0 or > MaxLength)
        {
            return false;
        }

        key = unquoted.ToString();
        return true;
    }
}
