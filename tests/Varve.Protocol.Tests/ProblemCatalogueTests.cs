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
using Varve.Events;
using Varve.Management;
using Varve.Management.Model;
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
    /// not exist; and a read that finds an extent gone, as a forgotten or an
    /// unannounced reader may (ADR 0160), needs a server that only reads a
    /// dataset in an object store, which is the Management plane's follower
    /// (#105). Removed from here by the slice that makes each emittable.
    /// </summary>
    private static readonly ImmutableArray<ProblemType> NotYetEmittable = [ProblemType.BelowArchiveHorizon, ProblemType.ReaderForgotten, ProblemType.ReadUnannouncedExtentGone];

    /// <summary>
    /// Types whose trigger holds state across requests, exercised in
    /// <c>GovernanceTests</c> (the live-tail bound) and <c>Varve.Server.Tests</c>
    /// (the concurrent-read limit, which is the host's rate limiter; and a
    /// writer superseded by a second server on the same object store, ADR 0136).
    /// </summary>
    private static readonly ImmutableArray<ProblemType> TestedElsewhere =
    [
        ProblemType.TooManyLiveTails, ProblemType.ServerBusy, ProblemType.WriterSuperseded, ProblemType.EventNotRendered,
        ProblemType.ManagementBehind, ProblemType.DatasetBehind,
    ];

    /// <summary>The types the management plane's endpoints emit, triggered by <see cref="every_problem_the_management_endpoints_emit_has_its_catalogued_shape"/>.</summary>
    private static readonly ImmutableArray<ProblemType> ManagementTriggered = [ProblemType.DeclarationInvalid, ProblemType.DatasetOpen, ProblemType.WriterMoved];

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
            new DatasetOptions
            {
                Clock = TimeProvider.System,
                Validators =
                [
                    new RefusesSecret(),
                    new EventCatalogueValidator(),

                    // ADR 0169's three types: shapes in a writable graph, over the default graph only.
                    new Varve.Shacl.Store.ShapesGate([new Varve.Shacl.Store.Model.ShapesBinding(
                        RdfTerm.Iri("http://ex/binding"u8), RdfTerm.Iri("http://ex/people"u8), GraphScope.Of([], [], DefaultGraphAccess.Included), Varve.Shacl.Store.Model.ValidationMode.Reject)]),
                ],
            }, P.Ct);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("d", dataset);

        // ADR 0167: a dataset whose event declarations moved refuses catalogue writes.
        Dataset frozen = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()),
            new DatasetOptions { Clock = TimeProvider.System, Validators = [new EventCatalogueFreeze(new DatasetName("frozen"))] }, P.Ct);
        datasets.Add("frozen", frozen);
        ProtocolLimits limits = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), new ByteCount(200), new ByteCount(2048), TimeSpan.FromSeconds(15))
        {
            MaxQueryMemory = new Varve.Sparql.Evaluation.MemoryBytes(1024),
            MaxAsOfDistance = 1,
        };
        GraphScope people = GraphScope.Of([RdfTerm.Iri("http://ex/people"u8)], [], DefaultGraphAccess.Included).WithGovernor(EventCatalogue.TryGetSourceGraph);
        TestAdministration administration = new();

        await using (dataset)
        await using (frozen)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(
            datasets,
            map: (app, o) =>
            {
                app.MapGroup("/datasets/{dataset}").MapVarveDataset(o);
                app.MapVarveManagement(o, null);
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
                [ProblemType.AgentRequired] = () => Put("manage/datasets/d/settings", "{\"defaultAccessScope\":\"Current\"}", "application/json"),
                [ProblemType.GraphNotWritable] = () => P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/other> { <http://ex/a> <http://ex/p> 1 } }"),
                [ProblemType.DatasetNotFound] = () => P.Get("datasets/nowhere/sparql?query=ASK%7B%7D"),
                [ProblemType.GraphNotFound] = () => P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/none"), "application/n-triples"),
                [ProblemType.PositionNotReached] = () => P.Query(Sparql, "ASK {}", asOf: "position:99"),
                [ProblemType.BeforeFirstCommit] = () => P.Query(Sparql, "ASK {}", asOf: "time:2000-01-01T00:00:00Z"),
                [ProblemType.MethodNotAllowed] = () => new HttpRequestMessage(HttpMethod.Delete, new Uri(Sparql, UriKind.Relative)),
                [ProblemType.NotAcceptable] = () => P.Query(Sparql, "ASK {}", accept: "image/png"),
                [ProblemType.Conflict] = () => P.Update(Sparql, "DELETE WHERE { ?s ?p ?o }", "\"" + dataset.Head.Value + "\""),
                [ProblemType.PreconditionFailed] = () => P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }", "\"77\""),
                [ProblemType.RequestTooLarge] = () => P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 200).Select(i => "<http://ex/s" + i + "> <http://ex/p> " + i + " . ")) + "}"),
                [ProblemType.UnsupportedMediaType] = () => new HttpRequestMessage(HttpMethod.Post, new Uri(Sparql, UriKind.Relative)) { Content = new StringContent("ASK {}", Encoding.UTF8, "text/plain") },
                [ProblemType.Rejected] = () => P.Update(Sparql, "INSERT DATA { <http://ex/secret> <http://ex/p> 1 }"),
                [ProblemType.EventDeclarationInvalid] = () => P.Update(Sparql, "INSERT DATA { GRAPH <" + Catalogue + "> { <http://ex/stray> <http://ex/p> 1 } }"),
                [ProblemType.EventCatalogueMoved] = () => P.Update("datasets/frozen/sparql", "INSERT DATA { GRAPH <" + Catalogue + "> { <http://ex/stray> <http://ex/p> 1 } }"),
                [ProblemType.EventSchemaIncompatible] = () => P.Update(Sparql,
                    "DELETE DATA { GRAPH <" + Catalogue + "> { <http://ex/types/T/at> <https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#dateTime> } } ; "
                    + "INSERT DATA { GRAPH <" + Catalogue + "> { <http://ex/types/T/at> <https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#string> } }"),
                [ProblemType.Unavailable] = () => Put("manage/datasets/broken/state", "{\"state\":\"open\"}", "application/json"),
                [ProblemType.ReadLimitExceeded] = () => P.Query(Sparql, "SELECT * { ?s ?p ?o } "),
                [ProblemType.MemoryLimitExceeded] = () => P.Query(Sparql, "SELECT * { ?s ?p ?o } ORDER BY ?s"),
                [ProblemType.AsOfDistanceExceeded] = () => P.Query(Sparql, "ASK {}", asOf: "position:2"),
                [ProblemType.ShapesViolated] = () => P.Update(Sparql, "INSERT DATA { <http://ex/shaped> <http://ex/q> \"x\" }"),
                [ProblemType.ShapesInvalid] = () => P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/people> { <http://ex/S2> <" + Sh + "targetNode> <http://ex/z> ; <" + Sh + "not> <http://ex/S2> } }"),
                [ProblemType.BindingNotIncremental] = () => P.Update(Sparql,
                    "INSERT DATA { GRAPH <http://ex/people> { <http://ex/S3> <" + Sh + "targetNode> <http://ex/z> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/q> ; <"
                    + Sh + "values> <http://ex/k> ; <" + Sh + "node> <http://ex/T> ] . <http://ex/T> <" + Sh + "nodeKind> <" + Sh + "IRI> } }"),
            };

            await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 20).Select(i => "<http://ex/s" + i + "> <http://ex/p> " + i + " . ")) + "}"), P.Ct);

            // The shapes the binding's triggers need: integers on ex:q at ex:shaped.
            Assert.Equal(System.Net.HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql,
                "INSERT DATA { GRAPH <http://ex/people> { <http://ex/S> <" + Sh + "targetNode> <http://ex/shaped> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/q> ; <"
                + Sh + "datatype> <http://www.w3.org/2001/XMLSchema#integer> ] } }"), P.Ct)).StatusCode);

            // A declaration the event triggers change, in the default graph's catalogue.
            Assert.Equal(System.Net.HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql,
                "INSERT DATA { GRAPH <" + Catalogue + "> { "
                + "<http://ex/types/T> a <https://w3id.org/varve/ns#EventType> ; <https://w3id.org/varve/ns#eventName> \"T\" ; "
                + "<https://w3id.org/varve/ns#eventClass> <http://ex/T> ; <https://w3id.org/varve/ns#eventVersion> 1 ; "
                + "<https://w3id.org/varve/ns#eventField> <http://ex/types/T/at> ; "
                + "<https://w3id.org/varve/ns#eventSelect> \"SELECT ?at WHERE { ?event <http://ex/at> ?at }\" . "
                + "<http://ex/types/T/at> a <https://w3id.org/varve/ns#EventField> ; <https://w3id.org/varve/ns#fieldName> \"at\" ; "
                + "<https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#dateTime> ; <https://w3id.org/varve/ns#fieldRequired> true } }"), P.Ct)).StatusCode);

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
            HashSet<string> covered = [.. triggers.Keys.Select(t => t.Value), .. ManagementTriggered.Select(t => t.Value), .. ServerEmitted.Select(t => t.Value), .. NotYetEmittable.Select(t => t.Value), .. TestedElsewhere.Select(t => t.Value)];
            Assert.Equal(ProblemCatalogue.All.Select(s => s.Type.Value).Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The management plane's problems (ADRs 0163, 0166, 0168): a declaration
    /// the code validator refuses, a delete of a dataset declared open, and a
    /// forwarded write that reaches an instance which no longer writes its
    /// dataset. Each has its catalogued shape and no other member.
    /// </summary>
    [Fact]
    public async Task every_problem_the_management_endpoints_emit_has_its_catalogued_shape()
    {
        Dataset system = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        Dataset moved = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("moved", moved);
        TestPlane plane = new(system);
        const string Declared = "<urn:y> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"y\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#Memory> .";

        await using (system)
        await using (moved)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(
            datasets,
            map: (app, o) =>
            {
                app.MapGroup("/datasets/{dataset}").MapVarveDataset(o);
                app.MapVarveManagement(o, plane);
            },
            writers: new MovedDirectory(),
            stopping: P.Ct))
        {
            Assert.Equal(System.Net.HttpStatusCode.Created, (await host.Client.SendAsync(Put("manage/datasets/y", Declared, "application/trig"), P.Ct)).StatusCode);
            Dictionary<ProblemType, Func<HttpRequestMessage>> triggers = new()
            {
                [ProblemType.DeclarationInvalid] = () => Put("manage/datasets/x", "<urn:x> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"x\" ; <https://w3id.org/varve/ns#erasureMode> \"y\" .", "application/trig"),
                [ProblemType.DatasetOpen] = () => new HttpRequestMessage(HttpMethod.Delete, new Uri("manage/datasets/y", UriKind.Relative)),
                [ProblemType.WriterMoved] = () =>
                {
                    HttpRequestMessage forwarded = P.Update("datasets/moved/sparql", "INSERT DATA { <http://ex/a> <http://ex/p> 1 }");
                    forwarded.Headers.TryAddWithoutValidation("Varve-Forwarded", "another-one");
                    return forwarded;
                },
            };

            Assert.Equal(ManagementTriggered.Select(t => t.Value).Order(StringComparer.Ordinal), triggers.Keys.Select(t => t.Value).Order(StringComparer.Ordinal));

            foreach ((ProblemType type, Func<HttpRequestMessage> trigger) in triggers)
            {
                ProblemShape shape = ProblemCatalogue.Of(type);
                HttpResponseMessage response = await host.Client.SendAsync(trigger(), P.Ct);
                string body = await response.Content.ReadAsStringAsync(P.Ct);
                Assert.True(shape.Status == (int)response.StatusCode, shape.Name + ": " + (int)response.StatusCode + " " + body);
                using JsonDocument problem = JsonDocument.Parse(body);
                Assert.Equal(type.Value, problem.RootElement.GetProperty("type").GetString());

                foreach (JsonProperty member in problem.RootElement.EnumerateObject())
                {
                    Assert.True(member.Name is "type" or "title" or "status" or "detail" or "instance" || shape.Members.Contains(member.Name), shape.Name + " carries an undeclared member " + member.Name);
                }
            }

            Assert.Equal(1L, system.Head.Value);
            Assert.Equal(0L, moved.Head.Value);
        }
    }

    private const string Catalogue = "https://w3id.org/varve/ns#events-catalogue/default";

    private const string Sh = "http://www.w3.org/ns/shacl#";

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

        public async ValueTask<CommitResult> ExecuteAsync(Dataset target, Update update, CommitMetadata metadata, UpdatePreconditions preconditions, CallerScope scope, CancellationToken cancellationToken)
        {
            if (preconditions.ExpectedPosition is not null && update.Operations[0] is DeleteWhere)
            {
                int n = Interlocked.Increment(ref _n);
                await dataset.CommitAsync(new CommitRequest().Assert(RdfTerm.Iri("http://ex/race"u8), RdfTerm.Iri("http://ex/p"u8), RdfTerm.Literal(Encoding.UTF8.GetBytes("r" + n))), cancellationToken);
            }

            return await inner.ExecuteAsync(target, update, metadata, preconditions, scope, cancellationToken);
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
    }

    /// <summary>The management plane over an in-memory system dataset (ADR 0163): the grants and the status at its head.</summary>
    private sealed class TestPlane(Dataset system) : IManagementPlane
    {
        public Dataset System => system;

        public GrantTable Table
        {
            get
            {
                using DatasetView view = system.Pin();
                return GrantTable.Compile(DeclarationReader.Read(view), new ManagementPosition(system.Head.Value), []);
            }
        }

        public ManagementStatus Status => new(ManagementRole.Writer, 0, new ManagementPosition(system.Head.Value), new ManagementPosition(system.Head.Value), BootstrapState.Absent, null);

        public ICommitValidator Validator { get; } = new ManagementValidator();
    }

    /// <summary>Another instance writes the dataset "moved"; this one writes the rest.</summary>
    private sealed class MovedDirectory : IWriterDirectory
    {
        public WriterName Instance { get; } = new("this-one");

        public ValueTask<WriterRoute> RouteAsync(WriterTarget target, CancellationToken cancellationToken) =>
            ValueTask.FromResult(target.Dataset is { Value: "moved" } ? new WriterRoute(WriterRouteKind.Remote, "http://127.0.0.1:9/", 7) : WriterRoute.Local);

        public void Stale(WriterTarget target)
        {
        }
    }
}
