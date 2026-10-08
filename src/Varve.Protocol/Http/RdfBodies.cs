// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;
using Varve.Turtle.Model;

namespace Varve.Protocol.Http;

/// <summary>A triple of a request body, as terms. Its blank nodes are the body's own labels.</summary>
internal readonly record struct BodyTriple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);

/// <summary>
/// A Graph Store body, parsed to triples (GSP §5). The body is a graph, so a
/// quad in a named graph — possible in N-Quads and TriG — is refused rather
/// than moved into the target graph.
/// </summary>
internal static class RdfBodies
{
    internal static bool TryParse(ReadOnlySpan<byte> body, RdfSyntax syntax, string baseIri, List<BodyTriple> into, out string? error)
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

        ParseResult result = syntax is RdfSyntax.Turtle or RdfSyntax.TriG
            ? TurtleParser.Parse(body, Add, new TurtleOptions { Syntax = syntax, BaseIri = Encoding.UTF8.GetBytes(baseIri), OnError = OnError })
            : NQuadsParser.Parse(body, Add, new ParseOptions { Syntax = syntax, OnError = OnError });

        if (!result.Succeeded || failure is not null)
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
}
