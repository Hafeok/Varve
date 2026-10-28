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
