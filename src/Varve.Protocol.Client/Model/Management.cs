// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// One dataset as <c>GET /manage/datasets</c> lists it (ADR 0163): its name,
/// state, storage, why the server holds it, this instance's role, and its
/// head. Each is null when the server did not send it.
/// </summary>
public sealed class DatasetEntry
{
    private DatasetEntry(DatasetName name, DatasetState? state, StorageKind? storage, DatasetOrigin? origin, InstanceRole? role, FeedPosition? head, IReadOnlyDictionary<string, JsonElement> members)
    {
        Name = name;
        State = state;
        Storage = storage;
        Origin = origin;
        Role = role;
        Head = head;
        Members = members;
    }

    /// <summary>The dataset's name.</summary>
    public DatasetName Name { get; }

    /// <summary>Open, closed or failed.</summary>
    public DatasetState? State { get; }

    /// <summary>Where its log is kept.</summary>
    public StorageKind? Storage { get; }

    /// <summary>Why this server holds it.</summary>
    public DatasetOrigin? Origin { get; }

    /// <summary>What this instance is for it.</summary>
    public InstanceRole? Role { get; }

    /// <summary>Its head, when this instance holds it.</summary>
    public FeedPosition? Head { get; }

    /// <summary>Every other member: <c>id</c>, and what a newer server adds.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    internal static DatasetEntry? Of(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || Json.Text(entry, "name") is not { } name)
        {
            return null;
        }

        return new DatasetEntry(
            new DatasetName(name),
            Json.Text(entry, "state") is { } state ? new DatasetState(state) : null,
            Json.Text(entry, "storage") is { } storage ? new StorageKind(storage) : null,
            Json.Text(entry, "origin") is { } origin ? new DatasetOrigin(origin) : null,
            Json.Text(entry, "role") is { } role ? new InstanceRole(role) : null,
            Json.Count(entry, "head") is { } head ? new FeedPosition(head) : null,
            Json.Rest(entry, "name", "state", "storage", "origin", "role", "head"));
    }
}

/// <summary>The datasets <c>GET /manage/datasets</c> lists, in its order.</summary>
public sealed class DatasetList
{
    private DatasetList(IReadOnlyList<DatasetEntry> datasets) => Datasets = datasets;

    /// <summary>The datasets.</summary>
    public IReadOnlyList<DatasetEntry> Datasets { get; }

    internal static DatasetList Of(JsonElement root)
    {
        List<DatasetEntry> datasets = [];

        if (root.TryGetProperty("datasets", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in listed.EnumerateArray())
            {
                if (DatasetEntry.Of(entry) is { } dataset)
                {
                    datasets.Add(dataset);
                }
            }
        }

        return new DatasetList(datasets);
    }
}

/// <summary>
/// A dataset's lifecycle as <c>GET /manage/datasets/{d}/state</c> answers it
/// (ADR 0165): its state, this instance's role, and why, when it failed.
/// </summary>
public sealed class DatasetLifecycle
{
    private DatasetLifecycle(DatasetState? state, InstanceRole? role, string? reason, IReadOnlyDictionary<string, JsonElement> members)
    {
        State = state;
        Role = role;
        Reason = reason;
        Members = members;
    }

    /// <summary>Open, closed or failed.</summary>
    public DatasetState? State { get; }

    /// <summary>What this instance is for it.</summary>
    public InstanceRole? Role { get; }

