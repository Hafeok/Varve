// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Xunit;

namespace Varve.Store.Tests.Formats;

/// <summary>
/// The log and the derived formats, from the fixtures the releases that
/// introduced them wrote (ADR 0216, docs/formats/README.md): current code
/// reads each at every position, and writes the log and the current derived
/// format byte for byte as the fixtures hold them.
/// </summary>
[DesignDecision(typeof(TheFormatInventory.ReadForeverFixturesReadAsWritten), Scope = ExceptionScope.Boundary)]
[DesignDecision(typeof(TheFormatInventory.DerivedFixturesReachTheSameResults), Scope = ExceptionScope.Boundary)]
public sealed class StoreFormatTests
{
    /// <summary>A fixture, and the name its derived directory has in it.</summary>
    public static TheoryData<string, string> Datasets => new()
    {
        { "store-preview.1", "derived.v2" },
        { "store-preview.2", "derived.v3" },
        { "store-next", "derived.v6" },
    };

    [Theory]
    [MemberData(nameof(Datasets))]
    public async Task the_log_and_its_derived_files_read_at_every_position_as_the_release_wrote_them(string name, string derived)
    {
        string fixture = FormatFixtures.Fixture(name);
        List<SortedSet<string>> expected = FormatFixtures.States(fixture);
        Assert.Equal(FormatFixtures.Commits + 1, expected.Count);

        // The log alone: read forever.
        await using (TemporaryDirectory logOnly = new(flushes: false))
        {
            FormatFixtures.Copy(Path.Combine(fixture, "log"), Path.Combine(logOnly.Path, "log"));
            await using FileStorage storage = await logOnly.OpenAsync();
            await using Dataset dataset = await Dataset.OpenAsync(storage, T.Options(), T.Ct);
            Assert.Equal(expected, await FormatFixtures.ViewsAsync(dataset));
        }

        // With the release's derived files: read, or rebuilt where this
        // version does not read them, to the same results.
        await using TemporaryDirectory written = new(flushes: false);
        FormatFixtures.Copy(Path.Combine(fixture, "log"), Path.Combine(written.Path, "log"));
        FormatFixtures.Copy(Path.Combine(fixture, derived), Path.Combine(written.Path, "derived"));
        await using FileStorage withDerived = await written.OpenAsync();
        await using Dataset reopened = await Dataset.OpenAsync(withDerived, T.Options(), T.Ct);
        Assert.Equal(expected, await FormatFixtures.ViewsAsync(reopened));
    }

    [Fact]
    [DesignDecision(typeof(TheFormatInventory.AFormatChangeWithoutANewVersionFails), Scope = ExceptionScope.Boundary)]
    public async Task current_code_writes_the_log_and_the_current_derived_format_as_the_fixtures_hold_them()
    {
        await using TemporaryDirectory directory = new(flushes: false);

        await using (FileStorage storage = await FileStorage.OpenAsync(directory.Directory, new FileStorageOptions { Clock = ManualClock.Epoch() }, T.Ct))
        {
            await FormatFixtures.WriteWorkloadAsync(storage, ManualClock.Epoch());
        }

        // Log format 1, as v0.1.0-preview.1 wrote it.
        Assert.Equal(
            FormatFixtures.Files(Path.Combine(FormatFixtures.Fixture("store-preview.1"), "log")),
            FormatFixtures.Files(Path.Combine(directory.Path, "log")));

        // Derived format 6, as the fixture of the release that introduces it holds it.
        Assert.Equal(
            FormatFixtures.Files(Path.Combine(FormatFixtures.Fixture("store-next"), "derived.v6")),
            FormatFixtures.Files(Path.Combine(directory.Path, "derived")));
    }
}
