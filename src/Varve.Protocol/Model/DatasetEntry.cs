// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Protocol.Model;

/// <summary>What a dataset is kept in (ADR 0105): a directory under the host's root, or memory.</summary>
public enum DatasetStorage : byte
{
    /// <summary>A directory under the host's root; durable.</summary>
    File = 0,

    /// <summary>Memory; gone with the process.</summary>
    Memory = 1,
}

/// <summary>How the host came to know a dataset (ADR 0105).</summary>
public enum DatasetOrigin : byte
{
    /// <summary>Named in the host's configuration.</summary>
    Configured = 0,

    /// <summary>Its directory was found under the root at start, or opened by name later.</summary>
    Discovered = 1,

    /// <summary>Created through the admin API in this process.</summary>
    Created = 2,
}

/// <summary>A dataset's state in the host (ADR 0105).</summary>
public enum DatasetState : byte
{
    /// <summary>Open and served.</summary>
    Open = 0,

    /// <summary>Closed by the admin API: its directory is there, it answers nothing, and a restart or <c>open</c> serves it again.</summary>
    Closed = 1,

    /// <summary>A directory that did not open, with the reason; never skipped (ADR 0105).</summary>
    Failed = 2,
}

/// <summary>The answer to an admin operation on a dataset (ADR 0105).</summary>
public enum AdminOutcome : byte
{
    /// <summary>Done.</summary>
    Done = 0,

    /// <summary>No dataset of that name in the state the operation needs.</summary>
    NotFound = 1,

    /// <summary>A dataset of that name exists, in some state.</summary>
    Exists = 2,

    /// <summary>The dataset is open, and the operation needs it closed.</summary>
    Open = 3,

    /// <summary>The operation failed; <c>DatasetEntry.Reason</c> says why after a failed open.</summary>
    Failed = 4,
}

/// <summary>One dataset the host knows: its name, state, storage, origin, and when open its id and head (ADR 0105).</summary>
public sealed class DatasetEntry
{
    /// <summary>An entry.</summary>
    public DatasetEntry(DatasetName name, DatasetState state, DatasetStorage storage, DatasetOrigin origin, string? reason, DatasetId? id, Position? head)
    {
        Name = name;
        State = state;
        Storage = storage;
        Origin = origin;
        Reason = reason;
        Id = id;
        Head = head;
    }

    /// <summary>The name a request uses.</summary>
    public DatasetName Name { get; }

    /// <summary>Open, closed or failed.</summary>
    public DatasetState State { get; }

    /// <summary>Where it is kept.</summary>
    public DatasetStorage Storage { get; }

    /// <summary>Configured, discovered under the root, or created by the admin API.</summary>
    public DatasetOrigin Origin { get; }

    /// <summary>Why it is failed, or null.</summary>
    [DesignDecision(typeof(ModelNamespacesForLayers3To5.FailureTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Reason { get; }

    /// <summary>The dataset's own identity, when open.</summary>
    public DatasetId? Id { get; }

    /// <summary>The head, when open.</summary>
    public Position? Head { get; }
}
