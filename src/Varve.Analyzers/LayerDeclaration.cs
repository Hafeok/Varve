using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// Reading and validating the layer an assembly declares.
/// </summary>
/// <remarks>
/// A layer reaches an analyzer by two different routes, and both are needed.
/// The compilation being analyzed reads its own layer from the
/// <c>VarveLayer</c> MSBuild property, surfaced through
/// <see cref="AnalyzerConfigOptions"/>. An assembly it *references* is already
/// compiled, so its layer is read back from the
/// <c>[assembly: AssemblyMetadata("Varve.Layer", n)]</c> attribute that
/// <c>Directory.Build.targets</c> emitted. See ADR 0003.
/// </remarks>
internal static class LayerDeclaration
{
    /// <summary>The MSBuild property, as the compiler surfaces it.</summary>
    internal const string BuildPropertyKey = "build_property.VarveLayer";

    /// <summary>
    /// The <c>IsPackable</c> MSBuild property, as the compiler surfaces it.
    /// </summary>
    /// <remarks>
    /// Whether an assembly is published is the only question that actually
    /// matters for layering, and it is the one an assembly name can only
    /// approximate. A project that is packed is in the package graph the layer
    /// rule describes, whatever it chose to call itself.
    /// </remarks>
    internal const string PackableBuildPropertyKey = "build_property.IsPackable";

    /// <summary>The assembly metadata key holding a compiled assembly's layer.</summary>
    internal const string MetadataKey = "Varve.Layer";

    /// <summary>The value declaring that an assembly has no layer.</summary>
    internal const string NoLayer = "none";

    /// <summary>The assembly name prefix that brings an assembly under the rule.</summary>
    internal const string AssemblyPrefix = "Varve.";

    /// <summary>The analyzer assembly, which has no layer.</summary>
    internal const string AnalyzerAssemblyName = "Varve.Analyzers";

    /// <summary>
    /// The analyzer's own test assembly — the only compilation with a reason to
    /// reference <see cref="AnalyzerAssemblyName"/> as an ordinary library.
    /// </summary>
    internal const string AnalyzerTestAssemblyName = "Varve.Analyzers.Tests";

    private const int LowestLayer = 0;
    private const int HighestLayer = 5;

    /// <summary>
    /// Whether <paramref name="assemblyName"/> is subject to the layering
    /// rules at all. Only <c>Varve.*</c> assemblies are; anything else in a
    /// compilation's reference set — the BCL, a test framework — is ignored.
    /// </summary>
    internal static bool IsVarveAssembly(string? assemblyName) =>
        assemblyName is not null
        && assemblyName.StartsWith(AssemblyPrefix, System.StringComparison.Ordinal);

    /// <summary>
    /// Whether an assembly may declare <see cref="NoLayer"/>, and is therefore
    /// also exempt from VARVE0001.
    /// </summary>
    /// <remarks>
    /// Test and benchmark assemblies compose across layers by nature — a test
    /// for the evaluator may need a store — and are not published, so the
    /// stability argument behind the layering does not apply to them. The
    /// analyzer itself runs inside the compiler and is not a library in the
    /// package graph. ADR 0004 records that this exemption is by name, and
    /// that no analyzer can close that hole.
    /// </remarks>
    internal static bool IsExemptFromLayering(string? assemblyName) =>
        assemblyName is not null
        && (assemblyName.EndsWith(".Tests", System.StringComparison.Ordinal)
            || assemblyName.EndsWith(".Benchmarks", System.StringComparison.Ordinal)
            || string.Equals(assemblyName, AnalyzerAssemblyName, System.StringComparison.Ordinal));

    /// <summary>
    /// Parses a declared layer. A layer is an integer from 0 to 5 inclusive;
    /// anything else, including <see cref="NoLayer"/>, is not a layer.
    /// </summary>
    internal static bool TryParseLayer(string? raw, out int layer)
    {
        layer = -1;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
        {
            return false;
        }

        if (parsed is < LowestLayer or > HighestLayer)
        {
            return false;
        }

        layer = parsed;
        return true;
    }

    /// <summary>
    /// The raw <c>VarveLayer</c> value the compilation under analysis declares,
    /// or <see langword="null"/> when the property is unset or empty.
    /// </summary>
    internal static string? ReadDeclaredValue(AnalyzerConfigOptionsProvider options) =>
        ReadBuildProperty(options, BuildPropertyKey);

    /// <summary>
    /// Whether the compilation under analysis is packed into a NuGet package.
    /// </summary>
    /// <remarks>
    /// Absent or unparseable is read as not packable. The repository sets
    /// <c>IsPackable</c> to false by default, so the value that carries weight
    /// — true — is always an explicit opt-in.
    /// </remarks>
    internal static bool ReadIsPackable(AnalyzerConfigOptionsProvider options) =>
        string.Equals(
            ReadBuildProperty(options, PackableBuildPropertyKey),
            "true",
            System.StringComparison.OrdinalIgnoreCase);

    private static string? ReadBuildProperty(AnalyzerConfigOptionsProvider options, string key)
    {
        if (!options.GlobalOptions.TryGetValue(key, out string? value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// The layer a referenced assembly carries in its
    /// <c>AssemblyMetadata("Varve.Layer", …)</c> attribute, or
    /// <see langword="null"/> when it carries none.
    /// </summary>
    /// <remarks>
    /// Returns the raw string rather than an integer so that a referenced
    /// assembly compiled from a malformed declaration is reported as malformed
    /// rather than silently treated as absent.
    /// </remarks>
    internal static string? ReadMetadataValue(IAssemblySymbol assembly, INamedTypeSymbol? metadataAttribute)
    {
        if (metadataAttribute is null)
        {
            return null;
        }

        foreach (AttributeData attribute in assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, metadataAttribute))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is not string key
                || !string.Equals(key, MetadataKey, System.StringComparison.Ordinal))
            {
                continue;
            }

            return attribute.ConstructorArguments[1].Value as string;
        }

        return null;
    }

    /// <summary>
    /// The metadata attribute type, resolved once per compilation.
    /// </summary>
    internal static INamedTypeSymbol? ResolveMetadataAttribute(Compilation compilation) =>
        compilation.GetTypeByMetadataName("System.Reflection.AssemblyMetadataAttribute");
}