    /// <summary>Why it failed, as the server wrote it.</summary>
    [DesignDecision(typeof(TypedReadsInTheProtocolClient.ProblemTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Reason { get; }

    /// <summary>Every other member: what a newer server adds (#97's warm standby and epoch).</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    internal static DatasetLifecycle Of(JsonElement root) => new(
        Json.Text(root, "state") is { } state ? new DatasetState(state) : null,
        Json.Text(root, "role") is { } role ? new InstanceRole(role) : null,
        Json.Text(root, "reason"),
        Json.Rest(root, "state", "role", "reason"));
}

/// <summary>
/// A dataset's status as <c>GET /manage/datasets/{d}/status</c> answers it
/// (ADRs 0106, 0113): its head, durability, state, default projection,
/// settings and checkpoints.
/// </summary>
public sealed class DatasetStatus
{
    private DatasetStatus(FeedPosition? head, Durability? durability, bool failed, DatasetState? state, ProjectionStatus? projection, IReadOnlyDictionary<string, JsonElement> settings, IReadOnlyList<FeedPosition> checkpoints, IReadOnlyDictionary<string, JsonElement> members)
    {
        Head = head;
        Durability = durability;
        Failed = failed;
        State = state;
        Projection = projection;
        Settings = settings;
        Checkpoints = checkpoints;
        Members = members;
    }

    /// <summary>The dataset's head.</summary>
    public FeedPosition? Head { get; }

    /// <summary>How its writes reach durable storage.</summary>
    public Durability? Durability { get; }

    /// <summary>Whether it failed.</summary>
    public bool Failed { get; }

    /// <summary>Open, closed or failed.</summary>
    public DatasetState? State { get; }

    /// <summary>The default projection, the quad indexes.</summary>
    public ProjectionStatus? Projection { get; }

    /// <summary>The dataset's settings, each as the server wrote it.</summary>
    public IReadOnlyDictionary<string, JsonElement> Settings { get; }

    /// <summary>The positions it has checkpoints at, as listed.</summary>
    public IReadOnlyList<FeedPosition> Checkpoints { get; }

    /// <summary>Every other member: <c>name</c>, <c>id</c>, an event move, and what a newer server adds.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    internal static DatasetStatus Of(JsonElement root)
    {
        Dictionary<string, JsonElement> settings = new(StringComparer.Ordinal);

        if (root.TryGetProperty("settings", out JsonElement declared) && declared.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty setting in declared.EnumerateObject())
            {
                settings[setting.Name] = setting.Value.Clone();
            }
        }

        List<FeedPosition> checkpoints = [];

        if (root.TryGetProperty("checkpoints", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement checkpoint in listed.EnumerateArray())
            {
                if (checkpoint.ValueKind == JsonValueKind.Number && checkpoint.TryGetInt64(out long at) && at >= 0)
                {
                    checkpoints.Add(new FeedPosition(at));
                }
            }
        }

        return new DatasetStatus(
            Json.Count(root, "head") is { } head ? new FeedPosition(head) : null,
            Json.Text(root, "durability") is { } durability ? new Durability(durability) : null,
            root.TryGetProperty("failed", out JsonElement failed) && failed.ValueKind == JsonValueKind.True,
            Json.Text(root, "state") is { } state ? new DatasetState(state) : null,
            root.TryGetProperty("projection", out JsonElement projection) && projection.ValueKind == JsonValueKind.Object ? ProjectionStatus.Of(projection) : null,
            settings,
            checkpoints,
            Json.Rest(root, "head", "durability", "failed", "state", "projection", "settings", "checkpoints"));
    }
}

/// <summary>A projection's progress: the position it has reached, its lag behind the head, and whether it failed.</summary>
public sealed class ProjectionStatus
{
    private ProjectionStatus(FeedPosition? position, PositionLag? lag, bool failed)
    {
        Position = position;
        Lag = lag;
        Failed = failed;
    }

    /// <summary>The position it has reached.</summary>
    public FeedPosition? Position { get; }

    /// <summary>How far it is behind the head.</summary>
    public PositionLag? Lag { get; }

    /// <summary>Whether it failed.</summary>
    public bool Failed { get; }

    internal static ProjectionStatus Of(JsonElement value) => new(
        Json.Count(value, "position") is { } position ? new FeedPosition(position) : null,
        Json.Count(value, "lag") is { } lag ? new PositionLag(lag) : null,
        value.TryGetProperty("failed", out JsonElement failed) && failed.ValueKind == JsonValueKind.True);
}

/// <summary>
/// The management plane on the instance asked, as <c>GET /manage/system</c>
/// answers it (ADR 0163 point 13): its role for the system dataset, the
/// writer's epoch, the position applied and the tail, the lag, the bootstrap
/// operators' state, and the system writer's address when this instance is
/// not it.
/// </summary>
public sealed class SystemPlane
{
    private SystemPlane(InstanceRole? role, WriterEpoch? epoch, FeedPosition? position, FeedPosition? tail, PositionLag? lag, BootstrapState? bootstrap, Uri? writer, IReadOnlyDictionary<string, JsonElement> members)
    {
        Role = role;
        Epoch = epoch;
        Position = position;
        Tail = tail;
        Lag = lag;
        Bootstrap = bootstrap;
        Writer = writer;
        Members = members;
    }

