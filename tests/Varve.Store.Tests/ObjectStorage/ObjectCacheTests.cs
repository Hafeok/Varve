// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The object-storage backend's local cache, damaged (#126): the commit index
/// below the catalog, kept as a blob in the cache by an earlier instance, cut
/// short or with a byte of what is verified zeroed. Opened again on that
/// cache, the dataset serves what it served before, as a cache miss would:
/// never an error, never a different answer.
/// </summary>
/// <remarks>
/// The entries before the last are not verified when the blob is read below
/// the catalog: only the last is compared with the catalog's (ADR 0139), and
/// a zeroed byte in one of the others is read as an error or as a different
/// history. What keeps such a blob from existing is its publication, a
/// temporary file flushed before its rename (ADR 0073), so a crash leaves a
/// blob whole or absent; that flush stays for this reason (#126).
/// </remarks>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectCacheTests
{
    private const int Commits = 24;

    [Fact]
    public async Task a_commit_index_blob_cut_short_or_with_its_verified_bytes_zeroed_is_a_miss()
    {
        await using ObjectHarness objects = new();
        string cache = Directory.CreateTempSubdirectory("varve-cache-").FullName;

        try
        {
            // A history, and a catalog at its head: the commit index below it
            // is what a later instance reads from the log on first use.
            await using (Backend writer = await objects.OpenAsync(instance: "writer"))
            await using (Dataset dataset = await Dataset.CreateAsync(writer, T.Id, Options(objects), T.Ct))
            {
                for (int i = 1; i <= Commits; i++)
                {
                    objects.Clock.Now = objects.Clock.Now.AddSeconds(1);
                    await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Integer(i.ToString(CultureInfo.InvariantCulture))), T.Ct);
                }

                await dataset.CheckpointAsync(dataset.Head, T.Ct);
            }

            // An instance on the cache reads below the catalog, which keeps
            // the commit index there as a blob in the cache.
            string expected = await ServeAsync(objects, cache);
            string blob = Assert.Single(Directory.GetFiles(Path.Combine(cache, "local", "derived", "index", "commits")));
            byte[] whole = await File.ReadAllBytesAsync(blob, T.Ct);
            List<string> wrong = [];

            // The last entry, the directory after it and the derived header
            // are what a read verifies (storage format, sections 7.1 and 7.4).
            const int EntryBytes = 80;
            int verified = whole.Length - 160 - (12 + (8 * ((Commits + 127) / 128))) - EntryBytes;
            List<(byte[] Damaged, string How)> cases = [];

            for (int at = 0; at < whole.Length; at++)
            {
                // A cut anywhere before the last entry loses the header as a
                // cut inside it does; every sixteenth byte stands for them.
                if (at >= verified || at % 16 == 0)
                {
                    cases.Add((whole[..at], "cut at byte " + at));
                }

                if (at >= verified && whole[at] != 0)
                {
                    byte[] zeroed = [.. whole];
                    zeroed[at] = 0;
                    cases.Add((zeroed, "byte " + at + " zeroed"));
                }
            }

            // A pull request checks every eighth case, from where a seed says,
            // so that pull requests between them check all of them; the
            // nightly run checks every one (docs/testing.md section 8).
            Sweep sweep = Depth.SweepOf("CACHE_DAMAGE", 8);
            int checkedCases = 0;

            for (int i = sweep.First - 1; i < cases.Count; i += sweep.Stride)
            {
                await CheckAsync(cases[i].Damaged, cases[i].How);
                checkedCases++;
            }

            TestContext.Current.TestOutputHelper?.WriteLine(sweep.Replay + ": " + checkedCases + " of " + cases.Count + " damaged blobs");
            Assert.True(wrong.Count == 0, sweep.Replay + " replays this. " + wrong.Count + " damaged blobs were not a miss, of " + whole.Length + " bytes:\n  " + string.Join("\n  ", wrong));

            async Task CheckAsync(byte[] damaged, string how)
            {
                string copy = Directory.CreateTempSubdirectory("varve-cache-").FullName;

                try
                {
                    Copy(cache, copy);
                    await File.WriteAllBytesAsync(Path.Combine(copy, Path.GetRelativePath(cache, blob)), damaged, T.Ct);
                    string served;

                    try
                    {
                        served = await ServeAsync(objects, copy);
                    }
                    catch (Exception error) when (error is not Xunit.Sdk.XunitException)
                    {
                        wrong.Add(how + ": " + error.GetType().Name + ": " + error.Message);
                        return;
                    }

                    if (served != expected)
                    {
                        wrong.Add(how + ": served a different history");
                    }
                }
                finally
                {
                    Directory.Delete(copy, recursive: true);
                }
            }
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static DatasetOptions Options(ObjectHarness objects) => T.Options(objects.Clock, segmentBytes: 2048);

    // What a follower on the cache serves below the catalog: every position
    // as of, by time, and from the feed.
    private static async Task<string> ServeAsync(ObjectHarness objects, string cache)
    {
        ObjectStorageOptions options = objects.Options(ObjectStorageAccess.ReadOnly);
        await using Backend storage = await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = options.Store,
            Prefix = options.Prefix,
            Cache = new CacheDirectory(cache),
            Access = options.Access,
            Instance = options.Instance,
            Clock = options.Clock,
            ProbeRandomness = options.ProbeRandomness,
            CacheBytes = options.CacheBytes,
        }, T.Ct);
        await using Dataset dataset = await Dataset.OpenAsync(storage, Options(objects), T.Ct);
        List<string> served = [];

        for (long q = 0; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            served.Add(q + ": " + string.Join(" ", T.Terms(view)));

            if (q > 0)
            {
                CommitTimestamp at = dataset.TimestampAt(new Position(q));
                served.Add(q + " @" + at.Value.UtcTicks + " -> " + dataset.PositionAt(at).Value);
            }
        }

        await using IAsyncEnumerator<Commit> commits = dataset.Subscribe(new Position(0), SubscriptionFilter.All, T.Ct).GetAsyncEnumerator(T.Ct);

        for (long q = 1; q <= dataset.Head.Value && await commits.MoveNextAsync(); q++)
        {
            served.Add("feed " + commits.Current.Position.Value + " @" + commits.Current.Timestamp.Value.UtcTicks + " +" + commits.Current.Delta.Asserted.Length);
        }

        return string.Join("\n", served);
    }

    private static void Copy(string from, string to)
    {
        foreach (string directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) != "LOCK")
            {
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
            }
        }
    }
}
