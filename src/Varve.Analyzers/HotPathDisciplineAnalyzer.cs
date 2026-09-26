// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0003. Hot-path discipline: inside a <c>[HotPath]</c> member, nothing
/// that allocates, and no call to code that is not itself held to the rule
/// (ADR 0064).
/// </summary>
/// <remarks>
/// <para>
/// Constraint 5 says allocation per quad is a defect. A benchmark finds that
/// after the code is written; this finds the class of code that causes it
/// while it is being written. The allocation tests stay as the second net.
/// </para>
/// <para>
/// What it reports inside a hot path: a boxing conversion; a lambda or local
/// function that captures; <c>new</c> of an array or a reference type,
/// collection expressions included; string concatenation and interpolation; a
/// <c>params</c> call that builds an array; LINQ; <c>foreach</c> over an
/// enumerator that is not a struct; <c>async</c>; and a call to a member that
/// is not <c>[HotPath]</c>, unless its type is a BCL type on the allow-list in
/// <c>varve_hot_path_allowed_types</c>.
/// </para>
/// <para>
/// Two things are deliberately not findings. A <c>throw</c> expression and
/// everything under it: the exception and its message are built on the path
/// that ends the operation, which is not the path per quad, and a hot path that
/// could not report a malformed input would have to be wrong about it instead.
/// And an operation that compiles to an instruction rather than a call — an
/// array's length, a built-in operator — calls nothing.
/// </para>
/// <para>
/// The exception path is <c>[DesignDecision]</c> on the member or its type,
/// citing a filed decision (ADR 0062). A callee may also declare itself safe to
/// call from a hot path with <c>[DesignDecision(..., Scope =
/// ExceptionScope.HotPath)]</c>, which is how a decision that one ordinary
/// helper is cheap enough gets recorded once rather than at every caller.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotPathDisciplineAnalyzer : DiagnosticAnalyzer
{
    internal const string Boxes = "boxes '{0}'";
    internal const string BoxesDecide = "keep the value typed, with a generic constrained to the struct or an overload that takes it";

    internal const string Captures = "declares a {0} that captures '{1}'";
    internal const string CapturesDecide = "make it static and pass what it needs as arguments, or write the loop inline";

    internal const string AllocatesArray = "allocates an array of '{0}'";
    internal const string AllocatesArrayDecide = "rent it from ArrayPool<T>, take a caller's buffer, or use a stackalloc'd span";

    internal const string AllocatesObject = "allocates a '{0}'";
    internal const string AllocatesObjectDecide = "make it a struct, reuse one the caller owns, or move the allocation out of the loop it is in";

    internal const string BuildsString = "builds a string";
    internal const string BuildsStringDecide = "write UTF-8 into a span the caller supplies, or format outside the hot path";

    internal const string ParamsCall = "passes a params array to '{0}'";
    internal const string ParamsCallDecide = "call an overload that takes the arguments or a span";

    internal const string UsesLinq = "uses LINQ ('{0}')";
    internal const string UsesLinqDecide = "write the loop";

    internal const string ClassEnumerator = "enumerates '{0}' through an enumerator that is a class";
    internal const string ClassEnumeratorDecide = "iterate a span, an array or a type whose GetEnumerator returns a struct";

    internal const string IsAsync = "is async";
    internal const string IsAsyncDecide = "make it synchronous over a span, and keep the I/O outside the hot path";

    internal const string CallsColdMember = "calls '{0}', which is not [HotPath]";
    internal const string CallsColdMemberDecide = "mark '{0}' [HotPath] and hold it to the same rules, or inline what the hot path needs from it";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VarveDiagnostics.HotPathDiscipline);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationBlockStartAction(OnBlockStart);
    }

    private static void OnBlockStart(OperationBlockStartAnalysisContext context)
    {
        ISymbol owner = context.OwningSymbol;

        if (!HotPath.IsHotPath(owner) || HotPath.IsExempted(owner) || IsClassConstruction(owner))
        {
            return;
        }

        if (owner is IMethodSymbol { IsAsync: true } asyncMethod)
        {
            context.RegisterOperationBlockEndAction(
                c => Report(c.ReportDiagnostic, asyncMethod.Locations[0], owner, IsAsync, IsAsyncDecide));
        }

        AllowList allowed = context.OperationBlocks.Length > 0
            ? HotPath.ReadAllowList(context.Options, context.OperationBlocks[0].Syntax.SyntaxTree)
            : AllowList.Empty;

        context.RegisterOperationAction(c => OnConversion(c, owner, allowed), OperationKind.Conversion);
        context.RegisterOperationAction(c => OnAnonymousFunction(c, owner), OperationKind.AnonymousFunction);
        context.RegisterOperationAction(c => OnLocalFunction(c, owner), OperationKind.LocalFunction);
        context.RegisterOperationAction(c => OnArrayCreation(c, owner), OperationKind.ArrayCreation);
        context.RegisterOperationAction(c => OnObjectCreation(c, owner, allowed), OperationKind.ObjectCreation);
        context.RegisterOperationAction(c => OnCollectionExpression(c, owner), OperationKind.CollectionExpression);
        context.RegisterOperationAction(c => OnBinary(c, owner, allowed), OperationKind.Binary);
        context.RegisterOperationAction(c => OnUnary(c, owner, allowed), OperationKind.Unary);
        context.RegisterOperationAction(c => OnInterpolatedString(c, owner), OperationKind.InterpolatedString);
        context.RegisterOperationAction(c => OnInvocation(c, owner, allowed), OperationKind.Invocation);
        context.RegisterOperationAction(c => OnPropertyReference(c, owner, allowed), OperationKind.PropertyReference);
        context.RegisterOperationAction(c => OnForEach(c, owner), OperationKind.Loop);
    }

    private static void OnConversion(OperationAnalysisContext context, ISymbol owner, AllowList allowed)
    {
        IConversionOperation conversion = (IConversionOperation)context.Operation;

        if (OnFailurePath(conversion))
        {
            return;
        }

        if (conversion.OperatorMethod is { } op)
        {
            CheckCallee(context, owner, op, conversion, allowed);
            return;
        }

        ITypeSymbol? from = conversion.Operand.Type;
        ITypeSymbol? to = conversion.Type;

        if (from is { IsValueType: true } && to is { IsReferenceType: true } && !conversion.Operand.ConstantValue.HasValue)
        {
            Report(context.ReportDiagnostic, conversion.Syntax.GetLocation(), owner, Format(Boxes, from), BoxesDecide);
        }
    }

    private static void OnAnonymousFunction(OperationAnalysisContext context, ISymbol owner)
    {
        IAnonymousFunctionOperation lambda = (IAnonymousFunctionOperation)context.Operation;

        if (lambda.Symbol.IsStatic || OnFailurePath(lambda))
        {
            return;
        }

        ReportCaptures(context, owner, lambda.Syntax, "lambda");
    }

    private static void OnLocalFunction(OperationAnalysisContext context, ISymbol owner)
    {
        ILocalFunctionOperation function = (ILocalFunctionOperation)context.Operation;

        if (function.Symbol.IsStatic)
        {
            return;
        }

        ReportCaptures(context, owner, function.Syntax, "local function");
    }

    private static void ReportCaptures(OperationAnalysisContext context, ISymbol owner, SyntaxNode syntax, string kind)
    {
        SemanticModel? model = context.Operation.SemanticModel;

        if (model is null)
        {
            return;
        }

        DataFlowAnalysis? flow = syntax switch
        {
            LocalFunctionStatementSyntax { Body: { } body } => model.AnalyzeDataFlow(body),
            LocalFunctionStatementSyntax { ExpressionBody: { } arrow } => model.AnalyzeDataFlow(arrow.Expression),
            _ => model.AnalyzeDataFlow(syntax),
        };

        if (flow is not { Succeeded: true })
        {
            return;
        }

        // What the function reads or writes from outside itself. 'this' is a
        // capture too: it is what makes an instance lambda a closure over the
        // object.
        foreach (ISymbol captured in flow.CapturedInside)
        {
            Report(context.ReportDiagnostic, syntax.GetLocation(), owner, Format(Captures, kind, captured.Name), CapturesDecide);
            return;
        }

        foreach (ISymbol read in flow.ReadInside)
        {
            if (read is IParameterSymbol { IsThis: true })
            {
                Report(context.ReportDiagnostic, syntax.GetLocation(), owner, Format(Captures, kind, "this"), CapturesDecide);
                return;
            }
        }
    }

    private static void OnArrayCreation(OperationAnalysisContext context, ISymbol owner)
    {
        IArrayCreationOperation creation = (IArrayCreationOperation)context.Operation;

        if (OnFailurePath(creation) || IsParamsArray(creation))
        {
            return;
        }

        string element = creation.Type is IArrayTypeSymbol array ? Display(array.ElementType) : "?";
        Report(context.ReportDiagnostic, creation.Syntax.GetLocation(), owner, Format(AllocatesArray, element), AllocatesArrayDecide);
    }

    private static void OnObjectCreation(OperationAnalysisContext context, ISymbol owner, AllowList allowed)
    {
        IObjectCreationOperation creation = (IObjectCreationOperation)context.Operation;

        if (OnFailurePath(creation) || creation.Type is null)
        {
            return;
        }

        if (creation.Type.IsReferenceType)
        {
            Report(context.ReportDiagnostic, creation.Syntax.GetLocation(), owner, Format(AllocatesObject, creation.Type), AllocatesObjectDecide);
            return;
        }

        // A struct's constructor is a call like any other, unless it is the
        // implicit zeroing one, which is no call at all.
        if (creation.Constructor is { IsImplicitlyDeclared: false } constructor)
        {
            CheckCallee(context, owner, constructor, creation, allowed);
        }
    }

    private static void OnCollectionExpression(OperationAnalysisContext context, ISymbol owner)
    {
        ICollectionExpressionOperation collection = (ICollectionExpressionOperation)context.Operation;

        if (OnFailurePath(collection) || collection.Type is null)
        {
            return;
        }

        // A collection expression targeting a span is stack space or a
        // constant; one targeting anything else is an allocation.
        if (IsSpan(collection.Type))
        {
            return;
        }

        if (collection.Type is IArrayTypeSymbol array)
        {
            if (collection.Elements.Length == 0)
            {
                return;
            }

            Report(context.ReportDiagnostic, collection.Syntax.GetLocation(), owner, Format(AllocatesArray, Display(array.ElementType)), AllocatesArrayDecide);
            return;
        }

        Report(context.ReportDiagnostic, collection.Syntax.GetLocation(), owner, Format(AllocatesObject, collection.Type), AllocatesObjectDecide);
    }

    private static void OnBinary(OperationAnalysisContext context, ISymbol owner, AllowList allowed)
    {
        IBinaryOperation binary = (IBinaryOperation)context.Operation;

        if (OnFailurePath(binary) || binary.ConstantValue.HasValue)
        {
            return;
        }

        if (binary.OperatorKind == BinaryOperatorKind.Add && binary.Type?.SpecialType == SpecialType.System_String)
        {
            // Report the outermost concatenation only: a + b + c is one string.
            if (binary.Parent is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } parent
                && parent.Type?.SpecialType == SpecialType.System_String)
            {
                return;
            }

            Report(context.ReportDiagnostic, binary.Syntax.GetLocation(), owner, BuildsString, BuildsStringDecide);
            return;
        }

        if (binary.OperatorMethod is { } op)
        {
            CheckCallee(context, owner, op, binary, allowed);
        }
    }

    private static void OnUnary(OperationAnalysisContext context, ISymbol owner, AllowList allowed)
    {
        IUnaryOperation unary = (IUnaryOperation)context.Operation;

        if (unary.OperatorMethod is { } op && !OnFailurePath(unary))
        {
            CheckCallee(context, owner, op, unary, allowed);
        }
    }

    private static void OnInterpolatedString(OperationAnalysisContext context, ISymbol owner)
    {
        IInterpolatedStringOperation interpolated = (IInterpolatedStringOperation)context.Operation;

        if (OnFailurePath(interpolated) || interpolated.ConstantValue.HasValue)
        {
            return;
        }

        // An interpolated string handed to a handler parameter (a span
        // formatter's TryWrite, say) builds nothing on the heap.
        if (interpolated.Type?.SpecialType != SpecialType.System_String
            || interpolated.Parent is IInterpolatedStringHandlerCreationOperation)
        {
            return;
        }

        Report(context.ReportDiagnostic, interpolated.Syntax.GetLocation(), owner, BuildsString, BuildsStringDecide);
    }

    private static void OnInvocation(OperationAnalysisContext context, ISymbol owner, AllowList allowed)
    {
        IInvocationOperation invocation = (IInvocationOperation)context.Operation;

        if (OnFailurePath(invocation))
        {
            return;
        }

        IMethodSymbol target = invocation.TargetMethod;
        IMethodSymbol source = target.ReducedFrom ?? target;

        if (source.ContainingType is { } containing
            && containing.ContainingNamespace?.ToDisplayString() == "System.Linq"
            && containing.Name is "Enumerable" or "Queryable")
        {
            Report(context.ReportDiagnostic, invocation.Syntax.GetLocation(), owner, Format(UsesLinq, target.Name), UsesLinqDecide);
            return;
        }

        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (argument.ArgumentKind == ArgumentKind.ParamArray
                || (argument.ArgumentKind == ArgumentKind.ParamCollection && argument.Parameter is { } p && !IsSpan(p.Type)))
            {
                Report(context.ReportDiagnostic, invocation.Syntax.GetLocation(), owner, Format(ParamsCall, Display(target)), ParamsCallDecide);
                break;
            }
        }

        CheckCallee(context, owner, target, invocation, allowed);
    }

    private static void OnPropertyReference(OperationAnalysisContext context, ISymbol owner, AllowList allowed)
    {
        IPropertyReferenceOperation reference = (IPropertyReferenceOperation)context.Operation;

        if (OnFailurePath(reference))
        {
            return;
        }

        // An array's length is an instruction, not a call.
        if (reference.Instance?.Type is IArrayTypeSymbol)
        {
            return;
        }

        CheckCallee(context, owner, reference.Property, reference, allowed);
    }

    private static void OnForEach(OperationAnalysisContext context, ISymbol owner)
    {
        if (context.Operation is not IForEachLoopOperation loop || OnFailurePath(loop))
        {
            return;
        }

        if (loop.Syntax is not CommonForEachStatementSyntax syntax || loop.SemanticModel is not { } model)
        {
            return;
        }

        ITypeSymbol? collection = loop.Collection is IConversionOperation { IsImplicit: true } conversion
            ? conversion.Operand.Type
            : loop.Collection.Type;

        // Arrays and strings are lowered to an index loop, and a span's
        // enumerator is a ref struct.
        if (collection is IArrayTypeSymbol || collection?.SpecialType == SpecialType.System_String || (collection is not null && IsSpan(collection)))
        {
            return;
        }

        ForEachStatementInfo info = model.GetForEachStatementInfo(syntax);
        ITypeSymbol? enumerator = info.GetEnumeratorMethod?.ReturnType;

        if (enumerator is { IsValueType: true })
        {
            return;
        }

        Report(context.ReportDiagnostic, syntax.Expression.GetLocation(), owner, Format(ClassEnumerator, collection?.ToDisplayString() ?? "?"), ClassEnumeratorDecide);
    }

    private static void CheckCallee(OperationAnalysisContext context, ISymbol owner, IMethodSymbol callee, IOperation at, AllowList allowed) =>
        CheckCallee(context, owner, (ISymbol)callee, at, allowed);

    private static void CheckCallee(OperationAnalysisContext context, ISymbol owner, ISymbol callee, IOperation at, AllowList allowed)
    {
        if (IsCallableFromHotPath(callee, allowed))
        {
            return;
        }

        string name = Display(callee);
        Report(context.ReportDiagnostic, at.Syntax.GetLocation(), owner, Format(CallsColdMember, name), Format(CallsColdMemberDecide, name));
    }

    private static bool IsCallableFromHotPath(ISymbol callee, AllowList allowed)
    {
        ISymbol definition = callee.OriginalDefinition;

        // A local function or lambda is part of the member that declares it,
        // and is checked as part of that member's body.
        if (definition is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction })
        {
            return true;
        }

        if (HotPath.IsHotPath(definition) || HotPath.IsDeclaredHotPathSafe(definition))
        {
            return true;
        }

        if (definition is IMethodSymbol { AssociatedSymbol: { } associated } && HotPath.IsDeclaredHotPathSafe(associated))
        {
            return true;
        }

        for (INamedTypeSymbol? type = definition.ContainingType; type is not null; type = type.ContainingType)
        {
            if (allowed.Allows(type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the block builds an instance of a class: a constructor, or a
    /// field or property initializer. That runs when the object is made, and a
    /// class made per quad is reported where it is made, by the hot caller's
    /// <c>new</c>. A struct's constructor runs per value and stays checked.
    /// </summary>
    private static bool IsClassConstruction(ISymbol owner) =>
        owner switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType.IsReferenceType: true } => true,
            IFieldSymbol { IsStatic: false, ContainingType.IsReferenceType: true } => true,
            IPropertySymbol { IsStatic: false, ContainingType.IsReferenceType: true } => true,
            _ => false,
        };

    /// <summary>
    /// Whether the operation is off the path per quad: built only to be
    /// thrown, since anything under a <c>throw</c> is on the path that ends the
    /// operation; or part of an attribute, which is metadata and never runs.
    /// </summary>
    private static bool OnFailurePath(IOperation operation)
    {
        for (IOperation? current = operation; current is not null; current = current.Parent)
        {
            if (current is IThrowOperation or IAttributeOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsParamsArray(IArrayCreationOperation creation) =>
        creation.IsImplicit && creation.Parent is IArgumentOperation { ArgumentKind: ArgumentKind.ParamArray };

    private static bool IsSpan(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true } named
        && named.ContainingNamespace?.ToDisplayString() == "System"
        && named.Name is "Span" or "ReadOnlySpan";

    private static string Display(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private static string Format(string format, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, format, arguments);

    private static void Report(System.Action<Diagnostic> report, Location location, ISymbol owner, string finding, string decide) =>
        report(Diagnostic.Create(VarveDiagnostics.HotPathDiscipline, location, Display(owner), finding, decide));
}
