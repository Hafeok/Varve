// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// What makes a symbol a hot path, and what a hot path may call.
/// </summary>
/// <remarks>
/// <para>
/// <c>[HotPath]</c> is <c>DecisionDriven.HotPathAttribute</c>, which the
/// package's generator emits as an <c>internal</c> type into every compilation
/// (ADR 0064, superseding ADR 0026's placement). Every assembly has its own
/// copy, so it is matched by full name and never by symbol identity; that is
/// the technique ADR 0026 chose, now with the generator's name. The same holds
/// for <c>[Contract]</c> and <c>[DesignDecision]</c>.
/// </para>
/// <para>
/// A member is a hot path when it carries the attribute, when it is an accessor
/// of a property that does, when any type containing it does, or when it is a
/// local function or lambda inside one of those: the body of a hot path
/// includes what it declares.
/// </para>
/// </remarks>
internal static class HotPath
{
    internal const string HotPathAttributeName = "DecisionDriven.HotPathAttribute";
    internal const string ContractAttributeName = "DecisionDriven.ContractAttribute";
    internal const string DesignDecisionAttributeName = "DecisionDriven.DesignDecisionAttribute";

    /// <summary>The <c>.editorconfig</c> option holding the BCL allow-list.</summary>
    internal const string AllowedTypesOption = "varve_hot_path_allowed_types";

    /// <summary><c>ExceptionScope.HotPath</c>'s value in the generated enum.</summary>
    private const int HotPathScope = 1;

    /// <summary>
    /// A test assembly (<c>*.Tests</c>) is not checked. It is not shipped, and a
    /// test double implementing a hot contract allocates per call on purpose;
    /// the DD contract rules draw the same line.
    /// </summary>
    internal static bool IsTestAssembly(Compilation compilation) =>
        compilation.AssemblyName is { } name && name.EndsWith(".Tests", System.StringComparison.Ordinal);

    /// <summary>Whether <paramref name="symbol"/> is held to the hot-path rules.</summary>
    /// <remarks>
    /// Marked itself or by a container, or an implementation of a member of a
    /// <c>[HotPath]</c> interface: a contract marked hot holds every
    /// implementation to the rules, or calling it through the interface would
    /// be a hot call into code nobody checked.
    /// </remarks>
    internal static bool IsHotPath(ISymbol? symbol) =>
        IsMarked(symbol) || ImplementsHotInterfaceMember(symbol);

