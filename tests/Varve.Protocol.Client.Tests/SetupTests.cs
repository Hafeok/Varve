// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Varve.Protocol.Client.Hosting;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// Client-side setup (ADR 0130), against the protocol over a socket: applying
/// a setup twice commits once per baseline source and per migration, then
/// nothing; a new migration applies alone; a failed migration leaves the head
/// and the record as they were; a baseline with a blank node is refused before
/// a request; two appliers at once apply each migration once; and AddVarve's
/// hosted service applies it at start, or fails the start with the problem.
/// </summary>
public class SetupTests
{
    private const string Vocabulary = "https://shop.example/vocabulary";
    private static readonly HttpClient Http = OutboundHttp.CreateClient();

    [Fact]
    public async Task Applying_twice_commits_once_per_source_and_per_migration_then_nothing()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        SparqlHttpClient client = Client(host);
        DatasetSetup setup = Setup(Migration("1-statuses"), Migration("2-retire-pending"));

        SetupResult first = await client.ApplyAsync(setup, C.Ct);
        Assert.False(first.Created);
        Assert.True(first.SettingsChanged);
        Assert.Equal(["vocabulary", "statuses"], first.BaselinesCommitted);
        Assert.Equal(["1-statuses", "2-retire-pending"], first.MigrationsApplied);
        Assert.Equal(5L, dataset.Head.Value);
        Assert.Equal(5L, first.Position);

