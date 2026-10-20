// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsCheck;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Grants evaluated at the applied management position (ADRs 0163 point 3,
/// 0166) through the real host: over generated grant histories, a data-plane
/// response names the management position it was served at, and exposes no
/// graph the grants there forbid; a request asking for a later position than
/// applied is <c>503 management-behind</c>; and the grants read as of a past
/// position are the grants that were in force there.
/// </summary>
public sealed class GrantTests : IAsyncLifetime
{
    // Sized for the land gate: each case is one declaration commit and a few reads.
    private const int Histories = 100;
    private const int Positions = 200;
    private const int Roles = 3;
    private const int Graphs = 4;

    private static readonly string[] Operator = ["operator"];
    private static readonly string[] RoleZero = ["r0"];

    // One grant: a role reads a set of graphs, or every graph under the prefix.
    private static readonly Gen<(int Role, int Graphs, bool Prefix)> Grant = Gen.Select(Gen.Int[0, Roles - 1], Gen.Int[1, (1 << Graphs) - 1], Gen.Int[0, 4].Select(n => n == 0));

    private static readonly Gen<(int Role, int Graphs, bool Prefix)[]> Declaration = Grant.Array[0, 4];

    private TestIssuer _issuer = null!;
    private RunningServer _server = null!;

    // The grants committed at each system position, as the model holds them.
    private readonly SortedDictionary<long, (int Role, int Graphs, bool Prefix)[]> _history = [];

