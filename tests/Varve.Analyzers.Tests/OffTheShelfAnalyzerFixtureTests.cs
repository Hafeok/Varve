// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// The off-the-shelf analyzers ADR 0004 relies on, proven to fire.
/// </summary>
/// <remarks>
/// <para>
/// BannedApiAnalyzers and PublicApiAnalyzers have been referenced since
/// milestone 1 and had nothing to say, because there was no packable project
/// for them to say it about. Configured is not the same as enforced: a
/// misspelled entry in <c>eng/BannedSymbols.txt</c>, an
/// <c>AdditionalFiles</c> line that does not match, a <c>PrivateAssets</c>
/// mistake — each leaves the analyzer silently inert, and a green build is
/// exactly what a silently inert analyzer produces.
/// </para>
/// <para>
/// These two fixtures exist to fail, and the diagnostic ids asserted here were
/// taken from the output of a real build rather than from memory.
/// </para>
/// </remarks>
public class OffTheShelfAnalyzerFixtureTests
{
    [Fact]
    public async Task Using_a_banned_symbol_fails_the_build_with_RS0030()
    {
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("banned-api", "Varve.Fixture.BannedSymbol", "Varve.Fixture.BannedSymbol.csproj"));

        Assert.True(
            result.ExitCode != 0,
            "Varve.Fixture.BannedSymbol uses System.Uri, which eng/BannedSymbols.txt bans, and must not build."
            + Environment.NewLine + result.Output);

        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);

        // The message is the second half of the point: a ban with no reason is
        // a rule people work around rather than a decision they can read.
        Assert.Contains("Use Varve.Iri", result.Output, StringComparison.Ordinal);
        Assert.Contains("ADR 0004", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_the_clock_in_a_deterministic_project_fails_the_build_with_RS0030()
    {
        // The second list is wired by VarveDeterministic, the property
        // Varve.Store sets. A list that never reaches the analyzer would pass
        // this build silently, which is why the fixture opts in the same way.
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("banned-api", "Varve.Fixture.AmbientClock", "Varve.Fixture.AmbientClock.csproj"));

        Assert.True(
            result.ExitCode != 0,
            "Varve.Fixture.AmbientClock reads DateTimeOffset.UtcNow and Random, which "
            + "eng/BannedSymbols.Deterministic.txt bans, and must not build."
            + Environment.NewLine + result.Output);

        Assert.Contains("RS0030", result.Output, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset.UtcNow", result.Output, StringComparison.Ordinal);
        Assert.Contains("'Random'", result.Output, StringComparison.Ordinal);
        Assert.Contains("ADR 0011", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_public_member_missing_from_the_baseline_fails_the_build_with_RS0016()
    {
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("public-api", "Varve.Fixture.UndeclaredApi", "Varve.Fixture.UndeclaredApi.csproj"));

        Assert.True(
            result.ExitCode != 0,
            "Varve.Fixture.UndeclaredApi has a public type in neither baseline file, and must not build."
            + Environment.NewLine + result.Output);

        Assert.Contains("RS0016", result.Output, StringComparison.Ordinal);
        Assert.Contains("Varve.Fixture.UndeclaredApi.Marker", result.Output, StringComparison.Ordinal);
    }
}
