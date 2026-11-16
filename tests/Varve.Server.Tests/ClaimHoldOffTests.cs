// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Store.ObjectStorage.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The hold-off before a member claims a dataset again (ADR 0163, amended
/// 2026-11-10; #251), over a clock the test moves and jitter it draws: a
/// member asks once a round, claims whenever it is not held, and every claim
/// fails, as against a store that fails continuously. The waits start at one
/// round and double to the cap, each between half its step and all of it and
/// never less than one whole round; at the cap the store sees a claim every
/// 15 to 30 seconds and no more; and one success puts the next failure back
/// to one round.
/// </summary>
public sealed class ClaimHoldOffTests
{
    [Fact]
    public void a_store_that_fails_continuously_sees_claims_at_the_capped_rate()
    {
        Gen.Select(Gen.Int[50, 5000], Gen.UInt.Array[64]).Sample((milliseconds, draws) =>
        {
            TimeSpan round = TimeSpan.FromMilliseconds(milliseconds);
            Clock clock = new();
            ClaimHoldOff holdOff = new(round, clock, new Draws(draws));
            List<TimeSpan> claims = [];

            // An hour of rounds; every claim fails at once.
            for (TimeSpan now = TimeSpan.Zero; now < TimeSpan.FromHours(1); now += round)
            {
                clock.Now = now;

                if (!holdOff.Round())
                {
                    claims.Add(now);
                    holdOff.Failed();
                }
            }

            for (int i = 1; i < claims.Count; i++)
            {
                TimeSpan gap = claims[i] - claims[i - 1];
                double doubled = round.TotalSeconds * Math.Pow(2, i - 1);
                TimeSpan step = doubled > ClaimHoldOff.Cap.TotalSeconds ? ClaimHoldOff.Cap : TimeSpan.FromSeconds(doubled);

                // Never sooner than half the step, nor than two rounds: one sat out whole.
                Assert.True(gap >= step / 2 - TimeSpan.FromTicks(1), "claim " + i + " came " + gap + " after the one before, under half of " + step);
                Assert.True(gap >= round * 2, "claim " + i + " came " + gap + " after the one before, with no round sat out");

                // And no later than the step, to the round that follows it.
                Assert.True(gap <= step + round * 2, "claim " + i + " came " + gap + " after the one before, over " + step);
            }

            // At the cap: the last half hour sees a claim every 15 to 30 seconds, a round's grain aside.
            int late = claims.Count(claim => claim >= TimeSpan.FromMinutes(30));
            Assert.InRange(late, (int)(1800 / (30 + (2 * round.TotalSeconds))) - 1, 1800 / 15);
        });
    }

    [Fact]
    public void a_successful_open_puts_the_next_failure_back_to_one_round()
    {
        TimeSpan round = TimeSpan.FromSeconds(1);
        Clock clock = new();
        ClaimHoldOff holdOff = new(round, clock, new Draws([uint.MaxValue]));
        Assert.False(holdOff.Round());

        for (int failure = 0; failure < 10; failure++)
        {
            holdOff.Failed();
        }

        Assert.Equal(10, holdOff.Failures);
        Assert.InRange(holdOff.Wait, ClaimHoldOff.Cap / 2, ClaimHoldOff.Cap);

        holdOff.Succeeded();
        Assert.False(holdOff.Round());
        holdOff.Failed();
        Assert.InRange(holdOff.Wait, round / 2, round);

        // The round after a failure is sat out, however short the wait; the one after it claims.
        clock.Now += round;
        Assert.True(holdOff.Round());
        clock.Now += round;
        Assert.False(holdOff.Round());
    }

    private sealed class Clock : TimeProvider
    {
        internal TimeSpan Now { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now.Ticks;
    }

    private sealed class Draws(uint[] values) : IProbeRandomness
    {
        private int _next;

        public void Fill(Span<byte> destination) => BitConverter.TryWriteBytes(destination, values[_next++ % values.Length]);
    }
}
