// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>sparql11/service-description</c>: three entries that name a check and
/// carry no action, so each check here is ours, written from the entry's name
/// and the Service Description Recommendation (ADR 0092). The schema check
/// uses <see cref="ServiceDescriptionShapes"/>, a hand-written stand-in for
/// SHACL until milestone 8.
/// </summary>
internal static class ServiceDescriptionChecks
{
    private const string Base = "http://www.w3.org/2009/sparql/docs/tests/data-sparql11/service-description/manifest#";
    private const string Sd = "http://www.w3.org/ns/sparql-service-description#";

    internal static IReadOnlyList<string> Iris { get; } = [Base + "returns-rdf", Base + "has-endpoint-triple", Base + "conforms-to-schema"];

    internal static async Task<string?> RunAsync(string testIri, ProtocolSubject subject, CancellationToken cancellationToken)
    {
        await using ProtocolRunner.Store store = await ProtocolRunner.Store.OpenAsync(subject, cancellationToken);

        // Two named graphs, so the description has a dataset to describe.
        CommitRequest data = new();
        data.Assert(Iri("http://example.org/s"), Iri("http://example.org/p"), Iri("http://example.org/o"), Iri("http://example.org/g1"));
        data.Assert(Iri("http://example.org/s"), Iri("http://example.org/p"), Iri("http://example.org/o"), Iri("http://example.org/g2"));
        await store.Dataset.CommitAsync(data, cancellationToken);

        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(store.Dataset, ProtocolRunner.Mount, stopping: cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Get, new System.Uri("sparql", System.UriKind.Relative));
        request.Headers.Host = ProtocolCatalogue.Host;
        request.Headers.TryAddWithoutValidation("Accept", "text/turtle");
        using HttpResponseMessage response = await host.Client.SendAsync(request, cancellationToken);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if ((int)response.StatusCode != 200 || response.Content.Headers.ContentType?.MediaType != "text/turtle")
        {
            return "GET on the endpoint answered " + (int)response.StatusCode + " " + response.Content.Headers.ContentType;
        }

        List<DataQuad> quads = [];
        ParseResult parsed = TurtleParser.Parse(body, (in QuadView q) => quads.Add(new DataQuad(q.Subject.Materialise(), q.Predicate.Materialise(), q.Object.Materialise(), null)),
            new TurtleOptions { Syntax = RdfSyntax.Turtle, BaseIri = "http://www.example/sparql"u8.ToArray() });

        if (!parsed.Succeeded)
        {
            return "the description does not parse: " + parsed.FirstError;
        }

        switch (testIri[Base.Length..])
        {
            case "returns-rdf":
                return quads.Count > 0 ? null : "the description is empty";
            case "has-endpoint-triple":
                return quads.Any(q => ManifestGraph.Text(q.Predicate) == Sd + "endpoint" && ManifestGraph.Text(q.Object) == "http://www.example/sparql")
                    ? null
                    : "no sd:endpoint <http://www.example/sparql>";
            default:
                List<string> violations = ServiceDescriptionShapes.Validate(quads);

                if (!quads.Any(q => ManifestGraph.Text(q.Predicate) == Sd + "name" && ManifestGraph.Text(q.Object) == "http://example.org/g1"))
                {
                    violations.Add("the named graph http://example.org/g1 is not described");
                }

                return violations.Count == 0 ? null : string.Join("; ", violations);
        }
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
