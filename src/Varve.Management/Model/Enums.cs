// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Management.Model;

/// <summary>What a grant permits (ADR 0166).</summary>
public enum GrantAccess : byte
{
    /// <summary>Read the grant's graphs.</summary>
    Read,

    /// <summary>Read and write the grant's graphs.</summary>
    Write,

    /// <summary>Everything on the dataset, every graph, and its management resources.</summary>
    Admin,

    /// <summary>Everything on every dataset and the fleet; declared only in the fleet graph.</summary>
    Operator,
}

/// <summary>Where a dataset is stored.</summary>
public enum StorageKind : byte
{
    /// <summary>The file backend.</summary>
    File,

    /// <summary>Memory, gone when the instance stops.</summary>
    Memory,

    /// <summary>The fleet's object store.</summary>
    ObjectStorage,
}

/// <summary>A dataset's default access scope (ADR 0021): what a read sees without a <c>Varve-As-Of</c>.</summary>
public enum HistoryScope : byte
{
    /// <summary>Every position is readable.</summary>
    AllHistory,

    /// <summary>Only the head is readable.</summary>
    Current,
}

/// <summary>How a graph template's expansion matches a graph.</summary>
public enum TemplateMatch : byte
{
    /// <summary>The graph is the expansion.</summary>
    Exact,

    /// <summary>The graph starts with the expansion.</summary>
    Prefix,
}

/// <summary>What a binding binds a graph to.</summary>
public enum BindingKind : byte
{
    /// <summary>A shapes graph, for SHACL gating.</summary>
    Shapes,

    /// <summary>A TBox graph, for inference.</summary>
    TBox,
}

/// <summary>What a shapes binding does to a commit: <c>varve:validationMode</c> (ADR 0170).</summary>
public enum ShapesMode : byte
{
    /// <summary><c>varve:Off</c>: recorded only.</summary>
    Off,

    /// <summary><c>varve:Report</c>: accepted, its affected report's digest attached.</summary>
    Report,

    /// <summary><c>varve:Reject</c>: a commit introducing a violation is refused.</summary>
    Reject,
}

/// <summary>What kind of principal a grant names.</summary>
public enum PrincipalKind : byte
{
    /// <summary>One agent, by its IRI.</summary>
    Agent,

    /// <summary>Every caller whose role claim holds the value.</summary>
    Role,

    /// <summary>Every caller whose claim of the type holds the value.</summary>
    Group,
}

/// <summary>Where a dataset's event declarations are (ADR 0167): <c>varve:eventDeclarations</c> on its node.</summary>
public enum EventDeclarationState : byte
{
    /// <summary>No value: in the dataset's catalogue graphs, written by the data plane.</summary>
    InDataset,

    /// <summary><c>varve:Moving</c>: frozen in the catalogue graphs, being copied here.</summary>
    Moving,

    /// <summary><c>varve:Moved</c>: in the dataset's graph of the system dataset, for good.</summary>
    Moved,
}
