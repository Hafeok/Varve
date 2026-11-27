// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Protocol.Client.Model;

namespace Varve.WebUi.Explorer.Parts;

/// <summary>A format an answer can be downloaded in: its name, the media type asked for, and the file's extension.</summary>
/// <param name="Text">What the menu says.</param>
/// <param name="MediaType">The media type sent as <c>Accept</c>.</param>
/// <param name="Extension">The saved file's extension, without its dot.</param>
public sealed record DownloadFormat(string Text, string MediaType, string Extension)
{
    /// <summary>
    /// The formats the server writes an answer of <paramref name="form"/> in,
    /// in its own order of preference (Varve.Protocol's media type tables):
    /// the four results formats for solutions, XML and JSON for a boolean,
    /// the six RDF syntaxes for a graph.
    /// </summary>
    public static IReadOnlyList<DownloadFormat> For(QueryForm form) => form switch
    {
        QueryForm.Graph =>
        [
            new("N-Triples", "application/n-triples", "nt"),
            new("Turtle", "text/turtle", "ttl"),
            new("N-Quads", "application/n-quads", "nq"),
            new("TriG", "application/trig", "trig"),
            new("RDF/XML", "application/rdf+xml", "rdf"),
            new("JSON-LD", "application/ld+json", "jsonld"),
        ],
        QueryForm.Boolean =>
        [
            new("SPARQL XML", "application/sparql-results+xml", "srx"),
            new("SPARQL JSON", "application/sparql-results+json", "srj"),
        ],
        _ =>
        [
            new("SPARQL XML", "application/sparql-results+xml", "srx"),
            new("SPARQL JSON", "application/sparql-results+json", "srj"),
            new("CSV", "text/csv", "csv"),
            new("TSV", "text/tab-separated-values", "tsv"),
        ],
    };
}
