// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0001. Reports a reference to a Varve assembly whose declared layer is
/// not strictly lower than the declaring assembly's.
/// </summary>
/// <remarks>
/// <para>
/// The package graph is a DAG with fixed layers (ADR 0003). A package may
/// reference only packages in a lower layer, and <em>same-layer references are
/// violations</em>: two packages in one layer that need each other are one
/// package or two layers, and the rule exists to force that question to be
/// answered rather than deferred.
/// </para>
/// <para>
/// An assembly exempt from layering — a test, a benchmark, or the analyzer
/// itself — is skipped. So is a compilation whose own declaration is missing or
/// malformed: VARVE0002 reports that, and reporting both would say the same
/// thing twice.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerDirectionAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VarveDiagnostics.LayerDirection);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        Compilation compilation = context.Compilation;
        string? declaringName = compilation.Assembly.Name;

        if (!LayerDeclaration.IsVarveAssembly(declaringName)
            || LayerDeclaration.IsExemptFromLayering(declaringName))
        {
            return;
        }

        string? declaredValue = LayerDeclaration.ReadDeclaredValue(context.Options.AnalyzerConfigOptionsProvider);
        if (!LayerDeclaration.TryParseLayer(declaredValue, out int declaringLayer))
        {
            // Undeclared or malformed. VARVE0002 owns that message.
            return;
        }

        INamedTypeSymbol? metadataAttribute = LayerDeclaration.ResolveMetadataAttribute(compilation);

        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            string? referencedName = referenced.Name;

            if (!LayerDeclaration.IsVarveAssembly(referencedName))
            {
                continue;
            }

            string? referencedValue = LayerDeclaration.ReadMetadataValue(referenced, metadataAttribute);
            if (!LayerDeclaration.TryParseLayer(referencedValue, out int referencedLayer))
            {
                // Carries no usable layer. VARVE0002 owns that message too, and
                // it is an error in its own right precisely so that this rule
                // cannot be bypassed by omitting the declaration.
                continue;
            }

            if (referencedLayer < declaringLayer)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                VarveDiagnostics.LayerDirection,
                Location.None,
                declaringName,
                declaringLayer,
                referencedName,
                referencedLayer));
        }
    }
}