    /// <summary>Writer, follower or superseded, for the system dataset.</summary>
    public InstanceRole? Role { get; }

    /// <summary>The system dataset's writer epoch.</summary>
    public WriterEpoch? Epoch { get; }

    /// <summary>The management position this instance has applied.</summary>
    public FeedPosition? Position { get; }

    /// <summary>The system dataset's tail.</summary>
    public FeedPosition? Tail { get; }

    /// <summary>How far the applied position is behind the tail.</summary>
    public PositionLag? Lag { get; }

    /// <summary>Whether the bootstrap operators match the recorded set.</summary>
    public BootstrapState? Bootstrap { get; }

    /// <summary>The system writer's address, when this instance is not it.</summary>
    public Uri? Writer { get; }

    /// <summary>Every other member: what a newer server adds.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    internal static SystemPlane Of(JsonElement root) => new(
        Json.Text(root, "role") is { } role ? new InstanceRole(role) : null,
        Json.Count(root, "epoch") is { } epoch ? new WriterEpoch(epoch) : null,
        Json.Count(root, "position") is { } position ? new FeedPosition(position) : null,
        Json.Count(root, "tail") is { } tail ? new FeedPosition(tail) : null,
        Json.Count(root, "lag") is { } lag ? new PositionLag(lag) : null,
        Json.Text(root, "bootstrap") is { } bootstrap ? new BootstrapState(bootstrap) : null,
        Uri.TryCreate(Json.Text(root, "writer"), UriKind.Absolute, out Uri? writer) ? writer : null,
        Json.Rest(root, "role", "epoch", "position", "tail", "lag", "bootstrap", "writer"));
}

/// <summary>
/// Management graphs as served (ADR 0166): TriG, UTF-8, exactly as the server
/// wrote it, and its version, the management position its <c>ETag</c> names.
/// </summary>
public sealed class Declaration
{
    internal Declaration(ReadOnlyMemory<byte> content, FeedPosition? version)
    {
        Content = content;
        Version = version;
    }

    /// <summary>The TriG, UTF-8.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>The version the server tagged it with, when it did.</summary>
    public FeedPosition? Version { get; }
}

/// <summary>
/// Where the management plane places each dataset (ADR 0166's
/// <c>varve:placedOn</c>). A dataset that names no instance is absent: it is
/// placed on the system writer.
/// </summary>
public sealed class PlacementList
{
    private PlacementList(IReadOnlyDictionary<DatasetName, InstanceName> placements) => Placements = placements;

    /// <summary>Each placed dataset's instance.</summary>
    public IReadOnlyDictionary<DatasetName, InstanceName> Placements { get; }

    // SPARQL 1.1 Query Results JSON: bindings of ?name and ?instance.
    internal static PlacementList Of(JsonElement root)
    {
        Dictionary<DatasetName, InstanceName> placements = [];

        if (root.TryGetProperty("results", out JsonElement results) && results.TryGetProperty("bindings", out JsonElement bindings) && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement binding in bindings.EnumerateArray())
            {
                if (Bound(binding, "name") is { } name && Bound(binding, "instance") is { } instance)
                {
                    placements[new DatasetName(name)] = new InstanceName(instance);
                }
            }
        }

        return new PlacementList(placements);
    }

    private static string? Bound(JsonElement binding, string variable) =>
        binding.ValueKind == JsonValueKind.Object && binding.TryGetProperty(variable, out JsonElement term) && term.ValueKind == JsonValueKind.Object ? Json.Text(term, "value") : null;
}

/// <summary>
/// A dataset's shapes bindings as its declaration graph states them (ADR
/// 0170): each binding's IRI and the shapes graph it binds, a named graph of
/// the same dataset. Read through the management plane's SPARQL endpoint.
/// </summary>
public sealed class ShapesBindingList
{
    private ShapesBindingList(IReadOnlyList<ShapesBindingEntry> bindings) => Bindings = bindings;

    /// <summary>The bindings, ordered by their IRI.</summary>
    public IReadOnlyList<ShapesBindingEntry> Bindings { get; }

