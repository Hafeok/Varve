// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>A temporary dataset directory, deleted with its storages.</summary>
internal sealed class TemporaryDirectory : IAsyncDisposable
{
    private readonly List<FileStorage> _opened = [];

    public TemporaryDirectory() =>
        Path = System.IO.Directory.CreateTempSubdirectory("varve-6a-").FullName;

    public string Path { get; }

    public DatasetDirectory Directory => new(Path);

    public static FileStorageOptions Options => new() { Clock = ManualClock.Epoch() };

    public async Task<FileStorage> OpenAsync(FileStorageOptions? options = null)
    {
        FileStorage storage = await FileStorage.OpenAsync(Directory, options ?? Options, T.Ct);
        _opened.Add(storage);
        return storage;
    }

    public FileStorage Open() => OpenAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        foreach (FileStorage storage in _opened)
        {
            await storage.DisposeAsync();
        }

        foreach (string file in System.IO.Directory.GetFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        System.IO.Directory.Delete(Path, recursive: true);
    }
}

public sealed class FileStorageContractTests : StorageContractTests, IAsyncDisposable
{
    private readonly List<TemporaryDirectory> _directories = [];

    protected override Durability Expected => Durability.Synchronised;

    protected override IStorage Create()
    {
        TemporaryDirectory directory = new();
        _directories.Add(directory);
        return directory.Open();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (TemporaryDirectory directory in _directories)
        {
            await directory.DisposeAsync();
        }
    }
}

/// <summary>
/// The storage contract on the file backend under Windows' sharing rules, on any
/// machine: a file system in memory that refuses to rename over an open file, as
/// Windows does. Replacing a blob a reader holds open takes the backend's other
/// path there, and the contract must hold on it too.
/// </summary>
public sealed class FileStorageWindowsSharingContractTests : StorageContractTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-simulated-windows", "dataset"));

    protected override Durability Expected => Durability.Synchronised;

    protected override IStorage Create() =>
        FileStorage.Open(new Faults.SimulatedFileSystem { RefuseReplacingOpenFiles = true }, new DatasetDirectory(Root), TemporaryDirectory.Options);
}

