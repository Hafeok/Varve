// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0004. A <c>[HotPath]</c> member's signature does not commit its
/// callers to an allocation or to interface dispatch (ADR 0064).
/// </summary>
/// <remarks>
/// <para>
/// VARVE0003 checks a hot path's body. A signature is a promise every caller
/// has to keep, so it is checked separately: an <c>IEnumerable&lt;T&gt;</c>
/// return is an enumerator object on every call, a <c>Task</c> is a state
/// machine, and an interface-typed parameter is a virtual call per element that
/// the JIT cannot see through, usually with a boxed struct behind it.
/// </para>
/// <para>
/// An interface parameter is allowed when the interface is itself a
/// <c>[Contract]</c> marked <c>[HotPath]</c>: somebody decided the contract is
/// on the hot path and holds every implementation to it. A type parameter
/// constrained to an interface is not an interface-typed parameter; that is the
/// shape that lets the JIT specialise, and it is what the rule is steering to.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotPathSignatureAnalyzer : DiagnosticAnalyzer
{
    internal const string ReturnsSequence = "returns '{0}'";
    internal const string ReturnsSequenceDecide = "write into a span the caller supplies, or return a struct cursor the caller advances";

    internal const string TakesSequence = "takes '{0}' as parameter '{1}'";
    internal const string TakesSequenceDecide = "take a ReadOnlySpan<T> or a struct cursor";

    internal const string ReturnsTask = "returns '{0}'";
    internal const string ReturnsTaskDecide = "make it synchronous over a span and keep the I/O outside the hot path";

    internal const string TakesTask = "takes '{0}' as parameter '{1}'";
    internal const string TakesTaskDecide = "await it before the hot path is entered and pass the result";

    internal const string TakesInterface = "takes interface '{0}' as parameter '{1}', which is not a [Contract] marked [HotPath]";
    internal const string TakesInterfaceDecide =
        "take a type parameter constrained to the interface so the JIT can specialise, or a struct | declare the interface a [Contract] and mark it [HotPath], holding every implementation to the rules";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VarveDiagnostics.HotPathSignature);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(OnMethod, SymbolKind.Method);
        context.RegisterSymbolAction(OnProperty, SymbolKind.Property);
    }

    private static void OnMethod(SymbolAnalysisContext context)
    {
        IMethodSymbol method = (IMethodSymbol)context.Symbol;

        // Accessors are reported through their property, and a compiler-made
        // member has no signature anybody wrote.
        if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise
            || method.IsImplicitlyDeclared
            || !HotPath.IsHotPath(method)
            || HotPath.IsExempted(method))
        {
            return;
        }

        Location location = method.Locations.Length > 0 ? method.Locations[0] : Location.None;

        CheckReturn(context, method, method.ReturnType, location);
        CheckParameters(context, method, method.Parameters, location);
    }

    private static void OnProperty(SymbolAnalysisContext context)
    {
        IPropertySymbol property = (IPropertySymbol)context.Symbol;

        if (property.IsImplicitlyDeclared || !HotPath.IsHotPath(property) || HotPath.IsExempted(property))
        {
            return;
        }

        Location location = property.Locations.Length > 0 ? property.Locations[0] : Location.None;

        CheckReturn(context, property, property.Type, location);
        CheckParameters(context, property, property.Parameters, location);
    }

    private static void CheckReturn(SymbolAnalysisContext context, ISymbol member, ITypeSymbol type, Location location)
    {
        if (IsSequence(type))
        {
            Report(context, member, location, Format(ReturnsSequence, type), ReturnsSequenceDecide);
        }
        else if (IsTask(type))
        {
            Report(context, member, location, Format(ReturnsTask, type), ReturnsTaskDecide);
        }
    }

    private static void CheckParameters(
        SymbolAnalysisContext context, ISymbol member, ImmutableArray<IParameterSymbol> parameters, Location fallback)
    {
        foreach (IParameterSymbol parameter in parameters)
        {
            ITypeSymbol type = parameter.Type;
            Location location = parameter.Locations.Length > 0 ? parameter.Locations[0] : fallback;

            if (IsSequence(type))
            {
                Report(context, member, location, Format(TakesSequence, type, parameter.Name), TakesSequenceDecide);
            }
            else if (IsTask(type))
            {
                Report(context, member, location, Format(TakesTask, type, parameter.Name), TakesTaskDecide);
            }
            else if (type.TypeKind == TypeKind.Interface && !IsHotContract(type))
            {
                Report(context, member, location, Format(TakesInterface, type, parameter.Name), TakesInterfaceDecide);
            }
        }
    }

    private static bool IsHotContract(ITypeSymbol type) =>
        HotPath.HasAttribute(type.OriginalDefinition, HotPath.ContractAttributeName)
        && HotPath.HasAttribute(type.OriginalDefinition, HotPath.HotPathAttributeName);

    /// <summary><c>IEnumerable</c> or <c>IEnumerable&lt;T&gt;</c> itself, as the declared type.</summary>
    private static bool IsSequence(ITypeSymbol type) =>
        type.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
            or SpecialType.System_Collections_IEnumerable;

    /// <summary><c>Task</c> or <c>Task&lt;T&gt;</c>. <c>ValueTask</c> is not a Task.</summary>
    private static bool IsTask(ITypeSymbol type) =>
        type is INamedTypeSymbol named
        && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
        && named.Name == "Task";

    private static string Format(string format, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, format, arguments);

    private static void Report(SymbolAnalysisContext context, ISymbol member, Location location, string finding, string decide) =>
        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.HotPathSignature,
            location,
            member.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            finding,
            decide));
}
