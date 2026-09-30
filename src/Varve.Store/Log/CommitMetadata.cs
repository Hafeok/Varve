// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Log;

/// <summary>
/// What a commit records about itself besides its delta (spec §1, <c>meta</c>).
/// The timestamp is not here: the sequencer assigns it, and never accepts one.
/// </summary>
public sealed class CommitMetadata
{
    /// <summary>Who made the change. A term, never an inline string, so that it can later be private.</summary>
    public RequestTerm Agent { get; init; }

    /// <summary>Why.</summary>
    public RequestTerm Cause { get; init; }

    /// <summary>
    /// The named graph the commit declares it is about. **A declaration recorded
    /// in metadata, never enforced by the store**; a validator may enforce it
    /// (specification 1.2, §1).
    /// </summary>
    public RequestTerm GraphScope { get; init; }
}
