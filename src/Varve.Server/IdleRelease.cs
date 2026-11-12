// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// What the server does once a checkpoint a caller asked for has been taken
/// (ADR 0244 point 6): if the dataset then takes no commit for
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
/// 24 ms. So it runs only for a dataset that has gone quiet.
/// </remarks>
[DesignDecision(typeof(MemoryBounds.TheServerReleasesMemoryAfterACheckpoint), Scope = ExceptionScope.Boundary)]
internal static class IdleRelease
{
    /// <summary>How long the dataset takes no commit, after its checkpoint's maintenance, before memory is released.</summary>
    internal static readonly TimeSpan IdleFor = TimeSpan.FromSeconds(5);

    /// <summary>The hook <c>ProtocolOptions.AfterCheckpoint</c> takes: starts the wait and returns at once.</summary>
    internal static ValueTask AfterCheckpoint(Dataset dataset, CancellationToken cancellationToken)
    {
        _ = Task.Run(
            () => WhenIdleAsync(
                () => dataset.MaintainAsync(CancellationToken.None),
                () => dataset.Head,
                () => Task.Delay(IdleFor, CancellationToken.None),
                static () => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)),
            CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Lets maintenance finish, waits, and releases only if the head has not
    /// moved meanwhile; true when it released. A dataset closed in the
    /// meantime, or whose maintenance failed, is left alone.
    /// </summary>
    internal static async Task<bool> WhenIdleAsync(Func<ValueTask> maintain, Func<Position> head, Func<Task> wait, Action release)
    {
        try
        {
            await maintain().ConfigureAwait(false);
            Position before = head();
            await wait().ConfigureAwait(false);

            if (head() != before)
            {
                return false;
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
