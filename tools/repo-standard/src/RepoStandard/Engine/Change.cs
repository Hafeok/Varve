// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RepoStandard.Engine;

/// <summary>What a change does to its resource.</summary>
internal enum ChangeAction
{
    /// <summary>The declaration names it and the repository does not have it.</summary>
    Create,

    /// <summary>Both have it and they differ.</summary>
    Update,

    /// <summary>The repository has it and the declaration does not name it.</summary>
    Delete,

    /// <summary>A difference repo-standard reports and cannot write.</summary>
    Unfixable,
}

/// <summary>One field that differs, as a dotted path and both values.</summary>
internal sealed record FieldChange(string Path, JsonNode? Live, JsonNode? Declared);

/// <summary>
/// One difference between the declaration and the repository, and the write
/// that removes it.
/// </summary>
/// <param name="Kind">The declaration key it belongs to: <c>labels</c>, <c>rulesets</c>, ...</param>
/// <param name="Target">What it is within the kind: a label's name, <c>settings</c>, ...</param>
/// <param name="Action">Create, update, delete, or unfixable.</param>
/// <param name="Fields">The differing fields, for an update.</param>
/// <param name="Note">A sentence for the reader, or null.</param>
/// <param name="Apply">The write; null for an unfixable change.</param>
internal sealed record Change(
    string Kind,
    string Target,
    ChangeAction Action,
    IReadOnlyList<FieldChange> Fields,
    string? Note,
    Func<CancellationToken, Task>? Apply);
