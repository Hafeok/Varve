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
/// Every kind of cached derived blob damaged where it is kept (ADR 0213,
/// #146's gate): one byte zeroed, flipped, or the blob cut there, at every
/// position of every blob but the order extents, which
/// <see cref="ExtentDamageTests"/> sweeps. That covers the runs maintenance
/// wrote, the projection's state, the graph-version indexes, the catalog, the
/// local commit index, and each set's dictionary, commit index and graph
/// index. Opened again from the log and the damaged blobs, the dataset
/// serves every position as before, by scan and by lookup, with every term
/// rendered. Damage is a miss: never a different answer, never a failed read.
/// </summary>
/// <remarks>
/// The checks are above the backend (<see cref="IReadableBlob"/>), so the
/// memory backend stands for the others here; the commit index on an object
/// store's cache has its own sweep (<c>ObjectCacheTests</c>), and damage met
/// after the open has <see cref="MidReadDamageTests"/>, on files and objects.
/// </remarks>
public sealed class DerivedDamageTests
{
    [Fact]
    public async Task every_kind_with_a_byte_zeroed_flipped_or_cut_is_a_miss_never_a_different_answer_or_a_failed_read()
    {
        MemoryStorage storage = new();
        ManualClock clock = ManualClock.Epoch();

        await using (Dataset dataset = await T.OpenOrCreate(storage, Options(clock)))
        {
            Random random = new(146);

            for (int c = 1; c <= 16; c++)
            {
                CommitRequestBuilder request = new();

                for (int i = 0; i < 8; i++)
                {
                    int s = random.Next(30);
                    RdfTerm o = T.Literal("ø" + (s % 4) + " " + (c % 3));
                    RdfTerm g = T.Iri("g" + (s % 2));

                    if (random.Next(3) == 0)
                    {
                        request.Retract(T.Iri("s" + s), T.Iri("p" + (s % 3)), o, g);
                    }
                    else
                    {
                        request.Assert(T.Iri("s" + s), T.Iri("p" + (s % 3)), o, g);
                    }
                }

                clock.Now = clock.Now.AddMinutes(1);
                await dataset.CommitAsync(request, T.Ct);
                await dataset.MaintainAsync(T.Ct);

                if (c is 6 or 12)
                {
                    await dataset.CheckpointAsync(dataset.Head, T.Ct);
                }
            }
        }

        List<byte[]> log = await LogPropertyTests.SegmentsAsync(storage);
        List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> blobs = [];

        foreach (BlobName name in await storage.Derived.ListAsync(T.Ct))
        {
            blobs.Add((name, await T.ReadBlobAsync(storage, name)));
        }

        string expected = await ServeAsync(log, blobs);
        List<(int Blob, int At, string How)> cases = [];
        Dictionary<string, int> perKind = [];

        for (int b = 0; b < blobs.Count; b++)
        {
            string name = blobs[b].Name.Value;

            if (name.Contains("/order", StringComparison.Ordinal))
            {
                continue;
            }

            ReadOnlySpan<byte> whole = blobs[b].Bytes.Span;
            perKind[KindOf(name)] = perKind.GetValueOrDefault(KindOf(name)) + whole.Length;

            for (int at = 0; at < whole.Length; at++)
            {
                if (whole[at] != 0)
                {
                    cases.Add((b, at, "zeroed"));
                }

                cases.Add((b, at, "flipped"));
                cases.Add((b, at, "cut"));
            }
        }

        Assert.Equal(["catalog", "commit index", "graph index", "run", "set commit index", "set dictionary", "set graph index", "state"], perKind.Keys.Order(StringComparer.Ordinal));

        // A pull request checks every sixteenth case, from where a seed says;
        // the nightly run checks every one (docs/testing.md section 8).
        Sweep sweep = Depth.SweepOf("DERIVED_DAMAGE", 16);
        List<string> wrong = [];
        Dictionary<string, int> checkedPerKind = [];
        int failed = 0;

        for (int i = sweep.First - 1; i < cases.Count; i += sweep.Stride)
        {
            (int b, int at, string how) = cases[i];
            byte[] whole = blobs[b].Bytes.ToArray();
            byte[] damaged = how switch
            {
                "zeroed" => Set(whole, at, 0),
                "flipped" => Set(whole, at, (byte)(whole[at] ^ 0xFF)),
                _ => whole[..at],
            };
            List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> copy = [.. blobs];
            copy[b] = (blobs[b].Name, damaged);
            string kind = KindOf(blobs[b].Name.Value);
            checkedPerKind[kind] = checkedPerKind.GetValueOrDefault(kind) + 1;

            try
            {
                if (await ServeAsync(log, copy) != expected)
                {
                    wrong.Add(blobs[b].Name + ": byte " + at + " " + how + ": a different answer");
                }
            }
            catch (Exception error) when (error is not Xunit.Sdk.XunitException)
            {
                // A read that fails is not a miss (ADR 0213).
                failed++;
                wrong.Add(blobs[b].Name + ": byte " + at + " " + how + ": " + error.GetType().Name + ": " + error.Message);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            sweep.Replay + ": " + checkedPerKind.Values.Sum() + " of " + cases.Count + " damaged blobs, " + failed + " failed a read, " + (wrong.Count - failed) + " served a different answer; by kind: "
            + string.Join(", ", checkedPerKind.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value + " (" + perKind[k.Key] + " bytes)")));
        Assert.True(wrong.Count == 0, sweep.Replay + " replays this. " + wrong.Count + " damaged blobs served a different answer or failed a read:\n  " + string.Join("\n  ", wrong.Take(40)));
    }

