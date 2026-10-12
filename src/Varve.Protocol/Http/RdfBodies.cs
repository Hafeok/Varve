// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Turtle;
using Varve.Turtle.Model;

namespace Varve.Protocol.Http;

/// <summary>A triple of a request body, as terms. Its blank nodes are the body's own labels.</summary>
internal readonly record struct BodyTriple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);

/// <summary>
/// A Graph Store body, parsed to triples (GSP §5). The body is a graph, so a
/// quad in a named graph — possible in N-Quads, TriG and JSON-LD — is refused
/// rather than moved into the target graph. RDF/XML reads the RDF 1.2 forms
/// where the document's <c>rdf:version</c> announces them (ADR 0122); JSON-LD
/// has no document loader here, so a remote context is a syntax error
/// (ADR 0123, RemoteContextsNeedALoader).
/// </summary>
internal static class RdfBodies
{
    internal static bool TryParse(ReadOnlyMemory<byte> body, GraphFormat format, string baseIri, List<BodyTriple> into, out string? error)
    {
        string? failure = null;
        bool named = false;

        void Add(in QuadView quad)
        {
            if (quad.HasGraph)
            {
                named = true;
                return;
            }

            into.Add(new BodyTriple(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()));
        }

        ErrorAction OnError(in ParseError parse)
        {
            failure ??= parse.Kind.ToString() + " at line " + parse.Position.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", column " + parse.Position.Column.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ErrorAction.Stop;
        }

        byte[] baseBytes = Encoding.UTF8.GetBytes(baseIri);
        bool succeeded;

        switch (format)
        {
            case GraphFormat.RdfXml:
                RdfXmlParseResult xml = RdfXmlParser.Parse(body, Add, new RdfXmlOptions { BaseIri = baseBytes });
                succeeded = xml.Succeeded;
                failure = xml.Error?.ToString();
                break;
            case GraphFormat.JsonLd:
                JsonLdResult json = JsonLdParser.Parse(body, Add, new JsonLdOptions { BaseIri = baseBytes });
                succeeded = json.Succeeded;
                failure = json.Error?.ToString();
                break;
            case GraphFormat.Turtle:
            case GraphFormat.TriG:
                succeeded = TurtleParser.Parse(body.Span, Add, new TurtleOptions { Syntax = SyntaxOf(format), BaseIri = baseBytes, OnError = OnError }).Succeeded;
                break;
            default:
                succeeded = NQuadsParser.Parse(body.Span, Add, new ParseOptions { Syntax = SyntaxOf(format), OnError = OnError }).Succeeded;
                break;
        }

        if (!succeeded || failure is not null)
        {
            error = failure ?? "The body does not parse.";
            return false;
        }

        if (named)
        {
            error = "The body of a Graph Store request is a graph: it has a quad in a named graph.";
            return false;
        }

        error = null;
        return true;
    }

    private static RdfSyntax SyntaxOf(GraphFormat format) => format switch
    {
        GraphFormat.NQuads => RdfSyntax.NQuads,
        GraphFormat.Turtle => RdfSyntax.Turtle,
        GraphFormat.TriG => RdfSyntax.TriG,
        _ => RdfSyntax.NTriples,
    };
}
