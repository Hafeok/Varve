// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Store;
using Varve.Protocol.Model;
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
/// An implementation commits with the metadata as given. When the client
/// stated a precondition it evaluates only where it holds — at the expected
/// position, or where every graph named is at its version — and anywhere
/// else answers <see cref="CommitOutcome.Conflict"/> with the head, having
/// evaluated nothing; such a request is never retried (ADR 0094). With no
/// precondition from the client, a conflict can come only from the
/// executor's own inferred scope, which the client had no part in, and the
/// executor re-pins and re-evaluates up to three times before answering it
/// (ADRs 0057 and 0094, amended 2026-10-13).
/// </para>
/// <para>
/// An operation that fails throws; the protocol answers it with
/// <c>operation-failed</c> and nothing is committed. A change outside the
/// caller's writable scope throws <see cref="GraphNotWritableException"/>,
/// answered with <c>403</c> and <c>graph-not-writable</c> (ADR 0107).
/// </para>
/// </remarks>
[Contract(typeof(VarveProtocolAndVarveServer.UpdateExecutorSeam), Role = "the host-bound execution of a SPARQL Update request as one commit")]
public interface ISparqlUpdateExecutor
{
    /// <summary>Executes <paramref name="update"/> as one commit, or none, reading and writing within <paramref name="scope"/>.</summary>
    ValueTask<CommitResult> ExecuteAsync(
        Dataset dataset,
        Update update,
        CommitMetadata metadata,
        UpdatePreconditions preconditions,
        CallerScope scope,
        CancellationToken cancellationToken);
}
