// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Varve.Analyzers.Tests;

/// <summary>
/// A compilation with the attributes the hot-path rules match, and the
/// allow-list the repository configures.
/// </summary>
/// <remarks>
/// <para>
/// The attributes are written out here with the full names the generator
/// gives them. That is exactly what the rules depend on: every compilation has
/// its own <c>internal</c> copy, so they are matched by name, never by symbol
/// (ADR 0064). A decision type to cite is written out the same way; whether it
/// is a generated one is DD0007's question, not these rules'.
/// </para>
/// <para>
/// The allow-list arrives as <c>varve_hot_path_allowed_types</c> in a global
/// analyzer config, which is where <c>.editorconfig</c> puts it, and has the
/// value <c>.editorconfig</c> gives it.
/// </para>
/// </remarks>
internal sealed class HotPathTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    internal const string AllowList =
        "System.Span`1, System.ReadOnlySpan`1, System.Runtime.InteropServices.MemoryMarshal, "
        + "System.Buffers.Binary.BinaryPrimitives, System.Runtime.CompilerServices.Unsafe, System.Buffers.ArrayPool`1, "
        + "System.Numerics.Vector*, System.Runtime.Intrinsics.Vector*";

    private const string Attributes = """
        namespace DecisionDriven
        {
            [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Property | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
            internal sealed class HotPathAttribute : System.Attribute
            {
                public HotPathAttribute(System.Type decision) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Interface | System.AttributeTargets.Class | System.AttributeTargets.Delegate)]
            internal sealed class ContractAttribute : System.Attribute
            {
                public ContractAttribute(System.Type decision) { }
                public string Role { get; set; } = "";
            }

            [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
            internal sealed class DesignDecisionAttribute : System.Attribute
            {
                public DesignDecisionAttribute(System.Type decision) { }
                public ExceptionScope Scope { get; set; }
            }

            internal enum ExceptionScope { Boundary, HotPath, Pool, Interop, Compatibility, Migration }
        }

        namespace DecisionDriven.Ledger.Varve
        {
            internal static class BriefConstraints
            {
                internal static class AllocationPerQuadIsADefect { }
            }
        }
        """;

    internal HotPathTest(string source, string allowList = AllowList)
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        TestCode = "using DecisionDriven;" + Environment.NewLine
            + "using DecisionDriven.Ledger.Varve;" + Environment.NewLine
            + source;
        TestState.Sources.Add(("Attributes.cs", Attributes));
        TestState.AnalyzerConfigFiles.Add((
            "/.globalconfig",
            "is_global = true" + Environment.NewLine + "varve_hot_path_allowed_types = " + allowList + Environment.NewLine));
    }

    internal static string Format(string format, params object[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, format, arguments);
}