        SetupResult second = await client.ApplyAsync(setup, C.Ct);
        Assert.False(second.Committed);
        Assert.Equal(5L, dataset.Head.Value);
        Assert.Equal(["1-statuses", "2-retire-pending"], await RecordedAsync(client));
    }

    [Fact]
    public async Task What_the_application_wrote_to_a_baseline_graph_survives_every_apply()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        SparqlHttpClient client = Client(host);
        await client.ApplyAsync(Setup(), C.Ct);

        // The application adds to the vocabulary graph a baseline also writes.
        using (HttpResponseMessage added = await client.UpdateAsync(
            "INSERT DATA { GRAPH <" + Vocabulary + "> { <https://shop.example/ns#Refund> a <http://www.w3.org/2000/01/rdf-schema#Class> } }", ifMatch: null, C.Ct))
        {
            Assert.True(added.IsSuccessStatusCode);
        }

        long before = dataset.Head.Value;
        SetupResult again = await client.ApplyAsync(Setup(), C.Ct);

        Assert.False(again.Committed);
        Assert.Equal(before, dataset.Head.Value);
        using HttpResponseMessage asked = await client.QueryAsync("ASK { GRAPH <" + Vocabulary + "> { <https://shop.example/ns#Refund> ?p ?o } }", "application/sparql-results+json", null, C.Ct);
        Assert.Contains("true", await asked.Content.ReadAsStringAsync(C.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_are_desired_state_a_declared_key_is_restored_and_an_undeclared_one_left_alone()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        SparqlHttpClient client = Client(host);
        await client.ApplyAsync(Setup(), C.Ct);
        Assert.Equal("Current", await ScopeAsync(client));

        // A manual change to a key the setup declares is overwritten on the next apply.
        await PatchScopeAsync(client, "AllHistory");
        SetupResult restored = await client.ApplyAsync(Setup(), C.Ct);
        Assert.True(restored.SettingsChanged);
        Assert.Equal("Current", await ScopeAsync(client));

        // A setup that declares no settings, or an empty object, leaves them as they are.
        await PatchScopeAsync(client, "AllHistory");
        DatasetSetup silent = new("d") { Settings = "{}" };
        Assert.False((await client.ApplyAsync(silent, C.Ct)).SettingsChanged);
        Assert.False((await client.ApplyAsync(new DatasetSetup("d"), C.Ct)).SettingsChanged);
        Assert.Equal("AllHistory", await ScopeAsync(client));
    }

    [Fact]
    public async Task A_new_migration_applies_alone()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        SparqlHttpClient client = Client(host);
        await client.ApplyAsync(Setup(Migration("1-statuses")), C.Ct);
        long before = dataset.Head.Value;

        SetupResult result = await client.ApplyAsync(Setup(Migration("1-statuses"), Migration("2-retire-pending")), C.Ct);

        Assert.Empty(result.BaselinesCommitted);
        Assert.False(result.SettingsChanged);
        Assert.Equal(["2-retire-pending"], result.MigrationsApplied);
        Assert.Equal(before + 1, dataset.Head.Value);
    }

    [Fact]
    public async Task A_failed_migration_leaves_the_head_and_the_record_as_they_were()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        SparqlHttpClient client = Client(host);
        await client.ApplyAsync(Setup(Migration("1-statuses")), C.Ct);
        long before = dataset.Head.Value;

        // A LOAD the server refuses: the update fails as it runs, after it parsed.
        DatasetSetup failing = Setup(Migration("1-statuses"), new SetupMigration("2-load", "LOAD <http://ex.invalid/data.ttl>"), Migration("3-never"));
        SetupException refused = await Assert.ThrowsAsync<SetupException>(() => client.ApplyAsync(failing, C.Ct));

        Assert.Equal("migration 2-load", refused.Step);
        Assert.NotNull(refused.Status);
        Assert.Equal(before, dataset.Head.Value);
        Assert.Equal(["1-statuses"], await RecordedAsync(client));
    }

    [Fact]
    public async Task A_migration_that_does_not_parse_commits_nothing_with_its_record()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        SparqlHttpClient client = Client(host);

        SetupException refused = await Assert.ThrowsAsync<SetupException>(() => client.ApplyAsync(Setup(new SetupMigration("1-broken", "DELETE WHERE { ?s ?p }")), C.Ct));

        Assert.Equal(400, refused.Status);
        Assert.Contains("sparql-syntax", refused.Problem, StringComparison.Ordinal);
        Assert.Empty(await RecordedAsync(client));
    }

    [Theory]
    [InlineData("text/turtle", "<http://ex/a> <http://ex/p> [ <http://ex/q> 1 ] .")]
    [InlineData("application/n-triples", "_:b1 <http://ex/p> <http://ex/o> .\n")]
    [InlineData("application/ld+json", "{\"@id\": \"http://ex/a\", \"http://ex/p\": {\"http://ex/q\": 1}}")]
    [InlineData("application/rdf+xml", "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:ex=\"http://ex/\"><rdf:Description><ex:p>1</ex:p></rdf:Description></rdf:RDF>")]
    public async Task A_baseline_with_a_blank_node_is_refused_before_any_request(string mediaType, string content)
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        DatasetSetup setup = new("d");
        setup.Baselines.Add(BaselineSource.FromBytes("with-a-blank-node", Vocabulary, mediaType, Encoding.UTF8.GetBytes(content)));

        SetupException refused = await Assert.ThrowsAsync<SetupException>(() => Client(host).ApplyAsync(setup, C.Ct));

        Assert.Equal("baseline with-a-blank-node", refused.Step);
        Assert.Contains("skolem IRI", refused.Message, StringComparison.Ordinal);
        Assert.Contains("RDF 1.1 Concepts §3.5", refused.Message, StringComparison.Ordinal);
        Assert.Null(refused.Status);
        Assert.Equal(0L, dataset.Head.Value);
    }

    [Fact]
    public async Task Two_appliers_at_once_apply_each_migration_once()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        DatasetSetup setup = Setup(Migration("1-statuses"), Migration("2-retire-pending"), Migration("3-rename"));

        await Task.WhenAll(Client(host).ApplyAsync(setup, C.Ct), Client(host).ApplyAsync(setup, C.Ct));

        Assert.Equal(["1-statuses", "2-retire-pending", "3-rename"], await RecordedAsync(Client(host)));
        Assert.Equal(6L, dataset.Head.Value);
    }

    [Fact]
    public async Task A_setup_is_refused_for_another_dataset()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(host).ApplyAsync(new DatasetSetup("elsewhere"), C.Ct));
    }

    [Fact]
    public async Task AddVarve_applies_the_setup_as_the_host_starts_and_a_failure_fails_the_start()
    {
        await using Dataset dataset = await EmptyAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddVarve(options =>
        {
            options.Server = host.Address;
            options.Setup = Setup(Migration("1-statuses"));
        });
        await using (WebApplication app = builder.Build())
        {
            await app.StartAsync(C.Ct);
            Assert.Equal(["1-statuses"], await RecordedAsync(Client(host)));
            await app.StopAsync(C.Ct);
        }

        DatasetSetup broken = new("d");
        broken.Migrations.Add(new SetupMigration("2-broken", "INSERT DATA {"));
        WebApplicationBuilder failing = WebApplication.CreateSlimBuilder();
        failing.Services.AddVarve(options =>
        {
            options.Server = host.Address;
            options.Setup = broken;
        });
        await using WebApplication refused = failing.Build();
        SetupException failure = await Assert.ThrowsAsync<SetupException>(() => refused.StartAsync(C.Ct));
        Assert.Equal("migration 2-broken", failure.Step);
        Assert.Contains("sparql-syntax", failure.Message, StringComparison.Ordinal);
    }

    private static DatasetSetup Setup(params SetupMigration[] migrations)
    {
        DatasetSetup setup = new("d") { Settings = "{\"defaultAccessScope\": \"Current\"}" };
        setup.Baselines.Add(BaselineSource.FromBytes("vocabulary", Vocabulary, "text/turtle", Encoding.UTF8.GetBytes(
            "@prefix shop: <https://shop.example/ns#> .\nshop:Order a <http://www.w3.org/2000/01/rdf-schema#Class> .\nshop:status a <http://www.w3.org/1999/02/22-rdf-syntax-ns#Property> .\n")));
        setup.Baselines.Add(BaselineSource.FromBytes("statuses", "https://shop.example/statuses", "application/n-triples", Encoding.UTF8.GetBytes(
            "<https://shop.example/status/placed> <http://www.w3.org/2000/01/rdf-schema#label> \"placed\" .\n")));

        foreach (SetupMigration migration in migrations)
        {
            setup.Migrations.Add(migration);
        }

        return setup;
    }

    private static async Task<string> ScopeAsync(SparqlHttpClient client)
    {
        using HttpResponseMessage settings = await client.SettingsAsync(C.Ct);
        using JsonDocument json = JsonDocument.Parse(await settings.Content.ReadAsStringAsync(C.Ct));
        return json.RootElement.GetProperty("defaultAccessScope").GetString()!;
    }

    private static async Task PatchScopeAsync(SparqlHttpClient client, string scope)
    {
        using HttpResponseMessage head = await client.SettingsAsync(C.Ct);
        using HttpResponseMessage patched = await client.PatchSettingsAsync("{\"defaultAccessScope\": \"" + scope + "\"}", SparqlHttpClient.PositionOf(head), C.Ct);
        Assert.True(patched.IsSuccessStatusCode);
    }

    private static SetupMigration Migration(string id) =>
        new(id, "INSERT DATA { <https://shop.example/migrated/" + id + "> <http://www.w3.org/2000/01/rdf-schema#label> \"" + id + "\" }");

    private static async Task<List<string>> RecordedAsync(SparqlHttpClient client)
    {
        using HttpResponseMessage response = await client.QueryAsync(
            "SELECT ?id WHERE { GRAPH <https://w3id.org/varve/ns#setup> { ?m a <https://w3id.org/varve/ns#Migration> ; <https://w3id.org/varve/ns#migrationId> ?id } } ORDER BY ?id",
            "application/sparql-results+json", null, C.Ct);
        using JsonDocument results = JsonDocument.Parse(await response.Content.ReadAsStringAsync(C.Ct));
        return [.. results.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray().Select(b => b.GetProperty("id").GetProperty("value").GetString()!)];
    }

    private static SparqlHttpClient Client(ProtocolTestHost host) => new(Http, new Uri(host.Address, "datasets/d/"), ClientLimits.Default);

    private static async Task<Dataset> EmptyAsync() =>
        await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, C.Ct);

    // A settings commit names an agent (ADR 0106): the test's identity has one.
    private static Task<ProtocolTestHost> StartAsync(Dataset dataset) =>
        ProtocolTestHost.StartAsync(dataset, identity: new Someone(), stopping: C.Ct);

    private sealed class Someone : ICallerIdentity
    {
        public RequestTerm AgentOf(ClaimsPrincipal caller) => RequestTerm.FromTerm(RdfTerm.Iri("https://issuer.example/#setup"u8));
    }
}
