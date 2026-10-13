// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Turtle;
using Varve.Turtle.Model;

namespace Varve.Protocol.Client;

public sealed partial class SparqlHttpClient
{
    /// <summary>The graph migrations are recorded in (ADR 0130): a system graph, ordinary data under ordinary grants.</summary>
    public const string SetupGraph = "https://w3id.org/varve/ns#setup";

    private const string VarveNs = "https://w3id.org/varve/ns#";

    /// <summary>
    /// Creates the dataset, as <c>PUT /datasets/{name}</c> does: <c>201</c> when it
    /// was created, <c>204</c> when it exists in that storage, <c>409</c> when it
    /// exists in another (ADR 0118); a server admin's call.
    /// </summary>
    public Task<HttpResponseMessage> PutDatasetAsync(SetupStorage storage, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, new Uri(Dataset.AbsoluteUri.TrimEnd('/')))
        {
            Content = new StringContent(storage == SetupStorage.Memory ? "{\"storage\":\"Memory\"}" : "{\"storage\":\"File\"}", Encoding.UTF8, "application/json"),
        }, cancellationToken);

    /// <summary>Replaces a graph by the Graph Store Protocol (<c>PUT</c>): the default graph for a null <paramref name="graph"/>.</summary>
    public Task<HttpResponseMessage> PutGraphAsync(string? graph, byte[] content, string mediaType, CancellationToken cancellationToken) =>
        GraphWriteAsync(HttpMethod.Put, graph, content, mediaType, cancellationToken);

    /// <summary>Merges into a graph by the Graph Store Protocol (<c>POST</c>): the default graph for a null <paramref name="graph"/>.</summary>
    public Task<HttpResponseMessage> PostGraphAsync(string? graph, byte[] content, string mediaType, CancellationToken cancellationToken) =>
        GraphWriteAsync(HttpMethod.Post, graph, content, mediaType, cancellationToken);

    /// <summary>Empties a graph by the Graph Store Protocol (<c>DELETE</c>): the default graph for a null <paramref name="graph"/>.</summary>
    public Task<HttpResponseMessage> DeleteGraphAsync(string? graph, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, GraphAddress(graph)), cancellationToken);

    /// <summary>
    /// Makes the dataset what <paramref name="setup"/> declares (ADR 0130), in
    /// order: the dataset, created when a read answers <c>404</c>; the settings,
    /// put under <c>If-Match</c> when they differ; each baseline source, by the
    /// Graph Store Protocol; each migration not yet recorded in
    /// <see cref="SetupGraph"/>, with its record, as one request under
    /// <c>If-Match</c>. A second apply of the same setup commits nothing.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="setup"/> names another dataset than this client's.</exception>
    /// <exception cref="SetupException">A step failed: a baseline holds a blank node or does not parse, or the server refused. The steps before it stand; it committed nothing.</exception>
    public async Task<SetupResult> ApplyAsync(DatasetSetup setup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        string name = Uri.UnescapeDataString(Dataset.AbsolutePath.TrimEnd('/')[(Dataset.AbsolutePath.TrimEnd('/').LastIndexOf('/') + 1)..]);

        if (!string.Equals(name, setup.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("The setup is of dataset " + setup.Name + ", and this client addresses " + name + ".", nameof(setup));
        }

        // Every baseline is read and checked before anything is sent.
        List<(BaselineSource Source, byte[] Content)> baselines = [];

        foreach (BaselineSource source in setup.Baselines)
        {
            byte[] content = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            CheckBaseline(source, content);
            baselines.Add((source, content));
        }

        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (SetupMigration migration in setup.Migrations)
        {
            if (string.IsNullOrEmpty(migration.Id) || !ids.Add(migration.Id))
            {
                throw new SetupException("migration " + migration.Id, "a migration's id is unique within its setup and not empty", null, null);
            }
        }

        bool created = await EnsureDatasetAsync(setup, cancellationToken).ConfigureAwait(false);
        bool settingsChanged = setup.Settings is not null && await EnsureSettingsAsync(setup.Settings, cancellationToken).ConfigureAwait(false);
        long position = await HeadForSetupAsync("baseline", cancellationToken).ConfigureAwait(false);
        List<string> committed = [];

        foreach ((BaselineSource source, byte[] content) in baselines)
        {
            // A baseline is added, never put (ADR 0130, amended on the plan's
            // review): a PUT would retract, on every apply and every retry,
            // whatever the application wrote to the graph since. A 409 or 412
            // means another writer came first, and an addition, being
            // idempotent, is sent again.
            for (int attempt = 0; ; attempt++)
            {
                using HttpResponseMessage written = await PostGraphAsync(source.Graph, content, source.MediaType, cancellationToken).ConfigureAwait(false);

                if (Moved(written) && attempt < MaxRetries)
                {
                    position = await HeadForSetupAsync("baseline " + source.Name, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await EnsureSetupStepAsync(written, "baseline " + source.Name, cancellationToken).ConfigureAwait(false);

                if (PositionOf(written) is long after && after > position)
                {
                    committed.Add(source.Name);
                    position = after;
                }

                break;
            }
        }

        List<string> applied = await MigrateAsync(setup.Migrations, cancellationToken).ConfigureAwait(false);
        position = Math.Max(position, await HeadForSetupAsync("migrations", cancellationToken).ConfigureAwait(false));
        return new SetupResult(created, settingsChanged, committed, applied, position);
    }

    private async Task<bool> EnsureDatasetAsync(DatasetSetup setup, CancellationToken cancellationToken)
    {
        using (HttpResponseMessage probe = await QueryAsync("ASK {}", "application/sparql-results+json", null, cancellationToken).ConfigureAwait(false))
        {
            if (probe.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSetupStepAsync(probe, "dataset", cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        using HttpResponseMessage created = await PutDatasetAsync(setup.Storage, cancellationToken).ConfigureAwait(false);
        await EnsureSetupStepAsync(created, "dataset", cancellationToken).ConfigureAwait(false);
        return created.StatusCode == HttpStatusCode.Created;
    }

    // The settings are desired state, last writer wins (ADR 0130): each key
    // the setup declares is compared with the dataset's as a JSON value, and
    // when one differs the declared keys alone are merged in (RFC 7396) under
    // If-Match. A manual change to a declared key is overwritten on the next
    // apply; a key the setup does not declare is never touched. A 409 or 412
    // means another writer came first: the settings are read and compared
    // again, and the declared values applied again.
    private async Task<bool> EnsureSettingsAsync(string settings, CancellationToken cancellationToken)
    {
        JsonObject wanted;

        try
        {
            wanted = JsonNode.Parse(settings) as JsonObject
                ?? throw new SetupException("settings", "the settings are a JSON object", null, null);
        }
        catch (JsonException error)
        {
            throw new SetupException("settings", "the settings are not JSON: " + error.Message, null, null);
        }

        if (wanted.Count == 0)
        {
            return false;
        }

        for (int attempt = 0; ; attempt++)
        {
            using HttpResponseMessage current = await SettingsAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSetupStepAsync(current, "settings", cancellationToken).ConfigureAwait(false);
            JsonObject? held = JsonNode.Parse(await current.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject;
            bool differs = false;

            foreach (KeyValuePair<string, JsonNode?> declared in wanted)
            {
                if (held is null || !held.TryGetPropertyValue(declared.Key, out JsonNode? value) || !JsonNode.DeepEquals(value, declared.Value))
                {
                    differs = true;
                    break;
                }
            }

            if (!differs)
            {
                return false;
            }

            using HttpResponseMessage patched = await PatchSettingsAsync(wanted.ToJsonString(), PositionOf(current), cancellationToken).ConfigureAwait(false);

            if (Moved(patched) && attempt < MaxRetries)
            {
                continue;
            }

            await EnsureSetupStepAsync(patched, "settings", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    // Each migration not yet recorded, with its record, as one request under
    // If-Match at the position the records were read at. A 409 or 412 means
    // another writer moved the dataset first (ADR 0094): the records are read
    // again and the migrations go on from what they say, so two appliers
    // starting together apply each migration once.
    private async Task<List<string>> MigrateAsync(IList<SetupMigration> migrations, CancellationToken cancellationToken)
    {
        List<string> applied = [];

        if (migrations.Count == 0)
        {
            return applied;
        }

        (HashSet<string> recorded, long position) = await RecordedAsync(cancellationToken).ConfigureAwait(false);
        int retries = 0;

        for (int i = 0; i < migrations.Count; i++)
        {
            SetupMigration migration = migrations[i];

            if (recorded.Contains(migration.Id))
            {
                continue;
            }

            using HttpResponseMessage response = await UpdateAsync(WithRecord(migration), position, cancellationToken).ConfigureAwait(false);

            if (Moved(response) && retries++ < MaxRetries)
            {
                (recorded, position) = await RecordedAsync(cancellationToken).ConfigureAwait(false);
                i--;
                continue;
            }

            await EnsureSetupStepAsync(response, "migration " + migration.Id, cancellationToken).ConfigureAwait(false);
            applied.Add(migration.Id);
            recorded.Add(migration.Id);
            position = PositionOf(response) ?? position;
        }

        return applied;
    }

    // How often a step reads again after another writer came first, before
    // the conflict is the caller's to see.
    private const int MaxRetries = 16;

    private static bool Moved(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed;

    private async Task<(HashSet<string> Recorded, long Position)> RecordedAsync(CancellationToken cancellationToken)
    {
        const string Query = "SELECT ?id WHERE { GRAPH <" + SetupGraph + "> { ?migration a <" + VarveNs + "Migration> ; <" + VarveNs + "migrationId> ?id } }";
        using HttpResponseMessage response = await QueryAsync(Query, "application/sparql-results+json", null, cancellationToken).ConfigureAwait(false);
        await EnsureSetupStepAsync(response, "migrations", cancellationToken).ConfigureAwait(false);
        HashSet<string> recorded = new(StringComparer.Ordinal);
        using JsonDocument results = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));

        foreach (JsonElement binding in results.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            if (binding.TryGetProperty("id", out JsonElement id) && id.GetProperty("value").GetString() is { } value)
            {
                recorded.Add(value);
            }
        }

        return (recorded, PositionOf(response) ?? 0);
    }

    // The migration's update and, after it, the INSERT DATA of its record:
    // one request, so one commit or none (ADR 0057).
    private static string WithRecord(SetupMigration migration)
    {
        string update = migration.Update.TrimEnd();

        while (update.EndsWith(';'))
        {
            update = update[..^1].TrimEnd();
        }

        StringBuilder text = new(update);
        text.Append(" ;\nINSERT DATA { GRAPH <").Append(SetupGraph).Append("> { <").Append(SetupGraph).Append("/migration/")
            .Append(Uri.EscapeDataString(migration.Id)).Append("> a <").Append(VarveNs).Append("Migration> ; <").Append(VarveNs).Append("migrationId> \"");

        foreach (char c in migration.Id)
        {
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                _ => c.ToString(),
            });
        }

        return text.Append("\" } }").ToString();
    }

    private async Task<long> HeadForSetupAsync(string step, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await QueryAsync("ASK {}", "application/sparql-results+json", null, cancellationToken).ConfigureAwait(false);
        await EnsureSetupStepAsync(response, step, cancellationToken).ConfigureAwait(false);
        return PositionOf(response) ?? 0;
    }

    private static async Task EnsureSetupStepAsync(HttpResponseMessage response, string step, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string problem = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new SetupException(step, "the server refused", (int)response.StatusCode, problem);
    }

    // A baseline is read here with the reader for its media type, so that one
    // holding a blank node is refused before anything is sent (ADR 0130), and
    // one that does not parse is refused with what the reader said.
    private static void CheckBaseline(BaselineSource source, byte[] content)
    {
        string? blank = null;
        string? failure;
        ReadOnlyMemory<byte> baseIri = Encoding.UTF8.GetBytes(source.Graph ?? "urn:varve:setup:" + source.Name);

        void Look(in QuadView quad)
        {
            blank ??= Blank(quad.Subject.Materialise()) ?? Blank(quad.Object.Materialise());
        }

        switch (MediaTypeHeaderValue.TryParse(source.MediaType, out MediaTypeHeaderValue? type) ? type.MediaType : null)
        {
            case "text/turtle":
            {
                string? at = null;
                bool parsed = TurtleParser.Parse(content, Look, new TurtleOptions { Syntax = RdfSyntax.Turtle, BaseIri = baseIri, OnError = (in ParseError error) => Stop(error, ref at) }).Succeeded;
                failure = parsed ? null : at ?? "the document does not parse";
                break;
            }

            case "application/n-triples":
            {
                string? at = null;
                bool parsed = NQuadsParser.Parse(content, Look, new ParseOptions { Syntax = RdfSyntax.NTriples, OnError = (in ParseError error) => Stop(error, ref at) }).Succeeded;
                failure = parsed ? null : at ?? "the document does not parse";
                break;
            }

            case "application/rdf+xml":
            {
                RdfXmlParseResult result = RdfXmlParser.Parse(content, Look, new RdfXmlOptions { BaseIri = baseIri });
                failure = result.Succeeded ? null : result.Error?.ToString();
                break;
            }

            case "application/ld+json":
            {
                JsonLdResult result = JsonLdParser.Parse(content, Look, new JsonLdOptions { BaseIri = baseIri });
                failure = result.Succeeded ? null : result.Error?.ToString();
                break;
            }

            default:
                throw new SetupException("baseline " + source.Name, "its media type, " + source.MediaType + ", is not text/turtle, application/n-triples, application/rdf+xml or application/ld+json", null, null);
        }

        if (failure is not null)
        {
            throw new SetupException("baseline " + source.Name, "it does not parse as " + source.MediaType + ": " + failure, null, null);
        }

        if (blank is not null)
        {
            throw new SetupException("baseline " + source.Name,
                "it holds the blank node " + blank + "; a blank node is a new node on every write (ADR 0098), so the baseline would commit each time it is applied. "
                + "Give the node an IRI: one of the application's own, or a skolem IRI such as https://example.org/.well-known/genid/… (RDF 1.1 Concepts §3.5)",
                null, null);
        }
    }

    private static ErrorAction Stop(in ParseError error, ref string? at)
    {
        at ??= error.Kind.ToString() + " at line " + error.Position.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", column " + error.Position.Column.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ErrorAction.Stop;
    }

    // The first blank node in a term, a triple term's components included.
    private static string? Blank(RdfTerm? term) => term switch
    {
        null => null,
        { Kind: RdfTermKind.BlankNode } => "_:" + Encoding.UTF8.GetString(term.Lexical),
        { Kind: RdfTermKind.TripleTerm } => Blank(term.Subject) ?? Blank(term.Object),
        _ => null,
    };

    private Task<HttpResponseMessage> GraphWriteAsync(HttpMethod method, string? graph, byte[] content, string mediaType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        ByteArrayContent body = new(content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return SendAsync(new HttpRequestMessage(method, GraphAddress(graph)) { Content = body }, cancellationToken);
    }

    private Uri GraphAddress(string? graph) =>
        new(Dataset, graph is null ? "graphs?default" : "graphs?graph=" + Uri.EscapeDataString(graph));
}
