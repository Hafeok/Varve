// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The answer to a write to the system dataset waits until this instance has
/// applied the head the write leaves: the writer applies its own write before
/// it answers, so the next request here is decided by it and its datasets
/// converge to it. A write that changed nothing states what the head already
/// says, which another commit may have put there without this instance having
/// applied it yet, so it is applied before the answer too: a cancel answered
/// is in force here (ADR 0167, amended for #254).
/// </summary>
[Contract(typeof(EventDeclarationsMoveToTheManagementPlane.CancelIsAnsweredOnlyWhenInForce), Role = "the advance a management write's answer waits for, a write with no change included")]
internal static class ManagementAnswers
{
    /// <summary>Advances the management plane after a write whose <paramref name="outcome"/> leaves a head to apply.</summary>
    internal static async ValueTask AppliedAsync(ProtocolOptions options, CommitOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome is CommitOutcome.Committed or CommitOutcome.NoChange && options.Management is { } management)
        {
            await management.AdvanceAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
