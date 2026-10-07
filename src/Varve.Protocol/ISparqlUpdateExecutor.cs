// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Sparql.Algebra;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// Executes a parsed SPARQL Update request against a dataset as at most one
/// commit. The host binds it — <c>Varve.Server</c> to
/// <c>Varve.Sparql.Store.SparqlUpdate</c> — because a layer 5 package cannot
/// reference another (ADR 0091).
/// </summary>
/// <remarks>
/// <para>
/// An implementation commits with the metadata as given, never
/// retries a <see cref="CommitOutcome.Conflict"/> (ADR 0094), and, when
/// an expected position is given, evaluates only at that
/// position: anywhere else the answer is <see cref="CommitOutcome.Conflict"/>
/// with the head, and nothing is evaluated.
/// </para>
/// <para>
/// An operation that fails throws; the protocol answers it with
/// <c>operation-failed</c> and nothing is committed.
/// </para>
/// </remarks>
[Contract(typeof(VarveProtocolAndVarveServer.UpdateExecutorSeam), Role = "the host-bound execution of a SPARQL Update request as one commit")]
public interface ISparqlUpdateExecutor
{
    /// <summary>Executes <paramref name="update"/> as one commit, or none.</summary>
    ValueTask<CommitResult> ExecuteAsync(
        Dataset dataset,
        Update update,
        CommitMetadata metadata,
        Position? expectedPosition,
        CancellationToken cancellationToken);
}