    private static bool ImplementsHotInterfaceMember(ISymbol? symbol)
    {
        ISymbol? member = symbol is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : symbol;

        if (member is not (IMethodSymbol or IPropertySymbol) || member.ContainingType is not { } type)
        {
            return false;
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            foreach (ISymbol candidate in implemented.GetMembers())
            {
                if (IsMarked(candidate)
                    && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(candidate), member))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an interface is on the hot path as a whole: marked itself, or
    /// every member marked. The generated attribute cannot be applied to an
    /// interface today (its usage is methods, properties, classes and structs),
    /// so the second form is the one that compiles; the first is kept for when
    /// it can be (decision-driven-analyzers#61).
    /// </summary>
    internal static bool IsHotInterface(ITypeSymbol type)
    {
        if (IsMarked(type))
        {
            return true;
        }

        bool any = false;

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet })
            {
                continue;
            }

            if (member is not (IMethodSymbol or IPropertySymbol))
            {
                continue;
            }

            if (!IsMarked(member))
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    private static bool IsMarked(ISymbol? symbol)
    {
        for (ISymbol? current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamespaceSymbol)
            {
                return false;
            }

            if (HasAttribute(current, HotPathAttributeName))
            {
                return true;
            }

            if (current is IMethodSymbol { AssociatedSymbol: { } associated }
                && HasAttribute(associated, HotPathAttributeName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="symbol"/>, or anything containing it, carries a
    /// <c>[DesignDecision(..., Scope = ExceptionScope.HotPath)]</c>: the only
    /// exception path for a DD or VARVE rule (ADR 0062), with the scope that
    /// says the exception is about the hot path. A citation with another scope
    /// answers another rule (a pool's for DD0004, say) and exempts nothing
    /// here. Whether the cited decision exists and fits is DD0007's question.
    /// </summary>
    internal static bool IsExempted(ISymbol? symbol)
    {
        for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (IsDeclaredHotPathSafe(current))
            {
                return true;
            }

            if (current is IMethodSymbol { AssociatedSymbol: { } associated } && IsDeclaredHotPathSafe(associated))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a callee accepts a violation of VARVE0003 on its own behalf: a
    /// <c>[DesignDecision(..., Scope = ExceptionScope.HotPath)]</c> on it says
    /// somebody decided it is safe to call from a hot path.
    /// </summary>
    internal static bool IsDeclaredHotPathSafe(ISymbol symbol)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (!IsNamed(attribute.AttributeClass, DesignDecisionAttributeName))
            {
                continue;
            }

            foreach (System.Collections.Generic.KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == "Scope" && argument.Value.Value is int scope && scope == HotPathScope)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="symbol"/> itself carries the attribute named.</summary>
    internal static bool HasAttribute(ISymbol symbol, string fullName)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (IsNamed(attribute.AttributeClass, fullName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNamed(INamedTypeSymbol? type, string fullName) =>
        type is not null
        && type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == fullName;

    /// <summary>
    /// The allow-list, read from <c>.editorconfig</c> for the tree being
    /// analysed. Unset is empty: the list is configuration, not code (ADR
    /// 0064), so nothing is allowed that the repository did not write down.
    /// </summary>
    internal static AllowList ReadAllowList(AnalyzerOptions options, SyntaxTree tree)
    {
        AnalyzerConfigOptions config = options.AnalyzerConfigOptionsProvider.GetOptions(tree);

        if (!config.TryGetValue(AllowedTypesOption, out string? raw) || string.IsNullOrWhiteSpace(raw))
        {
            return AllowList.Empty;
        }

        ImmutableArray<string>.Builder exact = ImmutableArray.CreateBuilder<string>();
        ImmutableArray<string>.Builder prefixes = ImmutableArray.CreateBuilder<string>();

        foreach (string entry in raw.Split(','))
        {
            string name = entry.Trim();

            if (name.Length == 0)
            {
                continue;
            }

            if (name.EndsWith("*", System.StringComparison.Ordinal))
            {
                prefixes.Add(name.Substring(0, name.Length - 1));
            }
            else
            {
                exact.Add(name);
            }
        }

        return new AllowList(exact.ToImmutable(), prefixes.ToImmutable());
    }

    /// <summary>A type's full metadata name: namespace, then nested types, with arity.</summary>
    internal static string MetadataName(INamedTypeSymbol type)
    {
        string name = type.MetadataName;

        for (INamedTypeSymbol? outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            name = outer.MetadataName + "+" + name;
        }

        return type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString() + "." + name
            : name;
    }
}

/// <summary>The BCL types a hot path may call without the callee being <c>[HotPath]</c>.</summary>
internal sealed class AllowList
{
    internal static readonly AllowList Empty = new(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

    private readonly ImmutableArray<string> _exact;
    private readonly ImmutableArray<string> _prefixes;

    internal AllowList(ImmutableArray<string> exact, ImmutableArray<string> prefixes)
    {
        _exact = exact;
        _prefixes = prefixes;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is on the list. Only types from outside
    /// Varve count: the list names BCL types, and a Varve type that happens to
    /// share a name with one is not it.
    /// </summary>
    internal bool Allows(INamedTypeSymbol type)
    {
        if (type.ContainingAssembly is null || LayerDeclaration.IsVarveAssembly(type.ContainingAssembly.Name))
        {
            return false;
        }

        string name = HotPath.MetadataName(type.OriginalDefinition);

        foreach (string exact in _exact)
        {
            if (name == exact)
            {
                return true;
            }
        }

        foreach (string prefix in _prefixes)
        {
            if (name.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
