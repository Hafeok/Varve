// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The problem catalogue (ADR 0119): every type the server can emit is in
/// it with its page, and every problem the endpoints write carries exactly
/// the catalogue's title and status and no member the type does not declare.
/// </summary>
public class ProblemCatalogueTests
{
    private const string Sparql = "datasets/d/sparql";

    /// <summary>
    /// Types the host writes outside the protocol's endpoints, exercised in
    /// <c>Varve.Server.Tests</c>: a refused caller, an address nobody serves,
    /// readiness, and the drain.
    /// </summary>
    private static readonly ImmutableArray<ProblemType> ServerEmitted =
        [ProblemType.Unauthorized, ProblemType.Forbidden, ProblemType.NotFound, ProblemType.NotReady, ProblemType.ShuttingDown, ProblemType.TooManyRequests];

    /// <summary>
    /// Types no request can trigger yet: the archive horizon (spec T3) does
    /// not exist, and the governance limits of ADR 0114 land with their own
    /// tests. Each is removed from here by the slice that makes it emittable.
    /// </summary>
    private static readonly ImmutableArray<ProblemType> NotYetEmittable =
        [ProblemType.BelowArchiveHorizon, ProblemType.ServerBusy, ProblemType.MemoryLimitExceeded, ProblemType.AsOfDistanceExceeded, ProblemType.TooManyLiveTails];

    [Fact]
    public void every_problem_type_is_catalogued_with_a_page_stating_the_same_shape()
    {
        IEnumerable<ProblemType> types = typeof(ProblemType)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(ProblemType))
            .Select(p => (ProblemType)p.GetValue(null)!);
        string problems = Path.Combine(RepositoryRoot(), "docs", "problems");
        string index = File.ReadAllText(Path.Combine(problems, "README.md"));
        int count = 0;

        foreach (ProblemType type in types)
        {
            count++;
            Assert.True(ProblemCatalogue.TryGet(type, out ProblemShape? shape), type.Value + " is not in the catalogue");
            string page = Path.Combine(problems, shape!.Name + ".md");
            Assert.True(File.Exists(page), page + " is missing");
            string text = File.ReadAllText(page);
            Assert.Contains("| **Type** | `" + type.Value + "` |", text, StringComparison.Ordinal);
            Assert.Contains("| **Title** | " + shape.Title + " |", text, StringComparison.Ordinal);
            Assert.Contains("| **Status** | `" + shape.Status.ToString(System.Globalization.CultureInfo.InvariantCulture) + "` |", text, StringComparison.Ordinal);
            string members = shape.Members.Length == 0 ? "none" : string.Join(", ", shape.Members.Select(m => "`" + m + "`"));
            Assert.Contains("| **Members** | " + members + " |", text, StringComparison.Ordinal);
            Assert.Contains("[`" + shape.Name + "`](" + shape.Name + ".md)", index, StringComparison.Ordinal);
        }

