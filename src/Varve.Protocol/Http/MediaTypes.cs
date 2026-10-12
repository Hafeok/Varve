// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using Microsoft.Net.Http.Headers;
using Varve.Sparql.Results;
using Varve.Turtle;

namespace Varve.Protocol.Http;

/// <summary>
/// The media types the endpoints read and write (ADR 0092), in the order the
/// server prefers them when <c>Accept</c> leaves the choice open.
/// </summary>
internal static class MediaTypes
{
    internal const string SparqlQuery = "application/sparql-query";
    internal const string SparqlUpdate = "application/sparql-update";
    internal const string Form = "application/x-www-form-urlencoded";
    internal const string Multipart = "multipart/form-data";
    internal const string NTriples = "application/n-triples";
    internal const string NQuads = "application/n-quads";
    internal const string Turtle = "text/turtle";
    internal const string TriG = "application/trig";
    internal const string Delta = "application/vnd.varve.delta";
    internal const string EventStream = "text/event-stream";
    internal const string Json = "application/json";
    internal const string MergePatch = "application/merge-patch+json";

    /// <summary>Solution and boolean results: JSON first (Oxigraph's default).</summary>
    internal static readonly ImmutableArray<Offer<SparqlResultsFormat>> Results =
    [
        new("application/sparql-results+json", SparqlResultsFormat.Json),
        new("application/sparql-results+xml", SparqlResultsFormat.Xml),
        new("text/csv", SparqlResultsFormat.Csv),
        new("text/tab-separated-values", SparqlResultsFormat.Tsv),
        new(Json, SparqlResultsFormat.Json),
        new("application/xml", SparqlResultsFormat.Xml),
    ];

    /// <summary>Graphs: N-Triples first, the one a streaming writer honours without buffering prefixes.</summary>
    internal static readonly ImmutableArray<Offer<RdfSyntax>> Graphs =
    [
        new(NTriples, RdfSyntax.NTriples),
        new(Turtle, RdfSyntax.Turtle),
        new(NQuads, RdfSyntax.NQuads),
        new(TriG, RdfSyntax.TriG),
    ];

    /// <summary>The change feed and the diff.</summary>
    internal static readonly ImmutableArray<Offer<FeedFraming>> Feeds =
    [
        new(Delta, FeedFraming.Delta),
        new(EventStream, FeedFraming.EventStream),
    ];

    /// <summary>The syntax a request body is in, by its media type.</summary>
    internal static bool TryReadSyntax(string mediaType, out RdfSyntax syntax)
    {
        switch (mediaType.ToLowerInvariant())
        {
            case NTriples:
            case "text/plain":
                syntax = RdfSyntax.NTriples;
                return true;
            case NQuads:
                syntax = RdfSyntax.NQuads;
                return true;
            case Turtle:
            case "application/x-turtle":
                syntax = RdfSyntax.Turtle;
                return true;
            case TriG:
                syntax = RdfSyntax.TriG;
                return true;
            default:
                syntax = default;
                return false;
        }
    }

    /// <summary>The media type a response in <paramref name="syntax"/> carries.</summary>
    internal static string Of(RdfSyntax syntax) => syntax switch
    {
        RdfSyntax.NTriples => NTriples,
        RdfSyntax.NQuads => NQuads,
        RdfSyntax.Turtle => Turtle,
        _ => TriG,
    };

    /// <summary>
    /// Reads a <c>Content-Type</c>: its media type, and whether its charset,
    /// if it names one, is UTF-8. Every body the endpoints read is UTF-8
    /// (ADR 0092: another charset is <c>415</c>).
    /// </summary>
    internal static bool TryReadContentType(string? header, out string mediaType, out bool utf8)
    {
        mediaType = string.Empty;
        utf8 = true;

        if (string.IsNullOrEmpty(header) || !MediaTypeHeaderValue.TryParse(header, out MediaTypeHeaderValue? parsed) || parsed.MediaType.Value is null)
        {
            return false;
        }

        mediaType = parsed.MediaType.Value.ToLowerInvariant();
        string? charset = parsed.Charset.Value;
        utf8 = charset is null || charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || charset.Equals("utf8", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>The <c>version</c> media-type parameter of a SPARQL body, if any (SPARQL 1.2 Protocol).</summary>
    internal static string? VersionParameter(string? header)
    {
        if (string.IsNullOrEmpty(header) || !MediaTypeHeaderValue.TryParse(header, out MediaTypeHeaderValue? parsed))
        {
            return null;
        }

        foreach (NameValueHeaderValue parameter in parsed.Parameters)
        {
            if (parameter.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                return HeaderUtilities.RemoveQuotes(parameter.Value).Value;
            }
        }

        return null;
    }
}

/// <summary>How a feed or a diff is framed on the wire (<c>change-feed.md</c> §2, §3).</summary>
internal enum FeedFraming : byte
{
    Delta,
    EventStream,
}
