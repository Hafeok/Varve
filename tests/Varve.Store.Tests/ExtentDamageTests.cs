// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// A format-5 extent damaged where it is kept (#133's review, after #146 and
/// #126's damaged cache): one byte of an order extent zeroed, flipped, or the
/// blob cut short there, at any position. Opened again from the log and the
/// extents, the dataset serves every position as before — by scan and by
/// lookup — or fails the read. It never serves a different answer.
/// </summary>
/// <remarks>
/// The header is self-hashed and the directory hashed whole (storage format
/// §7.1); the key blocks and the columns are checked a block at a time
/// against the checksums the directory carries (§7.5, ADR 0177), when read.
/// </remarks>
public sealed class ExtentDamageTests
{
    [Fact]
    public async Task an_order_extent_with_a_byte_zeroed_flipped_or_cut_never_serves_a_different_answer()
    {
        // A history with retractions and re-assertions, so the middle set has
        // open, closing and within rows; checkpoints at 6, 12 and the head.
        await using Dataset dataset = await T.OpenOrCreate(new MemoryStorage(), T.Options());
        Random random = new(133);

        for (int c = 0; c < 18; c++)
        {
            CommitRequest request = new();

            for (int i = 0; i < 12; i++)
            {
                int s = random.Next(40);

                if (random.Next(3) == 0)
                {
                    request.Retract(T.Iri("s" + s), T.Iri("p" + (s % 3)), T.Iri("o" + (s % 5)));
                }
                else
                {
                    request.Assert(T.Iri("s" + s), T.Iri("p" + (s % 3)), T.Iri("o" + (s % 5)));
                }
            }

            await dataset.CommitAsync(request, T.Ct);

            if ((c + 1) % 6 == 0)
            {
                await dataset.CheckpointAsync(dataset.Head, T.Ct);
            }
        }

        IStorage storage = dataset.StorageForTests();
        List<byte[]> log = await LogPropertyTests.SegmentsAsync(storage);
        List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> blobs = await T.CheckpointBlobsAsync(storage);
        string expected = await ServeAsync(log, blobs);

        // Each line pair: the scan, and the lookups of every quad; they agree.
        string[] lines = expected.Split('\n');

        for (int l = 0; l + 1 < lines.Length; l += 2)
        {
            string scan = lines[l][(lines[l].IndexOf(':', StringComparison.Ordinal) + 2)..];
            string holds = lines[l + 1][(lines[l + 1].IndexOf(" holds ", StringComparison.Ordinal) + 7)..];
            Assert.True(scan == holds, "undamaged, position " + l / 2 + ": scan '" + scan + "'\n lookups '" + holds + "'");
        }
        string middle = dataset.SetNamesForTests()[1];
        List<(int Blob, byte[] Damaged, string How)> cases = [];

        for (int b = 0; b < blobs.Count; b++)
        {
            string name = blobs[b].Name.Value;

            if (!name.StartsWith(middle, StringComparison.Ordinal) || !name[middle.Length..].StartsWith("order", StringComparison.Ordinal))
            {
                continue;
            }

            byte[] whole = blobs[b].Bytes.ToArray();

            for (int at = 0; at < whole.Length; at++)
            {
                byte[] zeroed = [.. whole];
                zeroed[at] = 0;
                byte[] flipped = [.. whole];
                flipped[at] ^= 0xFF;

                if (whole[at] != 0)
                {
                    cases.Add((b, zeroed, name + ": byte " + at + " zeroed"));
                }

                cases.Add((b, flipped, name + ": byte " + at + " flipped"));
                cases.Add((b, whole[..at], name + ": cut at byte " + at));
            }
        }

        // A pull request checks every sixteenth case, from where a seed says;
        // the nightly run checks every one (docs/testing.md section 8).
        Sweep sweep = Depth.SweepOf("EXTENT_DAMAGE", 16);
        List<string> wrong = [];
        int checkedCases = 0;
        int failed = 0;

        for (int i = sweep.First - 1; i < cases.Count; i += sweep.Stride)
        {
            (int b, byte[] damaged, string how) = cases[i];
            List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> copy = [.. blobs];
            copy[b] = (blobs[b].Name, damaged);
            checkedCases++;

            try
            {
                string served = await ServeAsync(log, copy);

                if (served != expected)
                {
                    string[] a = expected.Split('\n');
                    string[] c = served.Split('\n');
                    int d = 0;

                    while (d < Math.Min(a.Length, c.Length) && a[d] == c[d])
                    {
                        d++;
                    }

                    wrong.Add(how + " (first difference, line " + d + ": expected '" + (d < a.Length ? a[d][..Math.Min(200, a[d].Length)] : "<end>") + "', served '" + (d < c.Length ? c[d][..Math.Min(200, c[d].Length)] : "<end>") + "')");
                }
            }
            catch (Exception error) when (error is not Xunit.Sdk.XunitException)
            {
                // A read that fails is not a wrong answer.
                failed++;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(sweep.Replay + ": " + checkedCases + " of " + cases.Count + " damaged extents, " + failed + " failed a read, " + wrong.Count + " served a different answer");
        Assert.True(wrong.Count == 0, sweep.Replay + " replays this. " + wrong.Count + " of " + checkedCases + " damaged extents served a different answer:\n  " + string.Join("\n  ", wrong.Take(40)));
    }

    // Every position's quads, by scan, and every quad any position holds,
    // looked up at every position: from the log and the extents alone.
    private static async Task<string> ServeAsync(List<byte[]> log, List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> blobs)
    {
        MemoryStorage alone = MemoryStorage.FromLog(T.Manifest, log.Select(s => (ReadOnlyMemory<byte>)s), blobs.Select(b => new KeyValuePair<BlobName, ReadOnlyMemory<byte>>(b.Name, b.Bytes)));
        await using Dataset opened = await T.OpenOrCreate(alone, T.Options());
        List<DatasetView> views = [];
        List<string> served = [];

        try
        {
            for (long q = 0; q <= opened.Head.Value; q++)
            {
                views.Add(await opened.AsOfAsync(new Position(q), T.Ct));
            }

            List<List<Quad>> scans = [.. views.Select(T.All)];
            HashSet<Quad> every = [.. scans.SelectMany(s => s)];

            for (int q = 0; q < views.Count; q++)
            {
                served.Add(q + ": " + string.Join(" ", scans[q].Select(x => T.Render(views[q], x)).Order(StringComparer.Ordinal)));
                served.Add(q + " holds " + string.Join(" ", every.Where(x => views[q].Contains(in x)).Select(x => T.Render(views[q], x)).Order(StringComparer.Ordinal)));
            }
        }
        finally
        {
            foreach (DatasetView view in views)
            {
                view.Dispose();
            }
        }

        return string.Join("\n", served);
    }
}
