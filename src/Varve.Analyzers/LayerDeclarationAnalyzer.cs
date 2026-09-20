using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0002. Reports a Varve assembly that does not declare a usable layer,
/// and a referenced Varve assembly that carries no layer metadata.
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
/// The value <c>none</c> is a declaration, not an absence, and is accepted only
/// from a test assembly, a benchmark assembly, or the analyzer itself.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerDeclarationAnalyzer : DiagnosticAnalyzer
{
    private const string MustDeclare =
        "declares no layer. A Varve.* assembly must set the VarveLayer MSBuild property to an integer from 0 to 5, "
        + "or to 'none' if it is a test, benchmark or analyzer assembly (ADR 0003)";

    private const string NoLayerNotAllowed =
        "declares VarveLayer as 'none', which is allowed only for a test assembly, a benchmark assembly, or "
        + "Varve.Analyzers. A published Varve package has a layer (ADR 0003)";

    private const string MalformedFormat =
        "declares VarveLayer as '{0}', which is not a layer. A layer is an integer from 0 to 5 inclusive, or the "
        + "literal 'none' (ADR 0003)";

    private const string ReferenceMissingMetadata =
        "is referenced but carries no Varve.Layer assembly metadata, so the direction of the reference cannot be "
        + "checked. Set VarveLayer in the referenced project (ADR 0003)";

    private const string ReferenceMalformedFormat =
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
        ReportReferences(context, compilation);
    }

    private static void ReportOwnDeclaration(CompilationAnalysisContext context, string? declaringName)
    {
        string? declared = LayerDeclaration.ReadDeclaredValue(context.Options.AnalyzerConfigOptionsProvider);

        if (declared is null)
        {
            Report(context, declaringName, MustDeclare);
            return;
        }

        if (string.Equals(declared, LayerDeclaration.NoLayer, System.StringComparison.Ordinal))
        {
            if (!LayerDeclaration.IsExemptFromLayering(declaringName))
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

    private static void ReportReferences(CompilationAnalysisContext context, Compilation compilation)
    {
        INamedTypeSymbol? metadataAttribute = LayerDeclaration.ResolveMetadataAttribute(compilation);

        foreach (IAssemblySymbol referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            string? referencedName = referenced.Name;

            if (!LayerDeclaration.IsVarveAssembly(referencedName))
            {
                continue;
            }

            // The analyzer is referenced as an ordinary library by its own test
            // project, and it has no layer to carry. ADR 0004 records that this
            // exemption is by name and that no analyzer can close that hole.
            if (string.Equals(referencedName, LayerDeclaration.AnalyzerAssemblyName, System.StringComparison.Ordinal))
            {
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
