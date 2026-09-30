// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.CodeAnalysis;

namespace Varve.Analyzers;

/// <summary>
/// Diagnostic identifiers and descriptors for Varve's own rules.
/// </summary>
/// <remarks>
/// <para>
/// Ids are allocated by ADR 0064 and never reused. <c>VARVE0001</c> and
/// <c>VARVE0002</c> are retired, replaced by <c>DD0001</c> and, for the half of
/// <c>VARVE0002</c> it does not cover, <c>VARVE0005</c> (ADR 0062). Their pages
/// stay under <c>docs/rules/</c> so the ids are documented as taken.
/// </para>
/// <para>
/// Each descriptor's help link points at the rule's page, and that page cites
/// the ADR that motivates it. Messages follow the package's shape: the finding,
/// a <c>Decide:</c> line with both paths, and a guard sentence, because whoever
/// reads <c>dotnet build</c> output sees the message and nothing else.
/// </para>
/// </remarks>
internal static class VarveDiagnostics
{
    /// <summary>The category the hot-path rules report under.</summary>
    internal const string HotPathCategory = "Varve.HotPath";

    /// <summary>The category the layer declaration reports under.</summary>
    internal const string LayeringCategory = "Varve.Layering";

    internal const string HotPathDisciplineId = "VARVE0003";
    internal const string HotPathSignatureId = "VARVE0004";
    internal const string LayerDeclarationId = "VARVE0005";

    /// <summary>The guard sentence every rule with a [DesignDecision] path ends with.</summary>
    internal const string Guard =
        "Do not add the attribute without a decision that answers this; if the reason is only that the code "
        + "already looked like this, take the design change.";

    /// <summary>The documented-exception path, as the package words it.</summary>
    internal const string ExceptionPath =
        "mark it [DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.HotPath)] citing the accepted decision "
        + "that says so";

    /// <summary>
    /// VARVE0003. Something inside a <c>[HotPath]</c> member that allocates, or
    /// that leaves the hot path for code nobody has held to it (ADR 0064).
    /// </summary>
    internal static readonly DiagnosticDescriptor HotPathDiscipline = new(
        id: HotPathDisciplineId,
        title: "A hot path does not allocate, and calls only hot-path code",
        messageFormat: "'{0}' is [HotPath] and {1}. Decide: {2} | " + ExceptionPath + ". " + Guard,
        category: HotPathCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Constraint 5: allocation per quad is a defect. A [HotPath] member may not box, capture, allocate an "
            + "array or a reference type, build a string, make a params call, use LINQ, foreach over a class "
            + "enumerator, or be async, and may call only [HotPath] members and the BCL types on the configured "
            + "allow-list (ADR 0064).",
        helpLinkUri: HelpLinkFor(HotPathDisciplineId));

    /// <summary>
    /// VARVE0004. A <c>[HotPath]</c> member's signature that commits every
    /// caller to an allocation or to interface dispatch (ADR 0064).
    /// </summary>
    internal static readonly DiagnosticDescriptor HotPathSignature = new(
        id: HotPathSignatureId,
        title: "A hot path's signature does not force allocation or dispatch",
        messageFormat: "'{0}' is [HotPath] and {1}. Decide: {2} | " + ExceptionPath + ". " + Guard,
        category: HotPathCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A [HotPath] member does not take or return IEnumerable<T> or Task, and takes no interface-typed "
            + "parameter unless that interface is a [Contract] itself marked [HotPath]. Spans, ref structs, ref, "
            + "in and scoped are what a hot path's signature is made of (ADR 0064).",
        helpLinkUri: HelpLinkFor(HotPathSignatureId));

    /// <summary>
    /// VARVE0005. A layer declaration that is missing, malformed, or disagrees
    /// with what the assembly is (ADR 0064, ADR 0060).
    /// </summary>
    internal static readonly DiagnosticDescriptor LayerDeclaration = new(
        id: LayerDeclarationId,
        title: "Layer declaration is missing, malformed, or disagrees with the assembly",
        messageFormat: "Assembly '{0}' {1}. Decide: {2}. The declaration is configuration and has no exception "
            + "path: a Varve assembly that is not placed is checked by nothing, and the answer is to place it.",
        category: LayeringCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every Varve assembly declares ArchLayer, an integer from 0 to 6, unless it is a test assembly or "
            + "Varve.Analyzers; an executable is layer 6 and only an executable is; ArchCompositionRoot is true "
            + "exactly at layer 6. DD0001 is silent on a project that declares no layer, so this is what keeps an "
            + "undeclared one from escaping the layering entirely (ADR 0064).",
        helpLinkUri: HelpLinkFor(LayerDeclarationId),
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    private static string HelpLinkFor(string ruleId) =>
        "https://github.com/Hafeok/Varve/blob/main/docs/rules/" + ruleId + ".md";
}
