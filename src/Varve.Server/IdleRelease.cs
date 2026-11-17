// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// What the server does once a checkpoint a caller asked for has been taken
/// (ADR 0244 point 6): if no dataset on the server then takes a commit for
/// <see cref="IdleFor"/>, it asks the collector for one aggressive,
/// compacting collection, which returns to the operating system what the
/// import and the checkpoint left behind. The checkpoint's answer does not
/// wait for it.
/// </summary>
/// <remarks>
/// Measured on #227's import at 1.75 million quads, Native AOT. Idle, the
/// server keeps 372 MB without it and 155 MB with it; no runtime setting
/// and no ordinary forced collection releases any of it. Under a writer it
/// would buy nothing, 315 MB against 330 MB, since a server that allocates
/// collects on its own, and the longest commit beside it was 136 ms against
/// 24 ms. The collection pauses the whole process, so a commit to any
/// other dataset would pay that pause too: it runs only on a server whose
/// datasets have all gone quiet, the system dataset included. The wait
/// decides only when memory is returned, and never correctness.
/// </remarks>
[DesignDecision(typeof(MemoryBounds.TheServerReleasesMemoryAfterACheckpoint), Scope = ExceptionScope.Boundary)]
internal static class IdleRelease
{
    /// <summary>How long no dataset on the server takes a commit, after the checkpoint's maintenance, before memory is released.</summary>
    internal static readonly TimeSpan IdleFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The hook <c>ProtocolOptions.AfterCheckpoint</c> takes, over the heads of
    /// every dataset the server holds open: it starts the wait and returns at once.
    /// </summary>
    internal static Func<Dataset, CancellationToken, ValueTask> Over(Func<IReadOnlyList<Position>> heads) =>
        (dataset, cancellationToken) =>
        {
            _ = Task.Run(
                () => WhenIdleAsync(
                    () => dataset.MaintainAsync(CancellationToken.None),
                    heads,
                    () => Task.Delay(IdleFor, CancellationToken.None),
                    static () => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)),
                CancellationToken.None);
            return ValueTask.CompletedTask;
        };

    /// <summary>
    /// Lets the checkpointed dataset's maintenance finish, waits, and
    /// releases only if no dataset's head has moved meanwhile; true when it
    /// released. <paramref name="heads"/> answers every open dataset's head
    /// in a stable order. A dataset opened or closed during the wait leaves
    /// the server not known to be idle, so nothing is released; so does a
    /// dataset closed in the meantime, or whose maintenance failed.
    /// </summary>
    internal static async Task<bool> WhenIdleAsync(Func<ValueTask> maintain, Func<IReadOnlyList<Position>> heads, Func<Task> wait, Action release)
    {
        try
        {
            await maintain().ConfigureAwait(false);
            IReadOnlyList<Position> before = heads();
            await wait().ConfigureAwait(false);
            IReadOnlyList<Position> after = heads();

            if (after.Count != before.Count)
            {
                return false;
            }

            for (int i = 0; i < after.Count; i++)
            {
                if (after[i] != before[i])
                {
                    return false;
                }
            }

            release();
            return true;
        }
        catch (Exception error) when (error is ObjectDisposedException or OperationCanceledException or InvalidOperationException or AggregateException or System.IO.IOException)
        {
            return false;
        }
    }
}
