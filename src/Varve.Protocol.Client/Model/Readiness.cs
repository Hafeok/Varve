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
/// An instance's readiness as <c>/health/ready</c> answers it (ADRs 0113, 0163
/// point 13, 0174): ready or not, the management plane's state, and each
/// dataset's.
/// </summary>
/// <remarks>
/// Ready, the body lists every dataset. Not ready, it is a <c>503 not-ready</c>
/// problem whose body lists only the datasets that are neither ready nor
/// closed; the readiness is read from it all the same. Members the client does
/// not know are kept in <see cref="Members"/> and in each dataset's, so that
/// what a newer server adds is shown rather than dropped (ADR 0203).
/// </remarks>
public sealed class Readiness
{
    private Readiness(bool isReady, string? detail, ManagementReadiness? management, IReadOnlyList<DatasetReadiness> datasets, IReadOnlyDictionary<string, JsonElement> members)
    {
        IsReady = isReady;
        Detail = detail;
        Management = management;
        Datasets = datasets;
        Members = members;
    }

    /// <summary>Whether the instance said it is ready.</summary>
    public bool IsReady { get; }

    /// <summary>Why it is not, when the server said (a drain).</summary>
    [DesignDecision(typeof(TypedReadsInTheProtocolClient.ProblemTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Detail { get; }

    /// <summary>The management plane's state on this instance, when the server sent it.</summary>
    public ManagementReadiness? Management { get; }

    /// <summary>The datasets the body lists, in the order it lists them.</summary>
    public IReadOnlyList<DatasetReadiness> Datasets { get; }

    /// <summary>Every other member of the body: <c>retention</c>, and what a newer server adds.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    // Ready: {status, management, datasets: {name: {…}}, …}.
    internal static Readiness Ready(JsonElement root)
    {
        ManagementReadiness? management = null;
        List<DatasetReadiness> datasets = [];
        Dictionary<string, JsonElement> members = new(StringComparer.Ordinal);

        foreach (JsonProperty member in root.EnumerateObject())
        {
            if (member.NameEquals("status"))
            {
                continue;
            }

            if (member.NameEquals("management") && member.Value.ValueKind == JsonValueKind.Object)
            {
                management = ManagementReadiness.Of(member.Value);
            }
            else if (member.NameEquals("datasets") && member.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty dataset in member.Value.EnumerateObject())
                {
                    if (dataset.Name.Length > 0 && dataset.Value.ValueKind == JsonValueKind.Object)
                    {
                        datasets.Add(DatasetReadiness.Of(new DatasetName(dataset.Name), dataset.Value));
                    }
                }
            }
            else
            {
                members[member.Name] = member.Value.Clone();
            }
        }

        return new Readiness(true, null, management, datasets, members);
    }

    // Not ready: the problem's members, datasets as an array of {name, …}.
    internal static Readiness NotReady(Problem problem)
    {
        Dictionary<string, JsonElement> members = new(problem.Members, StringComparer.Ordinal);
        ManagementReadiness? management = members.Remove("management", out JsonElement m) && m.ValueKind == JsonValueKind.Object ? ManagementReadiness.Of(m) : null;
        List<DatasetReadiness> datasets = [];

        if (members.Remove("datasets", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in listed.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && Json.Text(entry, "name") is string name)
                {
                    datasets.Add(DatasetReadiness.Of(new DatasetName(name), entry));
                }
            }
        }

        return new Readiness(false, problem.Detail, management, datasets, members);
    }
}

/// <summary>
/// The management plane on one instance (ADR 0163 point 13): its role for the
/// system dataset, the writer's epoch, the position it applied, its lag behind
/// the system tail, and whether its bootstrap operators match the recorded set.
/// Each is null when the server did not send it.
/// </summary>
public sealed class ManagementReadiness
{
    private ManagementReadiness(InstanceRole? role, WriterEpoch? epoch, FeedPosition? position, PositionLag? lag, BootstrapState? bootstrap)
    {
        Role = role;
        Epoch = epoch;
        Position = position;
        Lag = lag;
        Bootstrap = bootstrap;
    }

    /// <summary>Writer, follower or superseded, for the system dataset.</summary>
    public InstanceRole? Role { get; }

    /// <summary>The system dataset's writer epoch.</summary>
    public WriterEpoch? Epoch { get; }

    /// <summary>The management position this instance has applied.</summary>
    public FeedPosition? Position { get; }

    /// <summary>How far the applied position is behind the system tail.</summary>
    public PositionLag? Lag { get; }

    /// <summary>Whether the bootstrap operators match the recorded set.</summary>
    public BootstrapState? Bootstrap { get; }

    internal static ManagementReadiness Of(JsonElement value) => new(
        Json.Text(value, "role") is string role ? new InstanceRole(role) : null,
        Json.Count(value, "epoch") is long epoch ? new WriterEpoch(epoch) : null,
        Json.Count(value, "position") is long position ? new FeedPosition(position) : null,
        Json.Count(value, "lag") is long lag ? new PositionLag(lag) : null,
        Json.Text(value, "bootstrap") is string bootstrap ? new BootstrapState(bootstrap) : null);
}

/// <summary>
/// One dataset as readiness reports it on this instance: its state, this
/// instance's role for it, its projection lag, each shapes binding's
/// validation lag (ADR 0174), and every member as sent.
/// </summary>
public sealed class DatasetReadiness
{
    private DatasetReadiness(DatasetName name, ReadinessState? state, InstanceRole? role, string? reason, PositionLag? lag, IReadOnlyDictionary<string, PositionLag> validation, IReadOnlyDictionary<string, JsonElement> members)
    {
        Name = name;
        State = state;
        Role = role;
        Reason = reason;
        Lag = lag;
        Validation = validation;
        Members = members;
    }

    /// <summary>The dataset's name.</summary>
    public DatasetName Name { get; }

    /// <summary>Its state, when the server sent one.</summary>
    public ReadinessState? State { get; }

    /// <summary>This instance's role for it: writer, follower or elsewhere.</summary>
    public InstanceRole? Role { get; }

    /// <summary>Why the dataset is not ready, when the server said.</summary>
    [DesignDecision(typeof(TypedReadsInTheProtocolClient.ProblemTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Reason { get; }

    /// <summary>How far the projection is behind the head, when reported.</summary>
    public PositionLag? Lag { get; }

    /// <summary>Each shapes binding's validation projection lag, by the binding's IRI.</summary>
    public IReadOnlyDictionary<string, PositionLag> Validation { get; }

    /// <summary>Every member of the dataset's entry, those above included.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    internal static DatasetReadiness Of(DatasetName name, JsonElement value)
    {
        Dictionary<string, PositionLag> validation = new(StringComparer.Ordinal);

        if (value.TryGetProperty("validation", out JsonElement bindings) && bindings.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty binding in bindings.EnumerateObject())
            {
                if (binding.Value.ValueKind == JsonValueKind.Number && binding.Value.TryGetInt64(out long behind) && behind >= 0)
                {
                    validation[binding.Name] = new PositionLag(behind);
                }
            }
        }

        Dictionary<string, JsonElement> members = new(StringComparer.Ordinal);

        foreach (JsonProperty member in value.EnumerateObject())
        {
            members[member.Name] = member.Value.Clone();
        }

        return new DatasetReadiness(
            name,
            Json.Text(value, "state") is string state ? new ReadinessState(state) : null,
            Json.Text(value, "role") is string role ? new InstanceRole(role) : null,
            Json.Text(value, "reason"),
            Json.Count(value, "lag") is long lag ? new PositionLag(lag) : null,
            validation,
            members);
    }
}