    // SPARQL 1.1 Query Results JSON: bindings of ?binding and ?graph.
    internal static ShapesBindingList Of(JsonElement root)
    {
        List<ShapesBindingEntry> bindings = [];

        if (root.TryGetProperty("results", out JsonElement results) && results.TryGetProperty("bindings", out JsonElement rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (Bound(row, "binding") is { } binding && Bound(row, "graph") is { } graph
                    && Uri.TryCreate(binding, UriKind.Absolute, out Uri? bindingIri) && Uri.TryCreate(graph, UriKind.Absolute, out Uri? graphIri))
                {
                    bindings.Add(new ShapesBindingEntry(bindingIri, graphIri));
                }
            }
        }

        bindings.Sort((x, y) => string.CompareOrdinal(x.Binding.OriginalString, y.Binding.OriginalString));
        return new ShapesBindingList(bindings);
    }

    private static string? Bound(JsonElement row, string variable) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(variable, out JsonElement term) && term.ValueKind == JsonValueKind.Object ? Json.Text(term, "value") : null;
}

/// <summary>One shapes binding: its IRI and the shapes graph it binds.</summary>
public sealed class ShapesBindingEntry
{
    internal ShapesBindingEntry(Uri binding, Uri shapesGraph)
    {
        Binding = binding;
        ShapesGraph = shapesGraph;
    }

    /// <summary>The binding node's IRI.</summary>
    public Uri Binding { get; }

    /// <summary>The shapes graph it binds, a named graph of the dataset.</summary>
    public Uri ShapesGraph { get; }
}

/// <summary>
/// A range of commits as the line format carries it (ADR 0118): the records in
/// order, and where the next page starts when the server paged the range
/// (<c>Link: rel="next"</c>, ADR 0114).
/// </summary>
public sealed class CommitRange
{
    internal CommitRange(IReadOnlyList<FeedRecord> commits, FeedPosition? next)
    {
        Commits = commits;
        Next = next;
    }

    /// <summary>The records, oldest first.</summary>
    public IReadOnlyList<FeedRecord> Commits { get; }

    /// <summary>The position the next page starts after, or null when this is the last.</summary>
    public FeedPosition? Next { get; }
}

/// <summary>
/// A dataset's grants as <c>GET /manage/datasets/{d}/grants</c> answers them
/// (ADRs 0107, 0166), at the management position they were read at.
/// </summary>
public sealed class GrantList
{
    private GrantList(FeedPosition? position, IReadOnlyList<GrantEntry> grants)
    {
        Position = position;
        Grants = grants;
    }

    /// <summary>The management position the grants were read at.</summary>
    public FeedPosition? Position { get; }

    /// <summary>The grants, in the order the server lists them.</summary>
    public IReadOnlyList<GrantEntry> Grants { get; }

    internal static GrantList Of(JsonElement root)
    {
        List<GrantEntry> grants = [];

        if (root.TryGetProperty("grants", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement grant in listed.EnumerateArray())
            {
                if (grant.ValueKind == JsonValueKind.Object)
                {
                    grants.Add(GrantEntry.Of(grant));
                }
            }
        }

        return new GrantList(Json.Count(root, "position") is { } position ? new FeedPosition(position) : null, grants);
    }
}

/// <summary>One grant: who, what permission, and over which graphs.</summary>
public sealed class GrantEntry
{
    private GrantEntry(Uri? grant, GrantRight? permission, GranteeKind? granteeKind, ClaimValue? grantee, ClaimType? granteeClaimType, GraphScope scope, IReadOnlyDictionary<string, JsonElement> members)
    {
        Grant = grant;
        Permission = permission;
        GranteeKind = granteeKind;
        Grantee = grantee;
        GranteeClaimType = granteeClaimType;
        Scope = scope;
        Members = members;
    }

    /// <summary>The grant's IRI in its declaration.</summary>
    public Uri? Grant { get; }

    /// <summary>What it allows.</summary>
    public GrantRight? Permission { get; }

    /// <summary>Whether the grantee is an agent, a role or a group.</summary>
    public GranteeKind? GranteeKind { get; }

    /// <summary>The value the grantee is matched by.</summary>
    public ClaimValue? Grantee { get; }

    /// <summary>The claim a group grantee's value is read from; null for an agent or a role.</summary>
    public ClaimType? GranteeClaimType { get; }

    /// <summary>The graphs it covers.</summary>
    public GraphScope Scope { get; }

