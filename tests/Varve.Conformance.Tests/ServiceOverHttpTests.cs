// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>sparql11/service</c> end to end over HTTP (ADR 0104): each
/// <c>qt:serviceData</c> endpoint of a case is an in-process server over a
/// memory dataset loaded with that data, the handler under test is
/// <see cref="HttpServiceHandler"/>, the policy allows the manifest's
/// endpoint IRIs and nothing else, and a message handler of this test maps
/// those IRIs to the loopback servers. One ratchet line per case,
/// <c>&lt;test IRI&gt;@http</c>, beside the in-process run's two.
/// </summary>
public class ServiceOverHttpTests
{
    private const string Suite = "sparql11/service";

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EvaluationEntry entry in EvaluationCatalogue.Entries.Where(e => e.Suite == Suite))
        {
            string id = entry.TestIri + "@http";
            yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
        }
    }

    [Fact]
    public void The_service_suite_runs_over_http_whole()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Assert.Equal(7, Cases().Count());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        EvaluationEntry entry = EvaluationCatalogue.ByIri[testIri[..testIri.LastIndexOf('@')]];
        List<ProtocolTestHost> hosts = [];
        List<Dataset> datasets = [];
        Dictionary<string, Uri> addresses = new(StringComparer.Ordinal);

        // The policy allows the manifest's endpoints and nothing else; the
        // same handler federates the local query and each server's own
        // queries, since a case's endpoint may itself call the next one
        // (service3's nested SERVICE).
        string[] endpoints = [.. entry.ServiceData.Select(s => s.Endpoint)];
        EndpointPolicy policy = endpoints.Length == 0 ? EndpointPolicy.None : new EndpointPolicy(endpoints, allowPrivateAddresses: false);
        using HttpClient http = new(new Rewriting(addresses, new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }), disposeHandler: true);
        HttpServiceHandler handler = new(http, policy, ClientLimits.Default);

        try
        {
            foreach ((string endpoint, IReadOnlyList<string> data) in entry.ServiceData)
            {
                Dataset dataset = await LoadAsync(data, TestContext.Current.CancellationToken);
                ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: TestContext.Current.CancellationToken, serviceHandler: handler);
                datasets.Add(dataset);
                hosts.Add(host);
                addresses[endpoint] = new Uri(host.Address, "datasets/service/sparql");
            }

            string? failure = await EvaluationRunner.RunAsync(entry, EvaluationSubjects.Dataset, handler, TestContext.Current.CancellationToken);
            Assert.True(failure is null, entry.Suite + " " + entry.Name + " (http): " + failure);
        }
        finally
        {
            foreach (ProtocolTestHost host in hosts)
            {
                await host.DisposeAsync();
            }

            foreach (Dataset dataset in datasets)
            {
                await dataset.DisposeAsync();
            }
        }
    }

    /// <summary>The endpoint's data as one commit per file (blank nodes fresh per request, ADR 0044).</summary>
    private static async Task<Dataset> LoadAsync(IReadOnlyList<string> files, CancellationToken ct)
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = FixedClock.Instance }, ct);

        for (int f = 0; f < files.Count; f++)
        {
            CommitRequest request = new();

            foreach (DataQuad quad in EvaluationData.Quads(files[f]))
            {
                RdfTerm subject = EvaluationSubjects.Scope(quad.Subject, f);
                RdfTerm @object = EvaluationSubjects.Scope(quad.Object, f);

                if (quad.Graph is null)
                {
                    request.Assert(RequestTerm.FromTerm(subject), RequestTerm.FromTerm(quad.Predicate), RequestTerm.FromTerm(@object));
                }
                else
                {
                    request.Assert(RequestTerm.FromTerm(subject), RequestTerm.FromTerm(quad.Predicate), RequestTerm.FromTerm(@object), RequestTerm.FromTerm(quad.Graph));
                }
            }

            if (request.Count.Value > 0)
            {
                CommitResult result = await dataset.CommitAsync(request, ct);
                Assert.True(result.Outcome is CommitOutcome.Committed or CommitOutcome.NoChange, result.Outcome.ToString());
            }
        }

        return dataset;
    }

    /// <summary>Maps the manifest's endpoint IRIs to the loopback servers; anything else is sent as written and fails to resolve.</summary>
    private sealed class Rewriting(IReadOnlyDictionary<string, Uri> addresses, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && addresses.TryGetValue(request.RequestUri.OriginalString, out Uri? address))
            {
                request.RequestUri = address;
            }

            return base.SendAsync(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && addresses.TryGetValue(request.RequestUri.OriginalString, out Uri? address))
            {
                request.RequestUri = address;
            }

            return base.Send(request, cancellationToken);
        }
    }
}