/// <summary>The file backend's own rules: layout, lease, key store, names.</summary>
public sealed class FileStorageTests
{
    [Fact]
    public async Task a_dataset_on_disk_has_the_layout_of_the_storage_format()
    {
        await using TemporaryDirectory directory = new();

        await using (FileStorage storage = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct))
        await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, T.Options(segmentBytes: 1024), T.Ct))
        {
            for (int i = 0; i < 10; i++)
            {
                await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 80))), T.Ct);
            }

            await dataset.CheckpointAsync(new Position(5), T.Ct);
        }

        string log = Path.Combine(directory.Path, "log");
        string derived = Path.Combine(directory.Path, "derived");
        Assert.Equal(96, new FileInfo(Path.Combine(log, "MANIFEST")).Length);
        Assert.Equal("*\n", File.ReadAllText(Path.Combine(derived, ".gitignore")));
        Assert.True(File.Exists(Path.Combine(derived, "checkpoints", "00000000000000000005")));

        string[] segments = [.. Directory.GetFiles(log, "*.seg").Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.True(segments.Length > 1);
        Assert.Equal("00000000.seg", segments[0]);
        Assert.All(segments.SkipLast(1), s => Assert.True((File.GetAttributes(Path.Combine(log, s)) & FileAttributes.ReadOnly) != 0, s + " is sealed"));

        await using FileStorage again = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct);
        await using Dataset reopened = await Dataset.OpenAsync(again, T.Options(segmentBytes: 1024), T.Ct);
        Assert.Equal(new Position(10), reopened.Head);
        Assert.Equal([new Position(5)], reopened.Checkpoints);
        using DatasetView view = await reopened.AsOfAsync(new Position(7), T.Ct);
        Assert.Equal(7, T.All(view).Count);
        Assert.Equal([new BlobName("checkpoints/00000000000000000005")], await again.Derived.ListAsync(T.Ct));
    }

    [Fact]
    public async Task a_directory_is_opened_by_one_storage_at_a_time()
    {
        await using TemporaryDirectory directory = new();
        FileStorage first = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct);

        DatasetLeasedException refused = await Assert.ThrowsAsync<DatasetLeasedException>(async () =>
            await FileStorage.OpenAsync(new DatasetDirectory(directory.Path + Path.DirectorySeparatorChar + "."), TemporaryDirectory.Options, T.Ct));
        Assert.Contains("process " + Environment.ProcessId, refused.Message, StringComparison.Ordinal);

        await first.DisposeAsync();
        await using FileStorage second = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct);
        Assert.Equal(Durability.Synchronised, second.Log.Durability);
    }

    [Theory]
    [InlineData("keys")]
    [InlineData("derived/keys")]
    [InlineData(".")]
    [InlineData("log/../keys")]
    public async Task a_key_store_inside_the_dataset_directory_is_refused(string relative)
    {
        await using TemporaryDirectory directory = new();
        FileStorageOptions options = new() { Clock = ManualClock.Epoch(), KeyStore = new KeyStoreDirectory(Path.Combine(directory.Path, relative)) };

        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(async () => await FileStorage.OpenAsync(directory.Directory, options, T.Ct));
        Assert.Contains("key store", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "log")), "nothing is created before the refusal");
    }

    [Fact]
    public async Task a_key_store_beside_the_dataset_directory_is_allowed()
    {
        await using TemporaryDirectory directory = new();
        FileStorageOptions options = new() { Clock = ManualClock.Epoch(), KeyStore = new KeyStoreDirectory(directory.Path + "-keys") };
        await using FileStorage storage = await FileStorage.OpenAsync(directory.Directory, options, T.Ct);
        Assert.Equal(Durability.Synchronised, storage.Log.Durability);
    }

    [Fact]
    public async Task unpublished_writes_are_deleted_on_open_and_backend_files_are_not_blobs()
    {
        await using TemporaryDirectory directory = new();

        await using (FileStorage storage = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct))
        {
            IBlobWriter abandoned = await storage.Derived.CreateAsync(new BlobName("index/runs/a"), T.Ct);
            await abandoned.WriteAsync(new byte[] { 1, 2, 3 }, T.Ct);

            // A crash: the writer is never published or disposed.
            Assert.Empty(await storage.Derived.ListAsync(T.Ct));
        }

        Assert.NotEmpty(Directory.GetFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));

        await using FileStorage reopened = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(await reopened.Derived.ListAsync(T.Ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await reopened.Derived.CreateAsync(new BlobName("LOCK"), T.Ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await reopened.Derived.CreateAsync(new BlobName("../escape"), T.Ct));
    }

    /// <summary>
    /// ADR 0074's containment rule, as a property over generated paths: a
    /// path built by appending segments to the dataset directory, through
    /// relative forms, <c>..</c> segments and trailing separators, is inside
    /// it exactly when the segments it ends with are; case matters only when
    /// the comparison ignores it (Windows).
    /// </summary>
    [Fact]
    public void containment_is_decided_on_resolved_paths_segment_by_segment()
    {
        Gen<string> segment = Gen.OneOfConst("a", "b", "A", "ds", "DS", "ds2", "keys");
        Gen<string[]> below = segment.Array[0, 3];

        Gen.Select(below, below, Gen.Bool, Gen.Bool, Gen.Bool).Sample(
            (datasetTail, keyTail, detour, trailing, ignoreCase) =>
            {
                string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-containment"));
                string dataset = Path.Combine([root, .. datasetTail]);

                // The key store as the test means it, and as written: the
                // same place, reached by a detour through '..' and with a
                // trailing separator, or relative to the current directory.
                string meant = Path.Combine([root, .. keyTail]);
                string written = meant;

                if (detour)
                {
                    written = Path.Combine(root, "x", "..", Path.GetRelativePath(root, meant));
                }

                if (trailing)
                {
                    written += Path.DirectorySeparatorChar;
                }

                string normalisedDataset = new DatasetDirectory(dataset).Value;
                string normalisedKey = new KeyStoreDirectory(Path.GetRelativePath(Environment.CurrentDirectory, written)).Value;

                bool expected = keyTail.Length >= datasetTail.Length
                    && keyTail.Take(datasetTail.Length).SequenceEqual(datasetTail, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

                Assert.Equal(expected, DirectoryPath.IsWithin(normalisedKey, normalisedDataset, ignoreCase));
            },
            iter: 5_000);
    }

    [Fact]
    public void containment_is_not_a_string_prefix()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-containment"));
        Assert.False(new KeyStoreDirectory(Path.Combine(root, "ds2")).IsWithin(new DatasetDirectory(Path.Combine(root, "ds"))));
        Assert.True(new KeyStoreDirectory(Path.Combine(root, "ds") + Path.DirectorySeparatorChar).IsWithin(new DatasetDirectory(Path.Combine(root, "ds"))));
        Assert.False(DirectoryPath.IsWithin(Path.Combine(root, "DS", "k"), Path.Combine(root, "ds"), ignoreCase: false));
        Assert.True(DirectoryPath.IsWithin(Path.Combine(root, "DS", "k"), Path.Combine(root, "ds"), ignoreCase: true));
        Assert.Equal(OperatingSystem.IsWindows(), new KeyStoreDirectory(Path.Combine(root, "DS", "k")).IsWithin(new DatasetDirectory(Path.Combine(root, "ds"))));
    }
}
