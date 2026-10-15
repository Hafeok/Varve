// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// Commits per second from concurrent writers in process, single-quad
/// commits, on the memory backend and on files, with group commit on and off
/// (ADR 0124): the sequencer's own cost, without HTTP.
/// </summary>
internal static class ConcurrentCommitBench
{
    internal static async Task RunAsync(int seconds)
    {
        Console.WriteLine("| Store | Group commit | Writers | Commits/s |");
        Console.WriteLine("|---|---|---:|---:|");

        foreach (string store in (string[])["memory", "file"])
        {
            foreach ((string name, GroupCommitOptions group) in new[] { ("on", GroupCommitOptions.Default), ("off", GroupCommitOptions.Off) })
            {
                foreach (int writers in (int[])[1, 8])
                {
                    string? directory = store == "file" ? System.IO.Directory.CreateTempSubdirectory("varve-concurrent-").FullName : null;
                    FileStorage? files = directory is null ? null : await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
                    IStorage storage = files is null ? new MemoryStorage() : files;
                    StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, GroupCommit = group });
                    long count = 0;
                    await RunForAsync(1);
                    count = 0;
                    await RunForAsync(seconds);

                    async Task RunForAsync(int length)
                    {
                        long end = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * length);
                        await Task.WhenAll(Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
                        {
                            while (Stopwatch.GetTimestamp() < end)
                            {
                                long n = Interlocked.Increment(ref count);
                                await dataset.CommitAsync(new CommitRequest().Assert(Iri("s" + n + "-" + length), Iri("p"), Iri("w" + w)));
                            }
                        })));
                    }

                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {store} | {name} | {writers} | {count / (double)seconds:N0} |"));
                    await dataset.DisposeAsync();

                    if (files is not null)
                    {
                        await files.DisposeAsync();
                        System.IO.Directory.Delete(directory!, recursive: true);
                    }
                }
            }
        }
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://ex/" + local));
}
