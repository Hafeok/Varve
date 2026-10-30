// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// A dataset written by an older release, derived format 3. Its full
/// checkpoints carry no block checks, so this release cannot verify them and
/// they are a cache miss (ADR 0213, superseding ADR 0139's migration as it
/// runs): it opens from the log, serves at every position what the log alone
/// gives, and starts a chain of extent sets at its first checkpoint under
/// this release — all without a change to any answer.
/// </summary>
public sealed class Format3Tests
{
    [Fact]
    public async Task a_dataset_written_by_an_older_release_opens_and_serves_identical_results_at_every_position()
    {
        string fixture = Path.Combine(Root(), "testdata", "derived-format-3");
        CheckSums(fixture);

        await using TemporaryDirectory written = new(flushes: false);
        await using TemporaryDirectory logOnly = new(flushes: false);
        Copy(Path.Combine(fixture, "log"), Path.Combine(written.Path, "log"));
        Copy(Path.Combine(fixture, "derived.v3"), Path.Combine(written.Path, "derived"));
        Copy(Path.Combine(fixture, "log"), Path.Combine(logOnly.Path, "log"));

        List<SortedSet<string>> expected;

        await using (FileStorage replayed = await logOnly.OpenAsync())
        await using (Dataset fromLog = await Dataset.OpenAsync(replayed, T.Options(), T.Ct))
        {
            Assert.Empty(fromLog.Checkpoints);
            expected = await ViewsAsync(fromLog);
        }

        Assert.Equal(121, expected.Count);

        await using FileStorage storage = await written.OpenAsync();
        await using Dataset dataset = await Dataset.OpenAsync(storage, T.Options(), T.Ct);

        // The full checkpoints are a miss: the dataset reads the log instead.
        Assert.Empty(dataset.Checkpoints);
        Assert.Equal(expected, await ViewsAsync(dataset));

        // The first checkpoint under this release starts the chain of sets.
        await dataset.CheckpointAsync(new Position(100), T.Ct);
        Assert.Equal([new Position(100)], dataset.Checkpoints);
        Assert.Equal(expected, await ViewsAsync(dataset));

        // Opened again, it opens on the set.
        await dataset.DisposeAsync();
        await using Dataset again = await Dataset.OpenAsync(storage, T.Options(), T.Ct);
        Assert.Equal([new Position(100)], again.Checkpoints);
        Assert.Equal(expected, await ViewsAsync(again));
    }

    private static async Task<List<SortedSet<string>>> ViewsAsync(Dataset dataset)
    {
        List<SortedSet<string>> views = [];

        for (long q = 0; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            views.Add(T.Terms(view));
        }

        return views;
    }

    private static void CheckSums(string fixture)
    {
        foreach (string line in File.ReadAllLines(Path.Combine(fixture, "SHA256SUMS")).Where(l => l.Length > 0))
        {
            string hash = line[..64];
            string file = line[66..];
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture, file)))));
        }
    }

    private static void Copy(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found above " + AppContext.BaseDirectory + ".");
    }
}
