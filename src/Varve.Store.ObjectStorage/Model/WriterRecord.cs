// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// The writer record: the dataset's writer epoch, its holder, and every claim
/// before it (ADR 0136). Kept as the object <c>writer</c> beside <c>log/</c>,
/// replaced only by replace-if-unchanged.
/// </summary>
public sealed record WriterRecord
{
    /// <summary>The current epoch.</summary>
    public required WriterEpoch Epoch { get; init; }

    /// <summary>Who holds it.</summary>
    public required WriterInstance Holder { get; init; }

    /// <summary>Why it was released, when it has been: then nobody holds it, and the next claim follows a release.</summary>
    public ReleaseReason? Released { get; init; }

    /// <summary>Every claim, oldest first; the last is the current epoch's.</summary>
    public required IReadOnlyList<EpochClaim> History { get; init; }
}
