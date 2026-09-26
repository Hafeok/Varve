// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// VARVE0005. A Varve assembly declares the layer it is, and the declaration
/// agrees with what the assembly is (ADR 0064, ADR 0060).
/// </summary>
/// <remarks>
/// The properties arrive as <c>build_property.*</c> in a global analyzer
/// config, which is how <c>CompilerVisibleProperty</c> surfaces an MSBuild
/// property. An unset property is absent from the config, not empty, because
/// that is what "undeclared" is. The fixture tests prove the wiring.
/// </remarks>
public class LayerDeclarationAnalyzerTests
{
    private static Test Declaring(
        string assemblyName,
        string? layer,
        bool packable = false,
        bool executable = false,
        bool compositionRoot = false) =>
        new(assemblyName, layer, packable, executable, compositionRoot);

    private static DiagnosticResult Violation(string assemblyName, string finding, string decide) =>
        new DiagnosticResult(VarveDiagnostics.LayerDeclaration)
            .WithNoLocation()
            .WithArguments(assemblyName, finding, decide);

    private static string Format(string format, object argument) =>
        string.Format(CultureInfo.InvariantCulture, format, argument);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    public async Task A_library_declaring_a_layer_below_6_is_clean(string layer) =>
        await Declaring("Varve.Rdf", layer, packable: true).RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_host_at_layer_6_that_is_the_composition_root_is_clean() =>
        await Declaring("Varve.AotSmoke", "6", executable: true, compositionRoot: true)
            .RunAsync(TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("Varve.Rdf.Tests")]
    [InlineData("Varve.Analyzers")]
    public async Task A_test_assembly_or_the_analyzer_declaring_no_layer_is_clean(string name) =>
        await Declaring(name, layer: null, executable: name.EndsWith(".Tests", StringComparison.Ordinal))
            .RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_assembly_outside_the_family_is_not_checked() =>
        await Declaring("RepoStandard", layer: null).RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_library_declaring_no_layer_is_reported()
    {
        Test test = Declaring("Varve.Something", layer: null);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Something", LayerDeclarationAnalyzer.MustDeclare, LayerDeclarationAnalyzer.MustDeclareDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_packable_assembly_declaring_no_layer_is_reported_whatever_it_is_called()
    {
        Test test = Declaring("Varve.Something.Tests", layer: null, packable: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Something.Tests",
            LayerDeclarationAnalyzer.PackableMustDeclare,
            LayerDeclarationAnalyzer.PackableMustDeclareDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_test_assembly_declaring_a_layer_is_reported()
    {
        Test test = Declaring("Varve.Rdf.Tests", "1", executable: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Rdf.Tests",
            Format(LayerDeclarationAnalyzer.UnlayeredDeclaresFormat, "1"),
            LayerDeclarationAnalyzer.UnlayeredDeclaresDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("one")]
    [InlineData("-1")]
    [InlineData("7")]
    [InlineData("2.0")]
    [InlineData("none")]
    public async Task A_malformed_layer_is_reported(string layer)
    {
        Test test = Declaring("Varve.Rdf", layer);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Rdf",
            Format(LayerDeclarationAnalyzer.MalformedFormat, layer),
            LayerDeclarationAnalyzer.MalformedDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_executable_below_layer_6_is_reported()
    {
        Test test = Declaring("Varve.Tool", "5", executable: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Tool",
            Format(LayerDeclarationAnalyzer.ExecutableBelowHostFormat, 5),
            LayerDeclarationAnalyzer.ExecutableBelowHostDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_library_at_layer_6_is_reported()
    {
        Test test = Declaring("Varve.Server.Parts", "6", compositionRoot: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Server.Parts",
            LayerDeclarationAnalyzer.HostNotExecutable,
            LayerDeclarationAnalyzer.HostNotExecutableDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_composition_root_below_layer_6_is_reported()
    {
        Test test = Declaring("Varve.Store", "4", compositionRoot: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Store",
            Format(LayerDeclarationAnalyzer.RootWithoutHostFormat, 4),
            LayerDeclarationAnalyzer.RootWithoutHostDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_host_at_layer_6_that_is_not_the_composition_root_is_reported()
    {
        Test test = Declaring("Varve.AotSmoke", "6", executable: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.AotSmoke",
            LayerDeclarationAnalyzer.HostWithoutRoot,
            LayerDeclarationAnalyzer.HostWithoutRootDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_test_assembly_claiming_to_be_the_composition_root_is_reported()
    {
        Test test = Declaring("Varve.Rdf.Tests", layer: null, executable: true, compositionRoot: true);
        test.ExpectedDiagnostics.Add(Violation(
            "Varve.Rdf.Tests",
            Format(LayerDeclarationAnalyzer.RootWithoutHostFormat, "none"),
            LayerDeclarationAnalyzer.RootWithoutHostDecide));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>One compilation with a name and the build properties VARVE0005 reads.</summary>
    private sealed class Test : CSharpAnalyzerTest<LayerDeclarationAnalyzer, DefaultVerifier>
    {
        internal Test(string assemblyName, string? layer, bool packable, bool executable, bool compositionRoot)
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            TestCode = "namespace Declaring { internal sealed class Marker { } }";

            string config = "is_global = true" + Environment.NewLine
                + "build_property.IsPackable = " + (packable ? "true" : "false") + Environment.NewLine
                + "build_property.OutputType = " + (executable ? "Exe" : "Library") + Environment.NewLine
                + "build_property.ArchCompositionRoot = " + (compositionRoot ? "true" : "false") + Environment.NewLine;

            if (layer is not null)
            {
                config += "build_property.ArchLayer = " + layer + Environment.NewLine;
            }

            TestState.AnalyzerConfigFiles.Add(("/.globalconfig", config));

            SolutionTransforms.Add((solution, projectId) =>
                solution.WithProjectAssemblyName(projectId, assemblyName));
        }
    }
}
