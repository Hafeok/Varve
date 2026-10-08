// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Client.Tests;

internal static class C
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    /// <summary>A memory dataset holding <c>:s :p "1"</c>, <c>:s :p "2"</c> and, in <c>:g</c>, <c>:t :p "3"</c>.</summary>
    internal static async Task<Dataset> DataAsync()
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, Ct);
        CommitRequest request = new CommitRequest()
            .Assert(RequestTerm.FromTerm(Iri("http://ex/s")), RequestTerm.FromTerm(Iri("http://ex/p")), RequestTerm.FromTerm(RdfTerm.Literal("1"u8)))
            .Assert(RequestTerm.FromTerm(Iri("http://ex/s")), RequestTerm.FromTerm(Iri("http://ex/p")), RequestTerm.FromTerm(RdfTerm.Literal("2"u8)))
            .Assert(RequestTerm.FromTerm(Iri("http://ex/t")), RequestTerm.FromTerm(Iri("http://ex/p")), RequestTerm.FromTerm(RdfTerm.Literal("3"u8)), RequestTerm.FromTerm(Iri("http://ex/g")));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, Ct)).Outcome);
        return dataset;
    }

    /// <summary>
    /// A server over <paramref name="dataset"/> with the protocol under
    /// <c>/datasets/{dataset}</c> and three endpoints of the tests' own: a
    /// redirect, a slow answer, and fixed SPARQL results XML.
    /// </summary>
    internal static Task<ProtocolTestHost> StartAsync(Dataset dataset) =>
        ProtocolTestHost.StartAsync(dataset, (app, options) =>
        {
            app.MapGroup("/datasets/{dataset}").MapVarveDataset(options);
            app.MapPost("/redirect", (RequestDelegate)(context =>
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = "/datasets/d/sparql";
                return Task.CompletedTask;
            }));
            app.MapPost("/slow", (RequestDelegate)(async context =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
            }));
            app.MapPost("/xml", (RequestDelegate)(async context =>
            {
                context.Response.ContentType = "application/sparql-results+xml";
                await context.Response.WriteAsync("""
                    <?xml version="1.0"?>
                    <sparql xmlns="http://www.w3.org/2005/sparql-results#">
                      <head><variable name="o"/></head>
                      <results><result><binding name="o"><literal>from xml</literal></binding></result></results>
                    </sparql>
                    """, context.RequestAborted);
            }));
            app.MapGet("/plain.ttl", (RequestDelegate)(async context =>
            {
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("<http://ex/a> <http://ex/p> <b> .\n", context.RequestAborted);
            }));
            app.MapGet("/json", (RequestDelegate)(async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{}", context.RequestAborted);
            }));
        });

    /// <summary>A policy allowing the loopback server and nothing else.</summary>
    internal static EndpointPolicy Allowing(ProtocolTestHost host) => new([host.Address.ToString()], allowPrivateAddresses: true);
}
