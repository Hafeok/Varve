// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.ObjectStorage.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// What readiness reports of a dataset its instance released for
/// storage-error or local-disk-error (ADR 0163, amended 2026-11-10; #251),
/// over a clock the test moves. A member alone whose disk fails cycles:
/// it releases for local-disk-error, stands by through its hold-off, claims
/// in place, writes with its spills failing again, and releases. Through
/// every round of that the dataset is reported degraded with that reason,
/// and the releases counted. It ends only when the instance has written for
/// thirty seconds with no spill failing.
/// </summary>
public sealed class OwnReleaseTests
{
    [Fact]
    public void a_lone_releaser_cycling_on_local_disk_error_is_degraded_through_every_round()
    {
        Clock clock = new() { Now = TimeSpan.FromHours(1) };
        OwnRelease released = new(clock);
        TimeSpan round = TimeSpan.FromSeconds(1);

        void Rounds(int count, bool writing, bool spillFailing)
        {
            for (int i = 0; i < count; i++)
            {
                clock.Now += round;
                released.Round(writing, spillFailing);
                Assert.Equal(ReleaseReason.LocalDiskError, released.Reason);
            }
        }

        Assert.Null(released.Reason);
        DateTimeOffset first = clock.GetUtcNow();

        for (int cycle = 1; cycle <= 20; cycle++)
        {
            released.Released(ReleaseReason.LocalDiskError);
            Assert.Equal(cycle, released.Releases);
            Assert.Equal(first, released.Since);

            // Standing by through the hold-off; then the writer again, its spills soon failing, for longer than the quiet time.
            Rounds(3, writing: false, spillFailing: false);
            Rounds(20, writing: true, spillFailing: false);
            Rounds(40, writing: true, spillFailing: true);
        }

        // A disk that works again: thirty seconds of writing after the first round with no spill failing, and the report ends.
        Rounds(30, writing: true, spillFailing: false);
        clock.Now += round;
        released.Round(writing: true, spillFailing: false);
        Assert.Null(released.Reason);
        Assert.Equal(0, released.Releases);

        // The next release starts a new report.
        released.Released(ReleaseReason.StorageError);
        Assert.Equal(ReleaseReason.StorageError, released.Reason);
        Assert.Equal(1, released.Releases);
        Assert.Equal(clock.GetUtcNow(), released.Since);
    }

    [Fact]
    public void standing_by_never_ends_the_report_and_writing_interrupted_starts_its_thirty_seconds_again()
    {
        Clock clock = new();
        OwnRelease released = new(clock);
        released.Released(ReleaseReason.StorageError);

        for (int second = 0; second < 600; second++)
        {
            clock.Now += TimeSpan.FromSeconds(1);
            released.Round(writing: second % 40 < 25, spillFailing: false);
            Assert.Equal(ReleaseReason.StorageError, released.Reason);
        }
    }

    private sealed class Clock : TimeProvider
    {
        internal TimeSpan Now { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now.Ticks;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero) + Now;
    }
}
