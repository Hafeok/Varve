// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Iri;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Protocol.Client;

/// <summary>
/// Fetches an RDF document by IRI (ADR 0103): <c>GET</c> with content
/// negotiation over N-Triples, N-Quads, Turtle and TriG, the syntax from the
/// response's media type or the path's extension, the base the request's IRI
/// without its fragment, the bytes capped, the endpoint policy consulted
/// first. A host binds the update executor's <c>ILoadSource</c> to it.
/// </summary>
public sealed class RdfDocumentClient
{
    private const string Accept = "text/turtle, application/n-triples, application/n-quads, application/trig, text/plain;q=0.5";

    private readonly HttpClient _http;
    private readonly EndpointPolicy _policy;
    private readonly ClientLimits _limits;

    /// <summary>A client over <paramref name="http"/>, reaching what <paramref name="policy"/> allows, within <paramref name="limits"/>.</summary>
    public RdfDocumentClient(HttpClient http, EndpointPolicy policy, ClientLimits limits)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(policy);
        _http = http;
        _policy = policy;
        _limits = limits;
    }

    /// <summary>The document <paramref name="iri"/> names, or why there is none.</summary>
    public async ValueTask<RdfDocument> FetchAsync(RdfTerm iri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(iri);
        string address = Encoding.UTF8.GetString(iri.Lexical);

        if (iri.Kind != RdfTermKind.Iri)
        {
            return RdfDocument.Failed("not an IRI: " + address);
        }

        if (!_policy.Allows(iri.Lexical, out string? refused))
        {
            return RdfDocument.Failed("the endpoint policy refuses <" + address + ">: " + refused);
        }

        using HttpRequestMessage message = new(HttpMethod.Get, new Uri(address, UriKind.Absolute));
        message.Headers.TryAddWithoutValidation("Accept", Accept);
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_limits.Timeout);
        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            return RdfDocument.Failed("<" + address + "> could not be reached: " + error.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RdfDocument.Failed("<" + address + "> did not answer within " + _limits.Timeout.ToString() + ".");
        }

        using (response)
        {
            int status = (int)response.StatusCode;

            if (status is >= 300 and < 400)
            {
                return RdfDocument.Failed("<" + address + "> answered a redirect (" + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + "), which is not followed.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return RdfDocument.Failed("<" + address + "> answered " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + response.ReasonPhrase + ".");
            }

            if (!TrySyntax(response.Content.Headers.ContentType, iri.Lexical, out RdfSyntax syntax))
            {
                return RdfDocument.Failed("<" + address + "> answered " + (response.Content.Headers.ContentType?.MediaType ?? "no media type") + ", not an RDF syntax Varve reads.");
            }

            (byte[]? body, string? failure) = await ReadAsync(response.Content, address, bounded.Token, cancellationToken).ConfigureAwait(false);
            return body is null ? RdfDocument.Failed("<" + address + ">: " + failure) : RdfDocument.Of(body, syntax, Base(iri.Lexical));
        }
    }

    private async ValueTask<(byte[]? Body, string? Failure)> ReadAsync(HttpContent content, string address, CancellationToken bounded, CancellationToken cancellationToken)
    {
        try
        {
            return await ResponseBodies.ReadAsync(content, _limits.MaxResponseBytes, bounded).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "<" + address + "> did not answer within " + _limits.Timeout.ToString() + ".");
        }
        catch (HttpRequestException error)
        {
            return (null, "the document was cut: " + error.Message);
        }
    }

    /// <summary>The media type decides; a generic or absent one falls back to the path's extension.</summary>
    internal static bool TrySyntax(MediaTypeHeaderValue? contentType, ReadOnlySpan<byte> iri, out RdfSyntax syntax)
    {
        switch (contentType?.MediaType?.ToLowerInvariant())
        {
            case "text/turtle":
            case "application/x-turtle":
                syntax = RdfSyntax.Turtle;
                return true;
            case "application/n-triples":
                syntax = RdfSyntax.NTriples;
                return true;
            case "application/n-quads":
                syntax = RdfSyntax.NQuads;
                return true;
            case "application/trig":
                syntax = RdfSyntax.TriG;
                return true;
            case null:
            case "text/plain":
            case "application/octet-stream":
                return TryExtension(iri, out syntax);
            default:
                syntax = default;
                return false;
        }
    }

    private static bool TryExtension(ReadOnlySpan<byte> iri, out RdfSyntax syntax)
    {
        ReadOnlySpan<byte> path = IriRef.TryValidate(iri, out IriComponents parts, out _) ? iri[parts.Path] : iri;

        if (path.EndsWith(".nt"u8))
        {
            syntax = RdfSyntax.NTriples;
        }
        else if (path.EndsWith(".nq"u8))
        {
            syntax = RdfSyntax.NQuads;
        }
        else if (path.EndsWith(".ttl"u8))
        {
            syntax = RdfSyntax.Turtle;
        }
        else if (path.EndsWith(".trig"u8))
        {
            syntax = RdfSyntax.TriG;
        }
        else
        {
            syntax = default;
            return false;
        }

        return true;
    }

    // RFC 3986 §5.1.3: the retrieval IRI, less its fragment.
    private static byte[] Base(ReadOnlySpan<byte> iri)
    {
        int hash = iri.IndexOf((byte)'#');
        return (hash >= 0 ? iri[..hash] : iri).ToArray();
    }
}
