// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// The layer rule as a real build sees it.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests construct compilations directly and prove the rule logic.
/// They cannot see the MSBuild wiring, and the wiring has three independent
/// ways to be wrong while the logic is perfectly correct: <c>VarveLayer</c> may
/// not be surfaced to the compiler as a visible property, the layer may not
/// reach the assembly as metadata, and the analyzer may not be referenced as an
/// analyzer at all. Each would leave the rule silently inert, which is a worse
/// failure than a wrong rule because nothing reports it.
/// </para>
/// <para>
/// So these two tests shell out to <c>dotnet build</c> over the fixtures in
/// <c>tests/fixtures/layer-rule/</c>, which inherit the repository's
/// <c>Directory.Build.props</c> and <c>Directory.Build.targets</c> exactly as
/// a real project does.
/// </para>
/// </remarks>
public class LayerRuleFixtureTests
{
    [Fact]
    public async Task A_downward_reference_builds()
    {
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("layer-rule", "conforming", "Varve.Fixture.Consumer", "Varve.Fixture.Consumer.csproj"));

        Assert.True(
            result.ExitCode == 0,
            "Varve.Fixture.Consumer (layer 1) references Varve.Fixture.Base (layer 0) and should build."
            + Environment.NewLine + result.Output);
    }

    [Fact]
    public async Task An_upward_reference_fails_the_build_with_VARVE0001()
    {
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("layer-rule", "violating", "Varve.Fixture.Lower", "Varve.Fixture.Lower.csproj"));

        Assert.True(
            result.ExitCode != 0,
            "Varve.Fixture.Lower (layer 1) references Varve.Fixture.Upper (layer 2) and must not build."
            + Environment.NewLine + result.Output);

        // A non-zero exit code on its own would also be satisfied by a typo in
        // the fixture, so the diagnostic id is the assertion that matters.
        Assert.Contains("VARVE0001", result.Output, StringComparison.Ordinal);
        Assert.Contains("Varve.Fixture.Upper", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Packable is an MSBuild property like any other, and VARVE0002 only sees
    /// it because Directory.Build.props marks it compiler-visible. A unit test
    /// supplies that value itself and so cannot tell whether the wiring exists.
    /// </summary>
    [Fact]
    public async Task A_packable_project_with_a_layer_builds()
    {
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("layer-rule", "packable", "Varve.Fixture.Packaged", "Varve.Fixture.Packaged.csproj"));

        Assert.True(
            result.ExitCode == 0,
            "Varve.Fixture.Packaged is packable and declares layer 0, and should build. This also exercises the "
            + "packable block in Directory.Build.targets, which nothing else in the repository does yet."
            + Environment.NewLine + result.Output);
    }

    [Fact]
    public async Task A_packable_project_declaring_no_layer_fails_the_build_with_VARVE0002()
    {
        BuildResult result = await FixtureBuild.RunAsync(
            Path.Combine("layer-rule", "packable", "Varve.Fixture.PackableNone", "Varve.Fixture.PackableNone.csproj"));

        Assert.True(
            result.ExitCode != 0,
            "Varve.Fixture.PackableNone is packable and declares VarveLayer=none, and must not build."
            + Environment.NewLine + result.Output);

        Assert.Contains("VARVE0002", result.Output, StringComparison.Ordinal);
    }
}
