// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The writer record's triggers and release reasons by name (ADRs 0136,
/// 0179): every one is written as its name and read back as itself, so the
/// epoch history of a handover reads as it was written. A name this version
/// does not know is refused, never read as another.
/// </summary>
public sealed class WriterRecordFormatTests
{
    internal static readonly (ClaimTrigger Trigger, string Name)[] Triggers =
        [(ClaimTrigger.Restart, "restart"), (ClaimTrigger.Operator, "operator"), (ClaimTrigger.Abdication, "abdication"), (ClaimTrigger.Probe, "probe")];

    internal static readonly (ReleaseReason Reason, string Name)[] Reasons =
    [
        (ReleaseReason.Shutdown, "shutdown"), (ReleaseReason.OperatorTakeover, "operator-takeover"), (ReleaseReason.Placement, "placement"),
        (ReleaseReason.StorageError, "storage-error"), (ReleaseReason.LocalDiskError, "local-disk-error"),
    ];

    [Fact]
    public void every_trigger_and_reason_is_written_by_its_name_and_read_back()
    {
        Assert.Equal(Enum.GetValues<ClaimTrigger>().Order(), Triggers.Select(t => t.Trigger).Order());
        Assert.Equal(Enum.GetValues<ReleaseReason>().Order(), Reasons.Select(r => r.Reason).Order());

        foreach ((ClaimTrigger trigger, string triggerName) in Triggers)
        {
            foreach ((ReleaseReason reason, string reasonName) in Reasons)
            {
                EpochClaim claim = new() { Epoch = new WriterEpoch(3), Holder = new WriterInstance("b"), Trigger = trigger, ClaimedAt = new ClaimTime(DateTimeOffset.UnixEpoch), Released = reason };
                WriterRecord record = new() { Epoch = new WriterEpoch(3), Holder = new WriterInstance("b"), Released = reason, History = [claim] };
                byte[] bytes = WriterRecordFormat.Write(record);
                string json = Encoding.UTF8.GetString(bytes);
                Assert.Contains("\"trigger\": \"" + triggerName + "\"", json, StringComparison.Ordinal);
                Assert.Contains("\"released\": \"" + reasonName + "\"", json, StringComparison.Ordinal);
                WriterRecord read = WriterRecordFormat.Read(bytes);
                Assert.Equal(reason, read.Released);
                Assert.Equal(trigger, read.History[0].Trigger);
                Assert.Equal(reason, read.History[0].Released);
            }
        }

        byte[] unknown = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(WriterRecordFormat.Write(new WriterRecord { Epoch = new WriterEpoch(1), Holder = new WriterInstance("a"), Released = ReleaseReason.Shutdown, History = [] })).Replace("shutdown", "nap", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => WriterRecordFormat.Read(unknown));
    }
}
