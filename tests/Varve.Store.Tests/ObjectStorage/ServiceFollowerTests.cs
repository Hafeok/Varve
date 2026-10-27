// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The tier-1 follower and a placement handover on the services (ADRs 0163,
/// 0164): on Azurite and on RustFS, a follower tails its writer by probing
/// the next slot; the writer abdicates with the reason placement; the next
/// writer claims after it sees the release; and the follower goes on
/// following the new writer's commits. The server's fleet runs on Azurite
/// (its S3 access is web identity alone, ADR 0137); this is the same
/// contract on both.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ServiceFollowerTests
{
    [Theory]
    [InlineData("azurite")]
    [InlineData("rustfs")]
    public async Task A_follower_tails_its_writer_across_a_placement_handover(string leg)
    {
        IObjectStore store = leg == "azurite" ? await Azurite.ContainerAsync() : await S3Server.RustFs.BucketAsync();
        string caches = Directory.CreateTempSubdirectory("varve-service-follower-").FullName;

        try
        {
            Backend first = await Backend.OpenAsync(Options(store, caches, "a", ObjectStorageAccess.Write), T.Ct);
            Dataset writer = await T.OpenOrCreate(first, T.Options());
            await CommitAsync(writer, 0, 5);

            Backend tailing = await Backend.OpenAsync(Options(store, caches, "f", ObjectStorageAccess.ReadOnly), T.Ct);
            await using Dataset follower = await Dataset.OpenFollowerAsync(tailing, T.Options(), T.Ct);
            await follower.AdvanceAsync(T.Ct);
            Assert.Equal(writer.Head, follower.Head);

            // The old writer abdicates for placement.
            first.ReleaseOnClose = ReleaseReason.Placement;
            await writer.DisposeAsync();
            await first.DisposeAsync();
            WriterRecord released = (await Backend.ReadWriterAsync(store, null, T.Ct))!;
            Assert.Equal(ReleaseReason.Placement, released.Released);

            // The newly placed writer claims, having seen the release.
            await using Backend second = await Backend.OpenAsync(Options(store, caches, "b", ObjectStorageAccess.Write), T.Ct);
            await using Dataset next = await T.OpenOrCreate(second, T.Options());
            await CommitAsync(next, 5, 3);
            WriterRecord claimed = (await Backend.ReadWriterAsync(store, null, T.Ct))!;
            Assert.Equal("b", claimed.Holder.Value);
            Assert.Equal(ReleaseReason.Placement, claimed.History[^2].Released);

            // The follower follows the new writer.
            await follower.AdvanceAsync(T.Ct);
            Assert.Equal(8L, follower.Head.Value);
            using DatasetView view = follower.Pin();
            Assert.True(view.TryInternalise(RdfTerm.Iri("http://ex/s7"u8), out _));
            await tailing.DisposeAsync();
        }
        finally
        {
            (store as IDisposable)?.Dispose();
            Directory.Delete(caches, recursive: true);
        }
    }

    private static ObjectStorageOptions Options(IObjectStore store, string caches, string instance, ObjectStorageAccess access) => new()
    {
        Store = store,
        Cache = new CacheDirectory(Directory.CreateDirectory(Path.Combine(caches, instance)).FullName),
        Access = access,
        Instance = new WriterInstance(instance),
        Clock = TimeProvider.System,
        ProbeRandomness = TestRandomness.Instance,
        CacheBytes = new ByteCount(64L << 20),
    };

    private static async Task CommitAsync(Dataset dataset, int from, int count)
    {
        for (int i = from; i < from + count; i++)
        {
            CommitResult result = await dataset.CommitAsync(
                new CommitRequestBuilder().Assert(RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/s" + i)), RdfTerm.Iri("http://ex/p"u8), RdfTerm.Literal(Encoding.UTF8.GetBytes(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
                T.Ct);
            Assert.Equal(CommitOutcome.Committed, result.Outcome);
        }
    }
}
