// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Iri;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;

namespace Varve.Protocol.Endpoints;

/// <summary>A query or an update as the protocol received it, before it is parsed.</summary>
internal sealed class SparqlRequest
{
    /// <summary>The text, as UTF-8 for a direct body, or as characters for a parameter.</summary>
    internal ReadOnlyMemory<byte> Utf8 { get; init; }

    internal string? Text { get; init; }

    /// <summary><c>default-graph-uri</c> or <c>using-graph-uri</c>.</summary>
    internal StringValues DefaultGraphs { get; init; }

    /// <summary><c>named-graph-uri</c> or <c>using-named-graph-uri</c>.</summary>
    internal StringValues NamedGraphs { get; init; }

    /// <summary>SPARQL 1.2's <c>version</c>, as a parameter or a media-type parameter.</summary>
    internal string? Version { get; init; }

    /// <summary>The version to parse at: the parameter's, or the parser's default (ADR 0092).</summary>
    internal bool TryVersion(out SparqlVersion version)
    {
        switch (Version)
        {
            case null:
            case "1.2":
                version = SparqlVersion.Sparql12;
                return true;
            case "1.2-basic":
                version = SparqlVersion.Sparql12Basic;
                return true;
            case "1.1":
                version = SparqlVersion.Sparql11;
                return true;
            default:
                version = default;
                return false;
        }
    }

    /// <summary>The protocol's dataset, or <see langword="null"/> when the request names none.</summary>
    internal bool TryDataset(out DatasetSpec? dataset, out string? invalid)
    {
        dataset = null;
        invalid = null;

        if (StringValues.IsNullOrEmpty(DefaultGraphs) && StringValues.IsNullOrEmpty(NamedGraphs))
        {
            return true;
        }

        List<RdfTerm> defaults = [];
        List<RdfTerm> named = [];

        if (!TryIris(DefaultGraphs, defaults, out invalid) || !TryIris(NamedGraphs, named, out invalid))
        {
            return false;
        }

        dataset = new DatasetSpec(AlgebraList.From(defaults), AlgebraList.From(named));
        return true;
    }

    /// <summary>An absolute IRI from a request parameter, as a term.</summary>
    internal static bool TryIri(string? text, out RdfTerm iri)
    {
        iri = null!;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(text);

        if (!IriRef.TryValidate(utf8, out IriComponents components, out _) || !components.HasScheme)
        {
            return false;
        }

        iri = RdfTerm.Iri(utf8);
        return true;
    }

    private static bool TryIris(StringValues values, List<RdfTerm> into, out string? invalid)
    {
        foreach (string? value in values)
        {
            if (!TryIri(value, out RdfTerm iri))
            {
                invalid = value;
                return false;
            }

            into.Add(iri);
        }

        invalid = null;
        return true;
    }

    /// <summary>The parse options: the request's address as the base, the version asked for.</summary>
    internal static SparqlParseOptions Options(Exchange exchange, SparqlVersion version) =>
        new(Encoding.UTF8.GetBytes(exchange.Address()), version);

    /// <summary>The <c>400</c> for text that does not parse, with its position (ADR 0092).</summary>
    internal static Task SyntaxErrorAsync(HttpContext context, SparqlParseError error) =>
        HttpProblems.WriteAsync(context, ProblemType.SparqlSyntax, error.Message, members =>
        {
            members.Number("line", error.Line);
            members.Number("column", error.Column);
            members.Number("offset", error.Offset);
        });
}