    private static DatasetOptions Options(TimeProvider? clock = null) =>
        new() { Clock = clock ?? ManualClock.Epoch(), CommitCache = T.CommitCache, MemtableLimit = new QuadCount(8), Maintenance = MaintenanceMode.Off, ExtentMerges = ExtentMergePolicy.Off };

    private static byte[] Set(byte[] bytes, int at, byte value)
    {
        byte[] copy = [.. bytes];
        copy[at] = value;
        return copy;
    }

    private static string KindOf(string name) =>
        name.StartsWith(ExtentSet.Prefix, StringComparison.Ordinal)
            ? name.EndsWith("/terms", StringComparison.Ordinal) ? "set dictionary" : name.EndsWith("/commits", StringComparison.Ordinal) ? "set commit index" : "set graph index"
            : name.StartsWith("index/runs/", StringComparison.Ordinal) ? "run"
            : name.StartsWith("index/commits/", StringComparison.Ordinal) ? "commit index"
            : name == "index/state" ? "state"
            : name == "catalog" ? "catalog"
            : "graph index";

    // Every position's quads, by scan, and every quad any position holds,
    // looked up at every position, with every term rendered.
    private static async Task<string> ServeAsync(List<byte[]> log, List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> blobs)
    {
        MemoryStorage alone = MemoryStorage.FromLog(T.Manifest, log.Select(s => (ReadOnlyMemory<byte>)s), blobs.Select(b => new KeyValuePair<BlobName, ReadOnlyMemory<byte>>(b.Name, b.Bytes)));
        await using Dataset opened = await Dataset.OpenAsync(alone, Options(), T.Ct);
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

            using DatasetView head = opened.Pin();
            served.Add("head: " + string.Join(" ", T.All(head).Select(x => T.Render(head, x)).Order(StringComparer.Ordinal)));

            // The graph-version indexes, at every position, and the commit
            // index, by the time of every commit and between them.
            for (long q = 0; q <= opened.Head.Value; q++)
            {
                foreach (string g in new[] { "g0", "g1", "g2" })
                {
                    served.Add(q + " " + g + " at " + (await opened.GraphVersionAsync(RequestTerm.FromTerm(T.Iri(g)), new Position(q), T.Ct)).Value);
                }
            }

            DateTimeOffset epoch = ManualClock.Epoch().Now;

            for (int minute = 0; minute <= opened.Head.Value + 1; minute++)
            {
                served.Add("at minute " + minute + ": " + opened.PositionAt(new CommitTimestamp(epoch.AddMinutes(minute))).Value
                    + ", and a half: " + opened.PositionAt(new CommitTimestamp(epoch.AddMinutes(minute + 0.5))).Value);
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
