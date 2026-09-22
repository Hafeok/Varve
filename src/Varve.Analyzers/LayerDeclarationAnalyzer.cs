// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0002. Reports every way a layer declaration can be missing, invalid, or
/// worked around.
/// </summary>
/// <remarks>
/// <para>
/// This rule exists because VARVE0001 can otherwise be bypassed by omission. An
/// assembly that declares no layer has nothing to compare against, and a
/// referenced assembly with no layer metadata is invisible to the direction
/// check. Either would turn the layering rule off silently, which is worse than
/// turning it off deliberately. See ADR 0003.
/// </para>
/// <para>
/// The value <c>none</c> is a declaration, not an absence. It is accepted from
/// an assembly whose name marks it as a test, benchmark or analyzer assembly,
/// and refused outright from anything packed into a package — whatever it is
/// called. The name check and the <c>IsPackable</c> check catch different
/// things, and it is the pair that closes the hole: a package cannot escape a
/// layer by naming itself <c>Something.Tests</c>, and a library that is not yet
/// packable is still held to its name.
/// </para>
/// <para>
/// The analyzer appearing among a compilation's referenced assemblies is itself
/// a violation. An analyzer is passed to the compiler as <c>/analyzer:</c> and
/// never as <c>/reference:</c>, so it cannot appear there by the wiring this
/// repository uses — if it does, something referenced it as an ordinary
/// library, which is the shape the old by-name exemption used to permit.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerDeclarationAnalyzer : DiagnosticAnalyzer
{
    internal const string MustDeclare =
        "declares no layer. A Varve.* assembly must set the VarveLayer MSBuild property to an integer from 0 to 5, "
        + "or to 'none' if it is a test, benchmark or analyzer assembly (ADR 0003)";

    internal const string NoLayerNotAllowed =
        "declares VarveLayer as 'none', which is allowed only for a test assembly, a benchmark assembly, or "
        + "Varve.Analyzers. A published Varve package has a layer (ADR 0003)";

    internal const string NoLayerOnPackable =
        "declares VarveLayer as 'none' but is packable. Whatever it is named, an assembly that is packed is in the "
        + "package graph the layering rule describes and must declare an integer from 0 to 5. Set a layer, or set "
        + "IsPackable to false (ADR 0003)";

    internal const string AnalyzerReferencedAsLibrary =
        "is referenced as an ordinary library. Varve.Analyzers is a build-time component: it is passed to the "
        + "compiler as an analyzer and must never appear among a compilation's references. Reference it with "
        + "OutputItemType=\"Analyzer\" and ReferenceOutputAssembly=\"false\" (ADR 0004)";

    internal const string MalformedFormat =
        "declares VarveLayer as '{0}', which is not a layer. A layer is an integer from 0 to 5 inclusive, or the "
        + "literal 'none' (ADR 0003)";

    internal const string ReferenceMissingMetadata =
        "is referenced but carries no Varve.Layer assembly metadata, so the direction of the reference cannot be "
        + "checked. Set VarveLayer in the referenced project (ADR 0003)";

    internal const string ReferenceMalformedFormat =
        "is referenced and carries Varve.Layer metadata of '{0}', which is not a layer. A layer is an integer from "
        + "0 to 5 inclusive (ADR 0003)";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VarveDiagnostics.LayerDeclaration);

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

        if (!LayerDeclaration.IsVarveAssembly(declaringName))
        {
            return;
        }

        ReportOwnDeclaration(context, declaringName);
        ReportReferences(context, compilation, declaringName);
    }

    private static void ReportOwnDeclaration(CompilationAnalysisContext context, string? declaringName)
    {
        AnalyzerConfigOptionsProvider options = context.Options.AnalyzerConfigOptionsProvider;
        string? declared = LayerDeclaration.ReadDeclaredValue(options);

        if (declared is null)
        {
            Report(context, declaringName, MustDeclare);
            return;
        }

        if (string.Equals(declared, LayerDeclaration.NoLayer, System.StringComparison.Ordinal))
        {
            // Two independent checks, because a name and a packaging decision
            // answer different questions. Being packed is the one that settles
            // whether an assembly is in the package graph at all.
            if (LayerDeclaration.ReadIsPackable(options))
            {
                Report(context, declaringName, NoLayerOnPackable);
            }
            else if (!LayerDeclaration.IsExemptFromLayering(declaringName))
            {
                Report(context, declaringName, NoLayerNotAllowed);
            }

            return;
        }

        if (!LayerDeclaration.TryParseLayer(declared, out _))
        {
            Report(context, declaringName, string.Format(
                System.Globalization.CultureInfo.InvariantCulture, MalformedFormat, declared));
        }
    }

    private static void ReportReferences(
        CompilationAnalysisContext context, Compilation compilation, string? declaringName)
    {
        INamedTypeSymbol? metadataAttribute = LayerDeclaration.ResolveMetadataAttribute(compilation);

        // Only the analyzer's own test project has a reason to reference it as
        // a library: the tests instantiate the rule types.
        bool mayReferenceTheAnalyzer = string.Equals(
            declaringName, LayerDeclaration.AnalyzerTestAssemblyName, System.StringComparison.Ordinal);

        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            string? referencedName = referenced.Name;

            if (!LayerDeclaration.IsVarveAssembly(referencedName))
            {
                continue;
            }

            if (string.Equals(referencedName, LayerDeclaration.AnalyzerAssemblyName, System.StringComparison.Ordinal))
            {
                // Reaching here at all means it came in as /reference: rather
                // than /analyzer:, which is the wiring mistake the old by-name
                // exemption used to wave through.
                if (!mayReferenceTheAnalyzer)
                {
                    Report(context, referencedName, AnalyzerReferencedAsLibrary);
                }

                continue;
            }

            string? metadata = LayerDeclaration.ReadMetadataValue(referenced, metadataAttribute);

            if (metadata is null)
            {
                Report(context, referencedName, ReferenceMissingMetadata);
                continue;
            }

            if (!LayerDeclaration.TryParseLayer(metadata, out _))
            {
                Report(context, referencedName, string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, ReferenceMalformedFormat, metadata));
            }
        }
    }

    private static void Report(CompilationAnalysisContext context, string? assemblyName, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.LayerDeclaration, Location.None, assemblyName, reason));
}
