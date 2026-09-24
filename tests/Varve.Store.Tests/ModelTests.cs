// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The model-based property that ties §10 together (ADR 0043): for arbitrary
/// request sequences the store and the reference model agree after every
/// request, and over the finished run on I2, I3, I5, R1, R2, R3, R4, I7, I8
/// and the settings fold.
/// </summary>
public class ModelTests
{
    internal const int Iterations = 1_000;

    [Fact]
    public async Task the_store_agrees_with_the_reference_model()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: Iterations,
            print: script => script.ToString());

        long[] counts = Coverage.Snapshot();
        string report = string.Join(", ", Coverage.Names.Select((n, i) => n + " " + counts[i].ToString(CultureInfo.InvariantCulture)));
        Assert.True(counts.All(c => c >= 10), "A generated case occurred fewer than ten times, so the property is close to vacuous for it: " + report);
        TestContext.Current.TestOutputHelper?.WriteLine("coverage: " + report);
    }

    [Fact]
    public async Task the_model_agrees_when_commits_are_split_into_many_records()
    {
        // A record limit small enough that most commits are several records,
        // and a segment small enough that commits cross segments.
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync(maxRecordBytes: 64, segmentBytes: 1024);
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: Iterations / 3,
            print: script => script.ToString());
    }
}