    /// <summary>Every other member: what a newer server adds.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    internal static GrantEntry Of(JsonElement grant)
    {
        JsonElement grantee = grant.TryGetProperty("grantee", out JsonElement g) && g.ValueKind == JsonValueKind.Object ? g : default;
        return new GrantEntry(
            Uri.TryCreate(Json.Text(grant, "grant"), UriKind.Absolute, out Uri? iri) ? iri : null,
            Json.Text(grant, "permission") is { } permission ? new GrantRight(permission) : null,
            grantee.ValueKind == JsonValueKind.Object && Json.Text(grantee, "kind") is { } kind ? new GranteeKind(kind) : null,
            grantee.ValueKind == JsonValueKind.Object && Json.Text(grantee, "value") is { } value ? new ClaimValue(value) : null,
            grantee.ValueKind == JsonValueKind.Object && Json.Text(grantee, "claimType") is { Length: > 0 } claimType ? new ClaimType(claimType) : null,
            GraphScope.Of(grant.TryGetProperty("graphs", out JsonElement graphs) ? graphs : default),
            Json.Rest(grant, "grant", "permission", "grantee", "graphs"));
    }
}

/// <summary>
/// The graphs a grant covers (ADR 0107): all of them, the default graph, named
/// graphs, graphs under prefixes, and graphs from templates.
/// </summary>
public sealed class GraphScope
{
    private GraphScope(bool all, bool defaultGraph, IReadOnlyList<Uri> graphs, IReadOnlyList<Uri> prefixes, IReadOnlyList<GraphTemplate> templates, IReadOnlyDictionary<GraphTemplate, TemplateMatch> matches)
    {
        All = all;
        DefaultGraph = defaultGraph;
        Graphs = graphs;
        Prefixes = prefixes;
        Templates = templates;
        TemplateMatches = matches;
    }

    /// <summary>Every graph of the dataset.</summary>
    public bool All { get; }

    /// <summary>The default graph.</summary>
    public bool DefaultGraph { get; }

    /// <summary>Named graphs, each by IRI.</summary>
    public IReadOnlyList<Uri> Graphs { get; }

    /// <summary>Every graph whose IRI starts with one of these.</summary>
    public IReadOnlyList<Uri> Prefixes { get; }

    /// <summary>Graphs from templates, filled from the caller's token.</summary>
    public IReadOnlyList<GraphTemplate> Templates { get; }

    /// <summary>How each template matches, where the server said: exactly, or as a prefix.</summary>
    public IReadOnlyDictionary<GraphTemplate, TemplateMatch> TemplateMatches { get; }

    internal static GraphScope Of(JsonElement graphs)
    {
        bool object_ = graphs.ValueKind == JsonValueKind.Object;
        Dictionary<GraphTemplate, TemplateMatch> matches = [];
        return new GraphScope(
            object_ && graphs.TryGetProperty("all", out JsonElement all) && all.ValueKind == JsonValueKind.True,
            object_ && graphs.TryGetProperty("defaultGraph", out JsonElement d) && d.ValueKind == JsonValueKind.True,
            Iris(graphs, "graphs"),
            Iris(graphs, "prefixes"),
            TemplatesOf(graphs, matches),
            matches);
    }

    // A template is an object, {template, match}.
    private static List<GraphTemplate> TemplatesOf(JsonElement graphs, Dictionary<GraphTemplate, TemplateMatch> matches)
    {
        List<GraphTemplate> templates = [];

        if (graphs.ValueKind == JsonValueKind.Object && graphs.TryGetProperty("templates", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && Json.Text(item, "template") is { Length: > 0 } text)
                {
                    GraphTemplate template = new(text);
                    templates.Add(template);

                    if (Json.Text(item, "match") is { Length: > 0 } match)
                    {
                        matches[template] = new TemplateMatch(match);
                    }
                }
            }
        }

        return templates;
    }

    private static List<Uri> Iris(JsonElement graphs, string name) =>
        Texts(graphs, name).ConvertAll(text => Uri.TryCreate(text, UriKind.Absolute, out Uri? iri) ? iri : null).FindAll(iri => iri is not null)!;

    private static List<string> Texts(JsonElement graphs, string name)
    {
        List<string> texts = [];

        if (graphs.ValueKind == JsonValueKind.Object && graphs.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                {
                    texts.Add(text);
                }
            }
        }

        return texts;
    }
}

/// <summary>
/// A write the server accepted (ADR 0203): no body of its own; the result's
/// facts carry the status and the positions it was committed at.
/// </summary>
public sealed class Acknowledgement
{
    internal Acknowledgement()
    {
    }
}