    public async ValueTask InitializeAsync()
    {
        _issuer = await TestIssuer.StartAsync();
        _server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Oidc",
            ["Varve:Auth:Authority"] = _issuer.Issuer,
            ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
            ["Varve:Auth:RequireHttpsMetadata"] = "false",
            ["Varve:Manage:BootstrapOperators:0"] = "operator",
        });

        await DeclareAsync([]);

        for (int g = 0; g < Graphs; g++)
        {
            using HttpRequestMessage fill = new(HttpMethod.Post, new Uri("datasets/g/sparql", UriKind.Relative))
            {
                Content = new StringContent("INSERT DATA { GRAPH <http://ex/g/" + g + "> { <http://ex/s> <http://ex/p> " + g + " } }", Encoding.UTF8, "application/sparql-update"),
            };
            fill.Headers.Authorization = Bearer(_issuer.Mint(new Dictionary<string, object> { ["sub"] = "ops", ["roles"] = Operator }));
            Assert.Equal(HttpStatusCode.NoContent, (await _server.Client.SendAsync(fill, TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _issuer.DisposeAsync();
    }

    /// <summary>
    /// Over generated grant histories, each role's read answers at the
    /// management position the response names, and shows exactly the graphs
    /// the grants at that position give the role: none it forbids.
    /// </summary>
    [Fact]
    public async Task No_response_exposes_a_graph_the_grants_at_its_position_forbid()
    {
        await Declaration.SampleAsync(async grants =>
        {
            long position = await DeclareAsync(grants);

            for (int role = 0; role < Roles; role++)
            {
                using HttpRequestMessage read = new(HttpMethod.Get, new Uri("datasets/g/sparql?query=" + Uri.EscapeDataString("SELECT DISTINCT ?g WHERE { GRAPH ?g { ?s ?p ?o } }"), UriKind.Relative));
                read.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
                read.Headers.Authorization = Bearer(_issuer.Mint(new Dictionary<string, object> { ["sub"] = "u" + role, ["roles"] = new[] { "r" + role } }));
                using HttpResponseMessage response = await _server.Client.SendAsync(read, TestContext.Current.CancellationToken);

                long servedAt = long.Parse(response.Headers.GetValues("Varve-Management-Position").Single(), CultureInfo.InvariantCulture);
                Assert.Equal(position, servedAt);
                HashSet<int> allowed = Allowed(_history[servedAt], role);

                if (!_history[servedAt].Any(grant => grant.Role == role))
                {
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    continue;
                }

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using JsonDocument results = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
                HashSet<int> seen = [.. results.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray()
                    .Select(binding => int.Parse(binding.GetProperty("g").GetProperty("value").GetString()!["http://ex/g/".Length..], CultureInfo.InvariantCulture))];
                Assert.True(allowed.SetEquals(seen), "role r" + role + " at " + servedAt + " saw {" + string.Join(",", seen) + "}, allowed {" + string.Join(",", allowed) + "}");
            }
        }, iter: Histories, threads: 1);
    }

    /// <summary>
    /// <c>Varve-Min-Management-Position</c> (ADR 0163 point 3): at or below the
    /// applied position the request is served, and its response names a
    /// position at least that; above it, after one advance, the request is
    /// 503 management-behind with the position asked for and the one applied.
    /// </summary>
    [Fact]
    public async Task The_minimum_management_position_is_honoured()
    {
        long head = await DeclareAsync([(0, 1, false)]);

        await Gen.Long[0, head + 5].SampleAsync(async wanted =>
        {
            using HttpRequestMessage read = new(HttpMethod.Get, new Uri("datasets/g/sparql?query=ASK%7B%7D", UriKind.Relative));
            read.Headers.Authorization = Bearer(_issuer.Mint(new Dictionary<string, object> { ["sub"] = "u0", ["roles"] = RoleZero }));
            read.Headers.TryAddWithoutValidation("Varve-Min-Management-Position", wanted.ToString(CultureInfo.InvariantCulture));
            using HttpResponseMessage response = await _server.Client.SendAsync(read, TestContext.Current.CancellationToken);
            long servedAt = long.Parse(response.Headers.GetValues("Varve-Management-Position").Single(), CultureInfo.InvariantCulture);

            if (wanted <= head)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(servedAt >= wanted);
                return;
            }

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("https://w3id.org/varve/problems/management-behind", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(wanted, problem.RootElement.GetProperty("position").GetInt64());
            Assert.Equal(head, problem.RootElement.GetProperty("appliedPosition").GetInt64());
        }, iter: Positions, threads: 1);
    }

    /// <summary>
    /// "Who could read this graph on that date" (ADR 0163): the grants read as
    /// of each past system position equal the grants committed there.
    /// </summary>
    [Fact]
    public async Task The_grants_as_of_a_position_are_the_grants_in_force_there()
    {
        await Declaration.Array[4, 8].SampleAsync(async steps =>
        {
            foreach ((int Role, int Graphs, bool Prefix)[] grants in steps)
            {
                await DeclareAsync(grants);
            }
        }, iter: 1, threads: 1);

        long[] positions = [.. _history.Keys];

        await Gen.Int[0, positions.Length - 1].SampleAsync(async index =>
        {
            long at = positions[index];
            using HttpRequestMessage read = new(HttpMethod.Get, new Uri("manage/datasets/g/grants", UriKind.Relative));
            read.Headers.Authorization = Bearer(_issuer.MintManagement(new Dictionary<string, object> { ["sub"] = "ops", ["roles"] = Operator }));
            read.Headers.TryAddWithoutValidation("Varve-As-Of", "position:" + at.ToString(CultureInfo.InvariantCulture));
            using HttpResponseMessage response = await _server.Client.SendAsync(read, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal(at, body.RootElement.GetProperty("position").GetInt64());

            List<string> listed = [];

            foreach (JsonElement grant in body.RootElement.GetProperty("grants").EnumerateArray())
            {
                if (grant.GetProperty("grantee").GetProperty("value").GetString() is { } role && role.StartsWith('r'))
                {
                    JsonElement graphs = grant.GetProperty("graphs");
                    List<string> named = [.. graphs.GetProperty("graphs").EnumerateArray().Select(g => g.GetString()!).Order(StringComparer.Ordinal)];
                    List<string> prefixes = [.. graphs.GetProperty("prefixes").EnumerateArray().Select(p => p.GetString()!)];
                    listed.Add(role + " " + string.Join(" ", named) + " " + string.Join(" ", prefixes));
                }
            }

            List<string> expected = [.. _history[at].Select(Describe)];
            Assert.Equal(expected.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
        }, iter: Positions, threads: 1);
    }

    // Replaces the dataset's declaration graph with these grants; the system
    // position the commit landed at.
    private async Task<long> DeclareAsync((int Role, int Graphs, bool Prefix)[] grants)
    {
        StringBuilder trig = new("@prefix varve: <https://w3id.org/varve/ns#> .\n<urn:g> a varve:Dataset ; varve:datasetName \"g\" ; varve:storage varve:Memory .\n");

        for (int role = 0; role < Roles; role++)
        {
            trig.Append("<urn:r").Append(role).Append("> a varve:Role ; varve:claimValue \"r").Append(role).Append("\" .\n");
        }

        for (int i = 0; i < grants.Length; i++)
        {
            (int role, int graphs, bool prefix) = grants[i];
            trig.Append("<urn:grant").Append(i).Append("> a varve:Grant ; varve:grantee <urn:r").Append(role).Append("> ; varve:permission varve:read");

            if (prefix)
            {
                trig.Append(" ; varve:graphPrefix \"http://ex/g/\"");
            }
            else
            {
                for (int g = 0; g < Graphs; g++)
                {
                    if ((graphs & (1 << g)) != 0)
                    {
                        trig.Append(" ; varve:graph <http://ex/g/").Append(g).Append('>');
                    }
                }
            }

            trig.Append(" .\n");
        }

        using HttpRequestMessage put = new(HttpMethod.Put, new Uri("manage/datasets/g", UriKind.Relative))
        {
            Content = new StringContent(trig.ToString(), Encoding.UTF8, "application/trig"),
        };
        put.Headers.Authorization = Bearer(_issuer.MintManagement(new Dictionary<string, object> { ["sub"] = "ops", ["roles"] = Operator }));
        using HttpResponseMessage response = await _server.Client.SendAsync(put, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        long position = long.Parse(response.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture);
        _history[position] = grants;

        // An unchanged declaration commits nothing: the grants are those of
        // the position it already was at, which the history holds the same.
        return position;
    }

    private static HashSet<int> Allowed((int Role, int Graphs, bool Prefix)[] grants, int role)
    {
        HashSet<int> allowed = [];

        foreach ((int who, int graphs, bool prefix) in grants)
        {
            for (int g = 0; g < Graphs; g++)
            {
                if (who == role && (prefix || (graphs & (1 << g)) != 0))
                {
                    allowed.Add(g);
                }
            }
        }

        return allowed;
    }

    private static string Describe((int Role, int Graphs, bool Prefix) grant)
    {
        List<string> named = [];

        for (int g = 0; g < Graphs && !grant.Prefix; g++)
        {
            if ((grant.Graphs & (1 << g)) != 0)
            {
                named.Add("http://ex/g/" + g);
            }
        }

        named.Sort(StringComparer.Ordinal);
        return "r" + grant.Role + " " + string.Join(" ", named) + " " + (grant.Prefix ? "http://ex/g/" : string.Empty);
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
}
