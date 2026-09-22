// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;

namespace Varve.Analyzers;

/// <summary>
/// Diagnostic identifiers and descriptors shared by the layering rules.
/// </summary>
/// <remarks>
/// Ids are allocated in <c>docs/adr/0004-enforcement-by-analyzers.md</c> and are
/// never reused. Each descriptor's help link points at the rule's page under
/// <c>docs/rules/</c>, and that page links back to the ADR that motivates it.
/// The chain from a build error to the reasoning behind it is two clicks and is
/// not allowed to break.
/// </remarks>
internal static class VarveDiagnostics
{
    /// <summary>The category every layering rule reports under.</summary>
    internal const string LayeringCategory = "Varve.Layering";

    internal const string LayerDirectionId = "VARVE0001";
    internal const string LayerDeclarationId = "VARVE0002";

    /// <summary>
    /// VARVE0001. A reference to an assembly whose declared layer is not
    /// strictly lower than the declaring assembly's. Same-layer references are
    /// violations; see ADR 0003.
    /// </summary>
    internal static readonly DiagnosticDescriptor LayerDirection = new(
        id: LayerDirectionId,
        title: "Reference is not to a strictly lower layer",
        messageFormat:
            "'{0}' (layer {1}) references '{2}' (layer {3}). A Varve package may reference only packages in a "
            + "strictly lower layer, and a same-layer reference is a violation: the two are one package or two "
            + "layers (ADR 0003).",
        category: LayeringCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The package graph is a DAG with fixed layers. A reference that points up, or sideways within a "
            + "layer, is what turns the graph into something that cannot be published one package at a time.",
        helpLinkUri: HelpLinkFor(LayerDirectionId),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// VARVE0002. Every way a layer declaration can be missing, invalid, or
    /// worked around — each of which would leave VARVE0001 with nothing to
    /// compare against rather than something to fail.
    /// </summary>
    internal static readonly DiagnosticDescriptor LayerDeclaration = new(
        id: LayerDeclarationId,
        title: "Layer declaration is missing, invalid, or worked around",
        messageFormat: "Assembly '{0}' {1}",
        category: LayeringCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every Varve assembly declares its layer through the VarveLayer MSBuild property. Without the "
            + "declaration VARVE0001 has nothing to compare, so an undeclared layer would silently bypass the "
            + "layering rule rather than fail it.",
        helpLinkUri: HelpLinkFor(LayerDeclarationId),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static string HelpLinkFor(string ruleId) =>
        "https://github.com/Hafeok/Varve/blob/main/docs/rules/" + ruleId + ".md";
}
