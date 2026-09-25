// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// VARVE0001. A reference must be to a strictly lower layer (ADR 0003).
/// </summary>
public class LayerDirectionAnalyzerTests
{
    private static LayerAnalyzerTest<LayerDirectionAnalyzer> Declaring(string assemblyName, string? layer) =>
        new(assemblyName, layer);

    private static DiagnosticResult Violation(
        string declaring, string declaringLayer, string referenced, string referencedLayer) =>
        new DiagnosticResult(VarveDiagnostics.LayerDirection)
            .WithNoLocation()
            .WithArguments(declaring, declaringLayer, referenced, referencedLayer);

    [Fact]
    public async Task Downward_reference_is_clean()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Turtle", "2")
            .ReferencingLayer("Varve.Rdf", 1);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Downward_reference_skipping_a_layer_is_clean()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Store", "4")
            .ReferencingLayer("Varve.Iri", 0);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Upward_reference_is_reported()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Rdf", "1")
            .ReferencingLayer("Varve.Store", 4);

        test.ExpectedDiagnostics.Add(Violation("Varve.Rdf", "1", "Varve.Store", "4"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Same-layer references are violations, not exceptions: two packages in
    /// one layer that need each other are one package or two layers.
    /// </summary>
    [Fact]
    public async Task Same_layer_reference_is_reported()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Turtle", "2")
            .ReferencingLayer("Varve.RdfXml", 2);

        test.ExpectedDiagnostics.Add(Violation("Varve.Turtle", "2", "Varve.RdfXml", "2"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Each_offending_reference_is_reported_once()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Turtle", "2")
            .ReferencingLayer("Varve.Iri", 0)
            .ReferencingLayer("Varve.RdfXml", 2)
            .ReferencingLayer("Varve.Store", 4);

        test.ExpectedDiagnostics.Add(Violation("Varve.Turtle", "2", "Varve.RdfXml", "2"));
        test.ExpectedDiagnostics.Add(Violation("Varve.Turtle", "2", "Varve.Store", "4"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Non_varve_references_are_ignored()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Iri", "0")
            .ReferencingUndeclared("Contoso.Data");

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A missing declaration is VARVE0002's message. Reporting it here as well
    /// would say the same thing twice.
    /// </summary>
    [Fact]
    public async Task Missing_declaration_is_left_to_VARVE0002()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Rdf", layer: null)
            .ReferencingLayer("Varve.Store", 4);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Malformed_declaration_is_left_to_VARVE0002()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Rdf", "one")
            .ReferencingLayer("Varve.Store", 4);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Declaration_of_none_on_a_package_is_left_to_VARVE0002()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Rdf", "none")
            .ReferencingLayer("Varve.Store", 4);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A referenced assembly with no layer metadata is invisible to the
    /// direction check. That is exactly why VARVE0002 reports it as an error in
    /// its own right — otherwise omitting the declaration would be a bypass.
    /// </summary>
    [Fact]
    public async Task Reference_without_layer_metadata_is_left_to_VARVE0002()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Rdf", "1")
            .ReferencingUndeclared("Varve.Store");

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reference_declaring_no_layer_is_left_to_VARVE0002()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Rdf.Tests", "none")
            .ReferencingLayer("Varve.Store", 4)
            .ReferencingLayer("Varve.Rdf", 1);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>ADR 0060: a host at layer 6 composes the integrations at 5.</summary>
    [Fact]
    public async Task A_host_may_reference_an_integration()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Server", "6")
            .ReferencingLayer("Varve.Sparql.Store", 5)
            .ReferencingLayer("Varve.Store", 4);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Referencing_a_host_is_reported()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Varve.Sparql.Store", "5")
            .ReferencingLayer("Varve.Server", 6);

        test.ExpectedDiagnostics.Add(Violation("Varve.Sparql.Store", "5", "Varve.Server", "6"));

        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Assemblies_outside_the_varve_namespace_are_not_checked()
    {
        LayerAnalyzerTest<LayerDirectionAnalyzer> test = Declaring("Contoso.App", layer: null)
            .ReferencingLayer("Varve.Store", 4);

        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