        Assert.Equal(count, ProblemCatalogue.All.Length);
        Assert.Equal(count, Directory.GetFiles(problems, "*.md").Length - 1);
    }

    [Fact]
    public async Task every_problem_the_endpoints_emit_has_its_catalogued_shape_and_no_other_member()
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()),
            new DatasetOptions { Clock = TimeProvider.System, Validators = [new RefusesSecret()] }, P.Ct);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("d", dataset);
        ProtocolLimits limits = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), new ByteCount(200), new ByteCount(2048), TimeSpan.FromSeconds(15));
        GraphScope people = GraphScope.Of([RdfTerm.Iri("http://ex/people"u8)], [], DefaultGraphAccess.Included);
        TestAdministration administration = new();

        await using (dataset)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(
            datasets,
            map: (app, o) =>
            {
                app.MapGroup("/datasets/{dataset}").MapVarveDataset(o);
                app.MapVarveAdministration("/datasets", o);
            },
            limits: limits,
            accessScopes: new Scoped(new CallerScope(GraphScope.All, people, AdminAccess.None)),
            administration: administration,
            updates: inner => new Racing(inner, dataset),
            stopping: P.Ct))
        {
            await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
            Dictionary<ProblemType, Func<HttpRequestMessage>> triggers = new()
            {
                [ProblemType.SparqlSyntax] = () => P.Query(Sparql, "SELECT"),
                [ProblemType.RdfSyntax] = () => Put("datasets/d/graphs?default", "not n-triples", "application/n-triples"),
                [ProblemType.BadRequest] = () => P.Get(Sparql + "?query=ASK%7B%7D&query=ASK%7B%7D"),
                [ProblemType.OperationFailed] = () => P.Update(Sparql, "LOAD <http://ex/nowhere>"),
                [ProblemType.AgentRequired] = () => Put("datasets/d/settings", "{\"defaultAccessScope\":\"Current\"}", "application/json"),
                [ProblemType.GraphNotWritable] = () => P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/other> { <http://ex/a> <http://ex/p> 1 } }"),
                [ProblemType.DatasetNotFound] = () => P.Get("datasets/nowhere/sparql?query=ASK%7B%7D"),
                [ProblemType.GraphNotFound] = () => P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/none"), "application/n-triples"),
                [ProblemType.PositionNotReached] = () => P.Query(Sparql, "ASK {}", asOf: "position:99"),
                [ProblemType.BeforeFirstCommit] = () => P.Query(Sparql, "ASK {}", asOf: "time:2000-01-01T00:00:00Z"),
                [ProblemType.MethodNotAllowed] = () => new HttpRequestMessage(HttpMethod.Delete, new Uri(Sparql, UriKind.Relative)),
                [ProblemType.NotAcceptable] = () => P.Query(Sparql, "ASK {}", accept: "image/png"),
                [ProblemType.Conflict] = () => P.Update(Sparql, "DELETE WHERE { ?s ?p ?o }", "\"" + dataset.Head.Value + "\""),
                [ProblemType.DatasetExists] = () => Put("datasets/x", "{\"storage\":\"Memory\"}", "application/json"),
                [ProblemType.DatasetOpen] = () => new HttpRequestMessage(HttpMethod.Delete, new Uri("datasets/x", UriKind.Relative)),
                [ProblemType.PreconditionFailed] = () => P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }", "\"77\""),
                [ProblemType.RequestTooLarge] = () => P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 200).Select(i => "<http://ex/s" + i + "> <http://ex/p> " + i + " . ")) + "}"),
                [ProblemType.UnsupportedMediaType] = () => new HttpRequestMessage(HttpMethod.Post, new Uri(Sparql, UriKind.Relative)) { Content = new StringContent("ASK {}", Encoding.UTF8, "text/plain") },
                [ProblemType.Rejected] = () => P.Update(Sparql, "INSERT DATA { <http://ex/secret> <http://ex/p> 1 }"),
                [ProblemType.Unavailable] = () => Put("datasets/broken/state", "{\"state\":\"open\"}", "application/json"),
                [ProblemType.ReadLimitExceeded] = () => P.Query(Sparql, "SELECT * { ?s ?p ?o } "),
            };

            // The dataset the admin triggers need: created first.
            Assert.Equal(System.Net.HttpStatusCode.Created, (await host.Client.SendAsync(Put("datasets/x", "{\"storage\":\"File\"}", "application/json"), P.Ct)).StatusCode);
            await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 20).Select(i => "<http://ex/s" + i + "> <http://ex/p> " + i + " . ")) + "}"), P.Ct);

            foreach ((ProblemType type, Func<HttpRequestMessage> trigger) in triggers)
            {
                ProblemShape shape = ProblemCatalogue.Of(type);
                HttpResponseMessage response = await host.Client.SendAsync(trigger(), P.Ct);
                string body = await response.Content.ReadAsStringAsync(P.Ct);
                Assert.True(shape.Status == (int)response.StatusCode, shape.Name + ": " + (int)response.StatusCode + " " + body);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                using JsonDocument problem = JsonDocument.Parse(body);
                JsonElement root = problem.RootElement;
                Assert.Equal(type.Value, root.GetProperty("type").GetString());
                Assert.Equal(shape.Title, root.GetProperty("title").GetString());
                Assert.Equal(shape.Status, root.GetProperty("status").GetInt32());
                Assert.Equal(P.Header(response, "Varve-Request-Id"), root.GetProperty("instance").GetString());
                Assert.Equal("Accept,Varve-As-Of,Authorization", string.Join(",", response.Headers.Vary));

                foreach (JsonProperty member in root.EnumerateObject())
                {
                    if (member.Name is "type" or "title" or "status" or "detail" or "instance")
                    {
                        continue;
                    }

                    Assert.True(shape.Members.Contains(member.Name), shape.Name + " carries an undeclared member " + member.Name);
                }

                if (type == ProblemType.MethodNotAllowed)
                {
                    Assert.NotEmpty(response.Content.Headers.Allow);
                }
            }

            // The catalogue is covered: by these, by the host's own, or not yet.
            HashSet<string> covered = [.. triggers.Keys.Select(t => t.Value), .. ServerEmitted.Select(t => t.Value), .. NotYetEmittable.Select(t => t.Value)];
            Assert.Equal(ProblemCatalogue.All.Select(s => s.Type.Value).Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
        }
    }

    private static HttpRequestMessage Put(string path, string body, string mediaType) =>
        new(HttpMethod.Put, new Uri(path, UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    internal static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("No Varve.slnx above " + AppContext.BaseDirectory + ".");
    }

    /// <summary>Refuses any commit that mentions the secret subject.</summary>
    private sealed class RefusesSecret : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            foreach (Quad quad in delta.Asserted)
            {
                if (proposed.TryExternalise(quad.Subject, out RdfTerm? subject) && Encoding.UTF8.GetString(subject!.Lexical) == "http://ex/secret")
                {
                    return ValidationVerdict.Reject([RdfTerm.Literal("secret"u8)]);
                }
            }

            return ValidationVerdict.Accept();
        }
    }

    private sealed class Scoped(CallerScope scope) : IAccessScopes
    {
        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => scope;
    }

    /// <summary>
    /// Commits a quad to the dataset before every update that expects a
    /// position, so that the pin is overtaken and the sequencer answers
    /// Conflict: the race ADR 0094 maps to 409, made deterministic.
    /// </summary>
    private sealed class Racing(ISparqlUpdateExecutor inner, Dataset dataset) : ISparqlUpdateExecutor
    {
        private int _n;

        public async ValueTask<CommitResult> ExecuteAsync(Dataset target, Update update, CommitMetadata metadata, Position? expectedPosition, CallerScope scope, CancellationToken cancellationToken)
        {
            if (expectedPosition is not null && update.Operations[0] is DeleteWhere)
            {
                int n = Interlocked.Increment(ref _n);
                await dataset.CommitAsync(new CommitRequest().Assert(RdfTerm.Iri("http://ex/race"u8), RdfTerm.Iri("http://ex/p"u8), RdfTerm.Literal(Encoding.UTF8.GetBytes("r" + n))), cancellationToken);
            }

            return await inner.ExecuteAsync(target, update, metadata, expectedPosition, scope, cancellationToken);
        }
    }

    /// <summary>An in-memory dataset map for the admin endpoints: "broken" never opens.</summary>
    private sealed class TestAdministration : IDatasetAdministration
    {
        private readonly Dictionary<string, (DatasetStorage Storage, DatasetState State)> _entries = new(StringComparer.Ordinal)
        {
            ["broken"] = (DatasetStorage.File, DatasetState.Failed),
        };

        public IReadOnlyList<DatasetEntry> List()
        {
            lock (_entries)
            {
                return [.. _entries.Select(e => new DatasetEntry(new DatasetName(e.Key), e.Value.State, e.Value.Storage, DatasetOrigin.Created, e.Value.State == DatasetState.Failed ? "the lease is held elsewhere" : null, null, null))];
            }
        }

        public ValueTask<AdminOutcome> CreateAsync(DatasetName name, DatasetStorage storage, CancellationToken cancellationToken)
        {
            lock (_entries)
            {
                if (_entries.ContainsKey(name.Value))
                {
                    return ValueTask.FromResult(AdminOutcome.Exists);
                }

                _entries[name.Value] = (storage, DatasetState.Open);
                return ValueTask.FromResult(AdminOutcome.Done);
            }
        }

        public ValueTask<AdminOutcome> OpenAsync(DatasetName name, CancellationToken cancellationToken)
        {
            lock (_entries)
            {
                if (!_entries.TryGetValue(name.Value, out (DatasetStorage Storage, DatasetState State) entry))
                {
                    return ValueTask.FromResult(AdminOutcome.NotFound);
                }

                if (entry.State == DatasetState.Failed)
                {
                    return ValueTask.FromResult(AdminOutcome.Failed);
                }

                _entries[name.Value] = (entry.Storage, DatasetState.Open);
                return ValueTask.FromResult(AdminOutcome.Done);
            }
        }

        public ValueTask<AdminOutcome> CloseAsync(DatasetName name, CancellationToken cancellationToken)
        {
            lock (_entries)
            {
                if (!_entries.TryGetValue(name.Value, out (DatasetStorage Storage, DatasetState State) entry) || entry.State != DatasetState.Open)
                {
                    return ValueTask.FromResult(AdminOutcome.NotFound);
                }

                _entries[name.Value] = (entry.Storage, DatasetState.Closed);
                return ValueTask.FromResult(AdminOutcome.Done);
            }
        }

        public ValueTask<AdminOutcome> DeleteAsync(DatasetName name, CancellationToken cancellationToken)
        {
            lock (_entries)
            {
                if (!_entries.TryGetValue(name.Value, out (DatasetStorage Storage, DatasetState State) entry))
                {
                    return ValueTask.FromResult(AdminOutcome.NotFound);
                }

                if (entry.State == DatasetState.Open)
                {
                    return ValueTask.FromResult(AdminOutcome.Open);
                }

                _entries.Remove(name.Value);
                return ValueTask.FromResult(AdminOutcome.Done);
            }
        }
    }
}
