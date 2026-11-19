// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// Reading the layer declaration a compilation makes about itself.
/// </summary>
/// <remarks>
/// Everything here arrives through <c>CompilerVisibleProperty</c>, read as
/// <c>build_property.*</c>. <c>DecisionDriven.Analyzers</c> makes the four
/// <c>Arch*</c> properties visible, and <c>Directory.Build.props</c> adds
/// <c>IsPackable</c> and <c>OutputType</c> (ADR 0064). A referenced assembly's
/// layer is <c>DD0001</c>'s business, read from the <c>[ArchLayer]</c> the
/// package generates, and nothing here reads metadata.
/// </remarks>
internal static class LayerDeclaration
{
    internal const string LayerKey = "build_property.ArchLayer";
    internal const string CompositionRootKey = "build_property.ArchCompositionRoot";
    internal const string OutputTypeKey = "build_property.OutputType";

    /// <summary>
    /// Whether an assembly is published is the question layering turns on, and
    /// the one an assembly name can only approximate.
    /// </summary>
    internal const string PackableKey = "build_property.IsPackable";

    /// <summary>The assembly name prefix that brings an assembly under the rule.</summary>
    internal const string AssemblyPrefix = "Varve.";

    /// <summary>The analyzer assembly, which has no layer.</summary>
    internal const string AnalyzerAssemblyName = "Varve.Analyzers";

    /// <summary>The host layer: the composition root, and only executables (ADR 0060).</summary>
    internal const int HostLayer = 6;

    private const int LowestLayer = 0;

    /// <summary>Whether the rule applies to an assembly at all.</summary>
    internal static bool IsVarveAssembly(string? assemblyName) =>
        assemblyName is not null
        && assemblyName.StartsWith(AssemblyPrefix, System.StringComparison.Ordinal);

    /// <summary>
    /// Whether an assembly declares no layer by its nature: a test assembly, or
    /// the analyzer, which runs inside the compiler. ADR 0004 records that this
    /// is by name and that no analyzer can close that hole; the packable check
    /// is what stops a package escaping by its name.
    /// </summary>
    internal static bool IsUnlayeredByName(string assemblyName) =>
        assemblyName.EndsWith(".Tests", System.StringComparison.Ordinal)
        || string.Equals(assemblyName, AnalyzerAssemblyName, System.StringComparison.Ordinal);

    /// <summary>A layer is an integer from 0 to 6 inclusive, and nothing else.</summary>
    internal static bool TryParseLayer(string raw, out int layer)
    {
        layer = -1;

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            || parsed is < LowestLayer or > HostLayer)
        {
            return false;
        }

        layer = parsed;
        return true;
    }

    /// <summary>An <c>OutputType</c> of <c>Exe</c> or <c>WinExe</c>, as ADR 0064 defines an executable.</summary>
    internal static bool IsExecutable(string? outputType) =>
        string.Equals(outputType, "Exe", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(outputType, "WinExe", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A boolean property. Absent or unparseable is false: the value that
    /// carries weight is always an explicit opt-in.
    /// </summary>
    internal static bool ReadFlag(AnalyzerConfigOptions options, string key) =>
        string.Equals(Read(options, key), "true", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A property's value, or <see langword="null"/> when it is unset or empty.
    /// MSBuild cannot tell the two apart, and neither can this.
    /// </summary>
    internal static string? Read(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}
