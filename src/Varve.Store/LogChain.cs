// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Store;

/// <summary>
/// Divergence detection between two copies of a log (spec I6, ADR 0014).
/// </summary>
/// <remarks>
/// Detection, not prevention and not authentication: the chain proves two
/// logs are not the same history and says where they parted. It does not say
/// which one is right.
/// </remarks>
public static class LogChain
{
    /// <summary>
    /// The first position at which two logs hold different commits, or null
    /// when one is a prefix of the other (or they are equal). Both logs must
    /// verify.
    /// </summary>
    /// <exception cref="LogVerificationException">Either log does not verify.</exception>
    public static async ValueTask<long?> FindDivergenceAsync(ISegmentStore first, ISegmentStore second, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        LogScan left = await LogReader.ScanAsync(first, cancellationToken).ConfigureAwait(false);
        LogScan right = await LogReader.ScanAsync(second, cancellationToken).ConfigureAwait(false);
        int common = Math.Min(left.Commits.Count, right.Commits.Count);

        for (int i = 0; i < common; i++)
        {
            // Each header hash covers every header before it through prev, so
            // the first difference is the branch point.
            if (!left.Commits[i].HeaderHash.AsSpan().SequenceEqual(right.Commits[i].HeaderHash))
            {
                return i + 1;
            }
        }

        return null;
    }
}
