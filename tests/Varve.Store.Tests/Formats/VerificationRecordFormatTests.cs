// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Objects;
using Xunit;

namespace Varve.Store.Tests.Formats;

/// <summary>
/// The verification record's format 1 (ADRs 0183, 0216), held by
/// <c>testdata/formats/verification-next</c>: current code writes the
/// fixture's bytes for the fixture's workload, and reads them back as what
/// was written. A byte that changes without a new format version fails here.
/// </summary>
public sealed class VerificationRecordFormatTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task current_code_writes_the_fixtures_bytes_and_reads_them_back()
    {
        string fixture = FormatFixtures.Fixture("verification-next");
        MemoryObjectStore store = new();
        VerificationOptions options = new() { Rounds = new VerificationRounds(8), Randomness = TestRandomness.Instance, Clock = new FixedClock(At) };

        await ObjectStoreVerification.RunAsync(store, options, T.Ct);

        if (Environment.GetEnvironmentVariable("VARVE_WRITE_VERIFICATION_FIXTURE") is { Length: > 0 } write)
        {
            await File.WriteAllBytesAsync(Path.Combine(write, "verification-next", "varve-verification"), store.Get("varve-verification")!, T.Ct);
        }

        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(fixture, "varve-verification"), T.Ct), store.Get("varve-verification"));

        MemoryObjectStore kept = new();
        kept.Put("varve-verification", await File.ReadAllBytesAsync(Path.Combine(fixture, "varve-verification"), T.Ct));
        VerificationRecord record = (await ObjectStoreVerification.ReadRecordAsync(kept, T.Ct))!;
        Assert.True(record.Understood);
        Assert.Equal(new VerificationSuite(1), record.Suite);
        Assert.Equal(new VerificationRounds(8), record.Rounds);
        Assert.Equal(At, record.VerifiedAt.Value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
