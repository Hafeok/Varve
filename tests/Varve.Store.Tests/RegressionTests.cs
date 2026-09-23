// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Counterexamples the properties found during milestone 4, each kept as a
/// named case beside the property that found it (ADR 0043).
/// </summary>
public class RegressionTests
{
    /// <summary>
    /// Found by the records property (CsCheck seed <c>655VFtVGHEs5</c>, one
    /// shrink, 64-byte records and 1 KiB segments). A cut inside a new
    /// segment's preamble was ignored on open; recovery sealed that segment
    /// and began another; and the next open refused the log, because the short
    /// segment was no longer the last one. One crash in that window, and one
    /// more commit, made the dataset unopenable.
    /// </summary>
    [Fact]
    public async Task a_cut_inside_a_segment_preamble_survives_the_next_reopen()
    {
        MemoryStorage storage = new();
        DatasetOptions options = T.Options(segmentBytes: 1024);

        await using (Dataset dataset = await Dataset.OpenAsync(storage, options, T.Ct))
        {
            for (int i = 0; (await storage.Log.ListSegmentsAsync(T.Ct)).Count < 2; i++)
            {
                await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 100))), T.Ct);
            }
        }

        List<byte[]> segments = await LogPropertyTests.SegmentsAsync(storage);

        for (int cut = 0; cut < 8; cut++)
        {
            MemoryStorage copy = MemoryStorage.FromSegments([segments[0], segments[1].AsMemory(0, cut)]);
            long head;

            await using (Dataset recovered = await Dataset.OpenAsync(copy, options, T.Ct))
            {
                head = recovered.Head;
                CommitResult next = await recovered.CommitAsync(new CommitRequest().Assert(T.Iri("after"), T.Iri("p"), T.Iri("o")), T.Ct);
                Assert.Equal(head + 1, next.Position);
            }

            await using Dataset reopened = await Dataset.OpenAsync(copy, options, T.Ct);
            Assert.Equal(head + 1, reopened.Head);
        }
    }
}
