// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;

namespace Varve.Protocol.Model;

/// <summary>What this instance is to the system dataset (ADR 0163).</summary>
public enum ManagementRole : byte
{
    /// <summary>It writes the system dataset.</summary>
    Writer = 0,

    /// <summary>It follows the system dataset.</summary>
    Follower = 1,

    /// <summary>It was the writer, and a newer epoch took the system dataset from it; it follows now.</summary>
    Superseded = 2,
}

/// <summary>Whether this instance's bootstrap operators agree with the recorded ones (ADR 0163 point 7).</summary>
public enum BootstrapState : byte
{
    /// <summary>The configured set is the recorded one.</summary>
    Recorded = 0,

    /// <summary>Nothing is recorded yet: the system writer records it at its first write.</summary>
    Absent = 1,

    /// <summary>The configured set differs from the recorded one: the instance is not ready.</summary>
    Mismatch = 2,
}

/// <summary>The management plane's state in this instance, as readiness and <c>/manage/system</c> report it.</summary>
/// <param name="Role">Writer, follower, or superseded.</param>
/// <param name="Epoch">The system dataset's writer epoch this instance last read, or 0.</param>
/// <param name="Applied">The management position whose grants it enforces.</param>
/// <param name="Tail">The system dataset's tail as this instance last saw it.</param>
/// <param name="Bootstrap">Whether its bootstrap operators agree with the recorded ones.</param>
/// <param name="WriterAddress">The system writer's internal address, when known.</param>
[DesignDecision(typeof(WriteRoutingByForwarding.WriterDirectorySeam), Scope = ExceptionScope.Boundary)]
public readonly record struct ManagementStatus(ManagementRole Role, long Epoch, ManagementPosition Applied, ManagementPosition Tail, BootstrapState Bootstrap, string? WriterAddress)
{
    /// <summary>How far the applied position is behind the tail.</summary>
    public long Lag => Tail.Value - Applied.Value;
}
