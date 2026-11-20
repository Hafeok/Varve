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
/// VARVE0006. The Web UI's levels are a dependency rule (ADR 0206 point 4,
/// amended 2026-10-30): a component uses only components of lower levels,
/// never one of its own, no type names a type of a higher level, and only
/// pages and the app use the client's services, which the client's model
/// never names.
/// </summary>
/// <remarks>
/// <para>
/// The levels are namespaces, in order: <c>Varve.WebUi.Atoms</c> &lt;
/// <c>.Molecules</c> &lt; <c>.Organisms</c> &lt; <c>.Templates</c> &lt;
/// <c>.Parts</c> &lt; <c>.Pages</c> &lt; <c>.App</c>. The app is the top: it
/// renders the pages and no level uses it. Three checks, over every name
/// that resolves to a type:
/// </para>
/// <list type="number">
///   <item><b>The level order.</b> A component type (one that implements
///   <c>IComponent</c>) in a level namespace is of a lower level than the type
///   that names it. Its own level is refused too: parts don't use parts, pages
///   don't use pages, and the app's root does not name its layout: the
///   composition root hands it the layout's type. A type that is not a
///   component may be of the user's own level or lower, never higher.</item>
///   <item><b>Services against models.</b> A type in
///   <c>Varve.Protocol.Client.Model</c> is named only in Parts, Pages and App;
///   any other type under <c>Varve.Protocol.Client</c>, the services that call
///   the server, only in Pages and App. This holds for every type of the Web
///   UI, in a level or not: a type outside the levels names no service, so no
///   level reaches the server through it.</item>
///   <item><b>The three assemblies</b> (ADR 0252). The levels span the host,
///   <c>Varve.WebUi</c>, the eager <c>Varve.WebUi.Components</c> that both page
///   sets share, and the lazy <c>Varve.WebUi.Explorer</c>. <c>Components</c>
///   names nothing of the host's or the explorer's, and declares no routed
///   page and nothing of the app; the explorer names nothing of the host's;
///   and the host names nothing of the explorer's, so nothing loads the lazy
///   assembly before its route does.</item>
///   <item><b>The model holds values only.</b> A type in
///   <c>Varve.Protocol.Client.Model</c> names no other type under
///   <c>Varve.Protocol.Client</c>. Otherwise a part could reach a service
///   through the model. This check needs no Blazor, so it runs in the client
///   itself.</item>
/// </list>
/// <para>
/// The rulings it holds, by key, since an analyzer assembly takes no
/// reference to the decision ledger and so cannot carry <c>[Contract]</c>:
/// <c>TheWebUiDesignSource.AtomicLevelsAreADependencyRule</c>,
/// <c>.OnlyPagesCallTheApi</c>, <c>.TheAppIsTheTopLevel</c> and
/// <c>.NoTypeOutsideTheLevelsHoldsAService</c> (ADR 0206);
/// <c>TheWebUiDesignSource.LevelsAcrossThreeAssemblies</c> (ADR 0206, amended
/// by ADR 0252 point 4); and
/// <c>TheWebUisPrivateAssemblies.VarveSixHoldsTheThreeApart</c> (ADR 0252
/// point 5). The last two are filed unaccepted.
/// </para>
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
        ImmutableArray.Create("Atoms", "Molecules", "Organisms", "Templates", "Parts", "Pages", "App");

    // The Web UI's root namespace, every type under which is checked.
    internal const string WebUiNamespace = "Varve.WebUi";

    // Where a type of the Web UI in no level is, for the message.
    internal const string NoLevel = "no level";

    internal const string SameOrHigherFormat = "uses the component '{0}' of level {1}, which is not below its own";

    internal const string SameOrHigherDecide =
        "render the component from a higher level and pass its output in a slot (a RenderFragment) | move a "
        + "component to the level of what it is: domain wording and the client's model to Parts, routed screens "
        + "to Pages";

    internal const string ServiceFormat = "names the client's service type '{0}', which only a page or the app may use";

    internal const string ServiceDecide =
        "read the data in the page and pass it down as parameters, since only a page reads the server | if this "
        + "component is a routed screen, it belongs in Varve.WebUi.Pages; if it is the app's own, such as the layout "
        + "or a service the shell reads, in Varve.WebUi.App";

    internal const string HigherTypeFormat = "names the type '{0}' of level {1}, which is above its own";

    internal const string HigherTypeDecide =
        "pass the value down from the higher level, as a parameter or a cascading value of a type this level may "
        + "name | move the type down to the lowest level that uses it, if it holds no service";

    internal const string ModelFormat = "names the client's model type '{0}', which only a part or a page may use";

    internal const string ModelNamesServiceFormat = "names the client's service type '{0}', which the model never names";

    internal const string ModelNamesServiceDecide =
        "keep the model to values, and let the service that calls the server take or return them | if the type "
        + "named is a value that calls nothing, move it into Varve.Protocol.Client.Model";

    // The assemblies the levels span (ADR 0252).
    internal const string HostAssembly = "Varve.WebUi";

    internal const string ComponentsAssembly = "Varve.WebUi.Components";

    internal const string ExplorerAssembly = "Varve.WebUi.Explorer";

    internal const string AcrossFormat = "names '{0}' of {1}, which {2} never names";

    internal const string ComponentsAcrossDecide =
        "move what both page sets need into Varve.WebUi.Components, and pass the rest down from the page that has it | "
        + "if only one page set uses it, it stays in that set's assembly";

    internal const string ExplorerAcrossDecide =
        "move what the explorer shares with the console into Varve.WebUi.Components | keep the explorer's own copy "
        + "of what only it uses";

    internal const string HostAcrossDecide =
        "reach the explorer's components through the router, from the assemblies the lazy loader returns, never by "
        + "naming a type of it, which would load it with the first screen";

    internal const string ComponentsDeclaresFormat = "is declared in Varve.WebUi.Components at level {0}, which holds no {1}";

    internal const string ComponentsDeclaresDecide =
        "move it to the page set it belongs to: the console's to Varve.WebUi, the explorer's to Varve.WebUi.Explorer | "
        + "if it is a service both page sets use, it belongs in Varve.WebUi.Pages.Services";

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
            // Null without Blazor: then only the model's check runs.
            INamedTypeSymbol? component = start.Compilation.GetTypeByMetadataName(ComponentInterface);

            // The Web UI is the assembly that declares the levels: its types in
            // no level are checked too. Its tests reference the levels, and
            // are not the Web UI.
            bool webUi = DeclaresLevels(start.Compilation.Assembly.GlobalNamespace);
            string assembly = start.Compilation.Assembly.Name;

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, component, webUi, assembly),
                SyntaxKind.IdentifierName,
                SyntaxKind.GenericName);

            if (component is not null && assembly == ComponentsAssembly)
            {
                start.RegisterSymbolAction(symbol => AnalyzeComponentsDeclaration(symbol, component), SymbolKind.NamedType);
            }
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol? component, bool webUi, string assembly)
    {
        if (component is not null && AnalyzeAcross(context, assembly))
        {
            return;
        }

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

        string userNamespace = user.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (IsUnder(userNamespace, ClientModelNamespace))
        {
            AnalyzeModel(context, user, userNamespace);
            return;
        }

        int userLevel = LevelOf(user.ContainingNamespace);

        if (component is null
            || (userLevel < 0 && !(webUi && IsUnder(userNamespace, WebUiNamespace)))
            || context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not INamedTypeSymbol named)
        {
            return;
        }

        INamedTypeSymbol used = named.OriginalDefinition;
        string usedNamespace = used.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (IsUnder(usedNamespace, ClientNamespace))
        {
            bool model = IsUnder(usedNamespace, ClientModelNamespace);

            // Outside the levels, a value of the model is a value; a service
            // would be a way round the levels.
            if (userLevel < 0)
            {
                if (!model)
                {
                    Report(context, user, NoLevel, ServiceFormat, used, ServiceDecide);
                }

                return;
            }

            int lowest = Levels.IndexOf(model ? "Parts" : "Pages");

            if (userLevel < lowest)
            {
                Report(context, user, userLevel, model ? ModelFormat : ServiceFormat, used, model ? ModelDecide : ServiceDecide);
            }

            return;
        }

        int usedLevel = LevelOf(used.ContainingNamespace);

        if (userLevel < 0
            || usedLevel < 0
            || SymbolEqualityComparer.Default.Equals(TopLevelType(used), user)
            || usedLevel < userLevel)
        {
            return;
        }

        if (!Implements(used, component))
        {
            // A value of its own level is the level's; a type of a higher
            // level may hold what this level must not reach.
            if (usedLevel > userLevel)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VarveDiagnostics.ComponentLevel,
                    context.Node.GetLocation(),
                    user.ToDisplayString(),
                    Levels[userLevel],
                    string.Format(System.Globalization.CultureInfo.InvariantCulture, HigherTypeFormat, used.ToDisplayString(), Levels[usedLevel]),
                    HigherTypeDecide));
            }

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

    // The assembly boundaries (ADR 0252); true when one was crossed and reported.
    private static bool AnalyzeAcross(SyntaxNodeAnalysisContext context, string assembly)
    {
        string? decide = assembly switch
        {
            ComponentsAssembly => ComponentsAcrossDecide,
            ExplorerAssembly => ExplorerAcrossDecide,
            HostAssembly => HostAcrossDecide,
            _ => null,
        };

        if (decide is null
            || context.Node.IsPartOfStructuredTrivia()
            || InsideNameOf(context.Node)
            || context.ContainingSymbol is not { } containing
            || TopLevelType(containing) is not { } user
            || context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not INamedTypeSymbol named
            || named.OriginalDefinition.ContainingAssembly?.Name is not { } owner)
        {
            return false;
        }

        bool crossed = assembly switch
        {
            ComponentsAssembly => owner is HostAssembly or ExplorerAssembly,
            ExplorerAssembly => owner == HostAssembly,
            _ => owner == ExplorerAssembly,
        };

        if (!crossed)
        {
            return false;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.ComponentLevel,
            context.Node.GetLocation(),
            user.ToDisplayString(),
            assembly,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, AcrossFormat, named.OriginalDefinition.ToDisplayString(), owner, assembly),
            decide));
        return true;
    }

    // Components holds the design system's levels, the parts both page sets
    // share and Pages.Services: no routed page, nothing of the app.
    private static void AnalyzeComponentsDeclaration(SymbolAnalysisContext context, INamedTypeSymbol component)
    {
        if (context.Symbol is not INamedTypeSymbol type || type.ContainingType is not null)
        {
            return;
        }

        int level = LevelOf(type.ContainingNamespace);
        string space = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        string? held = level == Levels.IndexOf("App") ? "type of the app"
            : level == Levels.IndexOf("Pages") && !space.EndsWith(".Pages.Services", StringComparison.Ordinal) && !space.Contains(".Pages.Services.") ? (Implements(type, component) ? "routed page" : "type of a page outside Pages.Services")
            : null;

        if (held is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                VarveDiagnostics.ComponentLevel,
                type.Locations.Length > 0 ? type.Locations[0] : Location.None,
                type.ToDisplayString(),
                Levels[level],
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ComponentsDeclaresFormat, Levels[level], held),
                ComponentsDeclaresDecide));
        }
    }

    private static void AnalyzeModel(SyntaxNodeAnalysisContext context, INamedTypeSymbol user, string userNamespace)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not INamedTypeSymbol named)
        {
            return;
        }

        INamedTypeSymbol used = named.OriginalDefinition;
        string usedNamespace = used.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (IsUnder(usedNamespace, ClientNamespace) && !IsUnder(usedNamespace, ClientModelNamespace))
        {
            Report(context, user, userNamespace, ModelNamesServiceFormat, used, ModelNamesServiceDecide);
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, INamedTypeSymbol user, int userLevel, string format, INamedTypeSymbol used, string decide) =>
        Report(context, user, Levels[userLevel], format, used, decide);

    private static void Report(SyntaxNodeAnalysisContext context, INamedTypeSymbol user, string where, string format, INamedTypeSymbol used, string decide) =>
        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.ComponentLevel,
            context.Node.GetLocation(),
            user.ToDisplayString(),
            where,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, format, used.ToDisplayString()),
            decide));

    // The level of a namespace: Varve.WebUi.<Level>, or the shared components'
    // or the explorer's own, Varve.WebUi.Components.<Level> and
    // Varve.WebUi.Explorer.<Level> (ADR 0252), or below one; else -1.
    internal static int LevelOf(INamespaceSymbol? space) => LevelOf(space?.ToDisplayString() ?? string.Empty);

    internal static int LevelOf(string name)
    {
        if (!name.StartsWith(LevelPrefix, StringComparison.Ordinal))
        {
            return -1;
        }

        string rest = name.Substring(LevelPrefix.Length);

        foreach (string part in AssemblyParts)
        {
            if (rest.StartsWith(part + ".", StringComparison.Ordinal))
            {
                rest = rest.Substring(part.Length + 1);
                break;
            }
        }

        int dot = rest.IndexOf('.');
        return Levels.IndexOf(dot < 0 ? rest : rest.Substring(0, dot));
    }

    // The namespaces of the host's private assemblies, under Varve.WebUi.
    private static readonly ImmutableArray<string> AssemblyParts = ImmutableArray.Create("Components", "Explorer");

    // Whether the assembly declares a type in a level namespace.
    private static bool DeclaresLevels(INamespaceSymbol global)
    {
        INamespaceSymbol? webUi = null;

        foreach (INamespaceSymbol varve in global.GetNamespaceMembers())
        {
            if (varve.Name == "Varve")
            {
                foreach (INamespaceSymbol member in varve.GetNamespaceMembers())
                {
                    if (member.Name == "WebUi")
                    {
                        webUi = member;
                    }
                }
            }
        }

        if (webUi is null)
        {
            return false;
        }

        foreach (INamespaceSymbol level in webUi.GetNamespaceMembers())
        {
            if (Levels.Contains(level.Name) && level.GetTypeMembers().Length > 0)
            {
                return true;
            }

            if (AssemblyParts.Contains(level.Name))
            {
                foreach (INamespaceSymbol inner in level.GetNamespaceMembers())
                {
                    if (Levels.Contains(inner.Name) && inner.GetTypeMembers().Length > 0)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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
