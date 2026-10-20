// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// Where a write is sent (ADR 0168): the host's knowledge of who writes each
/// dataset of its fleet, and of the system dataset. A host that is a fleet of
/// one leaves it unset, and every write is local.
/// </summary>
[Contract(typeof(WriteRoutingByForwarding.WriterDirectorySeam), Role = "the host's answer to who writes a dataset, or the system dataset")]
public interface IWriterDirectory
{
    /// <summary>This instance's name, sent in the hop header of what it forwards.</summary>
    WriterName Instance { get; }

    /// <summary>Who writes <paramref name="target"/>, as this instance last learned.</summary>
    ValueTask<WriterRoute> RouteAsync(WriterTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// What a forward learned: the writer named was not the writer, or could
    /// not be reached. The next route is read again.
    /// </summary>
    void Stale(WriterTarget target);
}

/// <summary>
/// The management plane as the data plane needs it (ADRs 0163, 0165): the
/// position whose grants are in force here, one advance of the system
/// follower when a request asks for more, and the check that keeps the
/// management positions a dataset records from decreasing.
/// </summary>
[Contract(typeof(TheManagementPlane.GrantsAtAppliedPosition), Role = "the host's applied management position and its advance")]
public interface IManagementAuthority
{
    /// <summary>The management position whose grants this instance enforces now.</summary>
    ManagementPosition Applied { get; }

    /// <summary>Advances the system follower once; returns the position applied after.</summary>
    ValueTask<ManagementPosition> AdvanceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The validator a write to <paramref name="dataset"/>, authorised at
    /// <paramref name="authorised"/>, carries into the sequencer: it refuses
    /// the commit with <c>management-behind</c> when the dataset's log
    /// already records a later position (ADR 0165).
    /// </summary>
    ICommitValidator OrderOf(DatasetName dataset, ManagementPosition authorised);
}

/// <summary>
/// A follower advanced on demand (ADR 0164): before a read, when its last
/// advance is older than the host's interval or the read asks for a later
/// position.
/// </summary>
[Contract(typeof(TheTier1Follower.FollowOnDemand), Role = "the host's on-demand advance of a follower before a read")]
public interface IDatasetFollowing
{
    /// <summary>
    /// Advances <paramref name="dataset"/>, a follower, when its last advance
    /// is older than the interval or <paramref name="atLeast"/> is above its head.
    /// </summary>
    ValueTask FollowAsync(DatasetName name, Dataset dataset, Position? atLeast, CancellationToken cancellationToken);
}

/// <summary>
/// The management plane's state in this instance (ADR 0163): the system
/// dataset it holds, as writer or follower, the grants it enforces, and what
/// readiness and <c>/manage/system</c> report. The management endpoints read
/// and commit through it; a host with no management plane leaves it unset.
/// </summary>
[Contract(typeof(TheManagementPlane.SystemDatasetPerFleet), Role = "the host's system dataset and its grants")]
public interface IManagementPlane
{
    /// <summary>The fleet's system dataset, as this instance holds it.</summary>
    Dataset System { get; }

    /// <summary>The grants at the position this instance has applied.</summary>
    Varve.Management.GrantTable Table { get; }

    /// <summary>What <c>/manage/system</c> and readiness report.</summary>
    ManagementStatus Status { get; }

    /// <summary>The validator every commit to the system dataset runs (ADR 0166): the code validator over the graphs it touches.</summary>
    ICommitValidator Validator { get; }
}
