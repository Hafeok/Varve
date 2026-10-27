// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0006. The Web UI's levels are a dependency rule (ADR 0206 point 4): a
/// component uses only components of lower levels, never one of its own, and
/// only pages use the client's services.
/// </summary>
/// <remarks>
/// <para>
/// The levels are namespaces, in order: <c>Varve.WebUi.Atoms</c> &lt;
/// <c>.Molecules</c> &lt; <c>.Organisms</c> &lt; <c>.Templates</c> &lt;
/// <c>.Parts</c> &lt; <c>.Pages</c>. Two checks, over every name a type in a
/// level namespace writes that resolves to a type:
/// </para>
/// <list type="number">
///   <item><b>The level order.</b> A component type (one that implements
///   <c>IComponent</c>) in a level namespace is of a lower level than the type
///   that names it. Its own level is refused too: parts don't use parts, pages
///   don't use pages.</item>
///   <item><b>Services against models.</b> A type in
///   <c>Varve.Protocol.Client.Model</c> is named only in Parts and Pages; any
///   other type under <c>Varve.Protocol.Client</c>, the services that call the
///   server, only in Pages.</item>
/// </list>
/// <para>
/// Razor compiles a component to C# through a source generator, and that is
/// generated code, which an analyzer neither analyses nor reports in unless it
/// opts in. This rule opts in, and only this rule: the opt-in is per analyzer,
/// so the DD rules stay out of the components, whose parameters are not
/// contracts. A component that renders another names it in
/// <c>OpenComponent&lt;T&gt;</c>, so the generated code is where the edges are.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComponentLevelAnalyzer : DiagnosticAnalyzer
{
    internal const string ComponentInterface = "Microsoft.AspNetCore.Components.IComponent";

    internal const string LevelPrefix = "Varve.WebUi.";

    internal const string ClientNamespace = "Varve.Protocol.Client";

    internal const string ClientModelNamespace = "Varve.Protocol.Client.Model";

    // The levels, lowest first (ADR 0206 point 4).
    internal static readonly ImmutableArray<string> Levels =
        ImmutableArray.Create("Atoms", "Molecules", "Organisms", "Templates", "Parts", "Pages");

    internal const string SameOrHigherFormat = "uses the component '{0}' of level {1}, which is not below its own";

    internal const string SameOrHigherDecide =
        "render the component from a higher level and pass its output in a slot (a RenderFragment) | move a "
        + "component to the level of what it is: domain wording and the client's model to Parts, routed screens "
        + "to Pages";

    internal const string ServiceFormat = "names the client's service type '{0}', which only a page may use";

    internal const string ServiceDecide =
        "read the data in the page and pass it down as parameters, since only a page reads the server | if this "
        + "component is a routed screen, it belongs in Varve.WebUi.Pages";

    internal const string ModelFormat = "names the client's model type '{0}', which only a part or a page may use";

    internal const string ModelDecide =
        "take the values as parameters of the abstract kind this level takes (words, tones, numbers, slots), and "
        + "map the model in a part | if this component holds Varve's domain, it belongs in Varve.WebUi.Parts";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VarveDiagnostics.ComponentLevel);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();

        // The rule's edges are in Razor's generated code. This opt-in is this
        // analyzer's alone (ADR 0206).
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? component = start.Compilation.GetTypeByMetadataName(ComponentInterface);

            if (component is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, component),
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        // A cref in a documentation comment, and a nameof, name a type without
        // using it; Razor writes a nameof for each parameter it sets, beside
        // the OpenComponent that is the use.
        if (context.Node.IsPartOfStructuredTrivia()
            || InsideNameOf(context.Node)
            || context.ContainingSymbol is not { } containing
            || TopLevelType(containing) is not { } user)
        {
            return;
        }

        int userLevel = LevelOf(user.ContainingNamespace);

        if (userLevel < 0
            || context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not INamedTypeSymbol named)
        {
            return;
        }

        INamedTypeSymbol used = named.OriginalDefinition;
        string usedNamespace = used.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (IsUnder(usedNamespace, ClientNamespace))
        {
            bool model = IsUnder(usedNamespace, ClientModelNamespace);
            int lowest = Levels.IndexOf(model ? "Parts" : "Pages");

            if (userLevel < lowest)
            {
                Report(context, user, userLevel, model ? ModelFormat : ServiceFormat, used, model ? ModelDecide : ServiceDecide);
            }

            return;
        }

        int usedLevel = LevelOf(used.ContainingNamespace);

        if (usedLevel < 0
            || SymbolEqualityComparer.Default.Equals(TopLevelType(used), user)
            || !Implements(used, component)
            || usedLevel < userLevel)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.ComponentLevel,
            context.Node.GetLocation(),
            user.ToDisplayString(),
            Levels[userLevel],
            string.Format(System.Globalization.CultureInfo.InvariantCulture, SameOrHigherFormat, used.ToDisplayString(), Levels[usedLevel]),
            SameOrHigherDecide));
    }

    private static void Report(SyntaxNodeAnalysisContext context, INamedTypeSymbol user, int userLevel, string format, INamedTypeSymbol used, string decide) =>
        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.ComponentLevel,
            context.Node.GetLocation(),
            user.ToDisplayString(),
            Levels[userLevel],
            string.Format(System.Globalization.CultureInfo.InvariantCulture, format, used.ToDisplayString()),
            decide));

    // The level of a namespace: Varve.WebUi.<Level> or below it, else -1.
    internal static int LevelOf(INamespaceSymbol? space)
    {
        string name = space?.ToDisplayString() ?? string.Empty;

        if (!name.StartsWith(LevelPrefix, StringComparison.Ordinal))
        {
            return -1;
        }

        string rest = name.Substring(LevelPrefix.Length);
        int dot = rest.IndexOf('.');
        return Levels.IndexOf(dot < 0 ? rest : rest.Substring(0, dot));
    }

    private static bool InsideNameOf(SyntaxNode node)
    {
        for (SyntaxNode? at = node.Parent; at is not null; at = at.Parent)
        {
            if (at is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax { Expression: Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
            {
                return true;
            }

            if (at is Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsUnder(string name, string space) =>
        name == space || name.StartsWith(space + ".", StringComparison.Ordinal);

    private static INamedTypeSymbol? TopLevelType(ISymbol symbol)
    {
        INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol.ContainingType;

        while (type?.ContainingType is { } outer)
        {
            type = outer;
        }

        return type;
    }

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol component)
    {
        foreach (INamedTypeSymbol face in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(face, component))
            {
                return true;
            }
        }

        return false;
    }
}
