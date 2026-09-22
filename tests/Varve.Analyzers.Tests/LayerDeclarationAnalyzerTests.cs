// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// VARVE0002. A Varve assembly declares a layer, and a referenced Varve
/// assembly carries one (ADR 0003).
/// </summary>
/// <remarks>
/// The rule exists because a missing layer would not fail VARVE0001 — it would
/// make VARVE0001 skip the assembly. Every case here is a way that could
/// happen.
/// </remarks>
public class LayerDeclarationAnalyzerTests
{
    private static readonly CompositeFormat MalformedFormat =
        CompositeFormat.Parse(LayerDeclarationAnalyzer.MalformedFormat);

    private static readonly CompositeFormat ReferenceMalformedFormat =
        CompositeFormat.Parse(LayerDeclarationAnalyzer.ReferenceMalformedFormat);

    private static LayerAnalyzerTest<LayerDeclarationAnalyzer> Declaring(
        string assemblyName, string? layer, bool isPackable = false) =>
        new(assemblyName, layer, isPackable);

    private static DiagnosticResult Violation(string assemblyName, string reason) =>
        new DiagnosticResult(VarveDiagnostics.LayerDeclaration)
            .WithNoLocation()
            .WithArguments(assemblyName, reason);

    [Fact]
    public async Task A_declared_layer_is_clean()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "1")
            .ReferencingLayer("Varve.Iri", 0);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("5")]
    public async Task Every_layer_in_range_is_accepted(string layer)
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Something", layer);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Missing_declaration_is_reported()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", layer: null);

        test.ExpectedDiagnostics.Add(Violation("Varve.Rdf", LayerDeclarationAnalyzer.MustDeclare));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("one")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("6")]
    [InlineData("2.0")]
    public async Task Malformed_declaration_is_reported(string layer)
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", layer);

        // An empty value is indistinguishable from an unset property once
        // MSBuild has evaluated it, so it is reported as undeclared.
        string reason = layer.Length == 0
            ? LayerDeclarationAnalyzer.MustDeclare
            : string.Format(CultureInfo.InvariantCulture, MalformedFormat, layer);

        test.ExpectedDiagnostics.Add(Violation("Varve.Rdf", reason));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Declaring_none_on_a_package_is_reported()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "none");

        test.ExpectedDiagnostics.Add(Violation("Varve.Rdf", LayerDeclarationAnalyzer.NoLayerNotAllowed));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Varve.Rdf.Tests")]
    [InlineData("Varve.Rdf.Benchmarks")]
    [InlineData("Varve.Analyzers")]
    public async Task Declaring_none_is_accepted_from_an_exempt_assembly(string assemblyName)
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring(assemblyName, "none");

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The omission bypass this rule exists to close: without this case, a
    /// reference to an assembly that simply never declared a layer would be
    /// invisible to VARVE0001.
    /// </summary>
    [Fact]
    public async Task Reference_without_layer_metadata_is_reported()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "1")
            .ReferencingUndeclared("Varve.Store");

        // Both halves fire, as they would in a real build: the referenced
        // project reports its own missing declaration, and the referencing
        // project reports that it cannot check the direction of the reference.
        test.ExpectedDiagnostics.Add(Violation("Varve.Store", LayerDeclarationAnalyzer.MustDeclare));
        test.ExpectedDiagnostics.Add(
            Violation("Varve.Store", LayerDeclarationAnalyzer.ReferenceMissingMetadata));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reference_with_malformed_layer_metadata_is_reported()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "1")
            .Referencing("Varve.Store", "four");

        test.ExpectedDiagnostics.Add(Violation("Varve.Store", string.Format(
            CultureInfo.InvariantCulture, MalformedFormat, "four")));
        test.ExpectedDiagnostics.Add(Violation("Varve.Store", string.Format(
            CultureInfo.InvariantCulture, ReferenceMalformedFormat, "four")));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The analyzer's own test project instantiates the rule types, so it is
    /// the one compilation with a reason to reference it as a library.
    /// </summary>
    [Fact]
    public async Task The_analyzer_test_project_may_reference_the_analyzer_as_a_library()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Analyzers.Tests", "none")
            .ReferencingNoLayer("Varve.Analyzers");

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An analyzer reaches the compiler as <c>/analyzer:</c> and never as
    /// <c>/reference:</c>, so appearing among the references means someone
    /// referenced it as an ordinary library. That is the shape the old by-name
    /// exemption used to wave through, and it is now the violation.
    /// </summary>
    [Fact]
    public async Task Referencing_the_analyzer_as_a_library_is_reported()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf.Tests", "none")
            .ReferencingNoLayer("Varve.Analyzers");

        test.ExpectedDiagnostics.Add(
            Violation("Varve.Analyzers", LayerDeclarationAnalyzer.AnalyzerReferencedAsLibrary));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_packable_assembly_with_a_layer_is_clean()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "1", isPackable: true)
            .ReferencingLayer("Varve.Iri", 0);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Declaring_none_on_a_packable_assembly_is_reported()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "none", isPackable: true);

        test.ExpectedDiagnostics.Add(Violation("Varve.Rdf", LayerDeclarationAnalyzer.NoLayerOnPackable));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The loophole itself: a name ending in <c>.Tests</c> used to exempt an
    /// assembly from declaring a layer. Being packed now overrides the name,
    /// so a package cannot escape the layering rule by what it calls itself.
    /// </summary>
    [Theory]
    [InlineData("Varve.Rdf.Tests")]
    [InlineData("Varve.Rdf.Benchmarks")]
    [InlineData("Varve.Analyzers")]
    public async Task A_packable_assembly_cannot_buy_an_exemption_with_its_name(string assemblyName)
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring(assemblyName, "none", isPackable: true);

        test.ExpectedDiagnostics.Add(Violation(assemblyName, LayerDeclarationAnalyzer.NoLayerOnPackable));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Non_varve_references_need_no_layer_metadata()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Varve.Rdf", "1")
            .ReferencingUndeclared("Contoso.Data");

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Assemblies_outside_the_varve_namespace_are_not_checked()
    {
        LayerAnalyzerTest<LayerDeclarationAnalyzer> test = Declaring("Contoso.App", layer: null)
            .ReferencingLayer("Varve.Store", 4);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
