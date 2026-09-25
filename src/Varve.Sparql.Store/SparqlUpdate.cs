// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Sparql.Algebra;
using Varve.Store;

namespace Varve.Sparql.Store;

/// <summary>
/// Executes a SPARQL 1.1 Update request against a dataset as one commit
/// (<c>sparql-update-store.md</c>, ADR 0057).
/// </summary>
public static class SparqlUpdate
{
    /// <summary>
    /// Pins the head, evaluates each operation in order over the overlay of
    /// the ones before it, and submits the composed change as one commit that
    /// expects the pinned position.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is the commit's: <see cref="CommitOutcome.Committed"/> when
    /// the request changed something, <see cref="CommitOutcome.NoChange"/>
    /// when its net effect is empty — no commit is made — and
    /// <see cref="CommitOutcome.Conflict"/> when another writer committed
    /// after the pin and <see cref="UpdateOptions.ConflictRetries"/> did not
    /// allow, or did not survive, another attempt. The dataset's pre-commit
    /// validators run as part of the commit and may return
    /// <see cref="CommitOutcome.Rejected"/>.
    /// </para>
    /// <para>
    /// The pin lives for one attempt and is released whatever happens (ADR 0052).
    /// </para>
    /// </remarks>
    /// <exception cref="SparqlUpdateException">An operation failed; nothing was committed.</exception>
    public static async ValueTask<CommitResult> ExecuteAsync(
        Dataset dataset,
        Update update,
        UpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(options);

        for (int attempt = 0; ; attempt++)
        {
            CommitRequest request;

            using (DatasetView view = dataset.Pin())
            {
                RequestExecution execution = new(view.Stage(), update, options, cancellationToken);
                await execution.RunAsync().ConfigureAwait(false);
                request = execution.ToCommitRequest();
            }

            CommitResult result = await dataset.CommitAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.Outcome != CommitOutcome.Conflict || attempt >= options.ConflictRetries)
            {
                return result;
            }
        }
    }
}
