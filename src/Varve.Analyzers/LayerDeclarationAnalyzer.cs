// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Varve.Analyzers;

/// <summary>
/// VARVE0005. A Varve assembly declares the layer it is, and the declaration
/// agrees with what the assembly is (ADR 0064, ADR 0060).
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the retired <c>VARVE0002</c> that <c>DD0001</c> does not
/// cover. <c>DD0001</c> reports a <em>reference</em> to a family assembly that
/// declares no layer, and is silent on a project that declares none itself.
/// That leaves a host, which nothing references, and a packable project that
/// forgot the property, checked by nothing at all.
/// </para>
/// <para>
/// Three rules, for a compilation whose name starts <c>Varve.</c>:
/// </para>
/// <list type="number">
///   <item>It declares <c>ArchLayer</c>, an integer from 0 to 8, unless it is a
///   test assembly or <c>Varve.Analyzers</c>, which declare none. A packable
///   assembly declares one whatever it is called.</item>
///   <item>An executable declares 6 or above, and a library declares 6 or
///   above only when it is a host's private assembly and names its host in
///   <c>VarveHostPartOf</c>: the Web UI's shared components at 6 and its
///   lazy explorer at 7, below their host at 8 (ADR 0252). A test assembly is
///   an executable under Microsoft.Testing.Platform and declares none, as
///   before.</item>
///   <item><c>ArchCompositionRoot</c> is true exactly when <c>ArchLayer</c> is
///   6 or above. ADR 0060 rejected a second declaration because it can disagree with the
///   first; the package needs the property, so this makes disagreement an
///   error instead.</item>
/// </list>
/// <para>
/// The rulings it holds, by key, since an analyzer assembly takes no
/// reference to the decision ledger and so cannot carry <c>[Contract]</c>:
/// <c>HostsAtLayer6TheCompositionRoot.ExecutablesAreLayer6</c>,
/// <c>.HostLayersSixToEight</c> and <c>.PrivateAssembliesOfAHost</c> (ADR 0060,
/// amended 2026-11-19 by ADR 0252 points 2 and 3); the last two are filed
/// unaccepted.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayerDeclarationAnalyzer : DiagnosticAnalyzer
{
    internal const string MustDeclare = "declares no layer";

    internal const string MustDeclareDecide =
        "set ArchLayer in its project file to the layer ADR 0060's table gives it, an integer from 0 to 8 | if it is "
        + "a test assembly, name it *.Tests";

    internal const string PackableMustDeclare = "is packable and declares no layer";

    internal const string PackableMustDeclareDecide =
        "set ArchLayer to the layer ADR 0060's table gives it, since a packed assembly is in the package graph "
        + "whatever it is called | set IsPackable to false";

    internal const string UnlayeredDeclaresFormat =
        "is a test assembly or the analyzer, and declares layer '{0}'";

    internal const string UnlayeredDeclaresDecide =
        "remove ArchLayer: a test assembly composes across layers and is not published, and the analyzer runs "
        + "inside the compiler | if it is a package, name it as one";

    internal const string MalformedFormat = "declares ArchLayer '{0}', which is not a layer";

    internal const string MalformedDecide =
        "declare an integer from 0 to 8 inclusive | if it has no layer, it is a test assembly: name it *.Tests";

    internal const string ExecutableBelowHostFormat = "is an executable and declares layer {0}";

    internal const string ExecutableBelowHostDecide =
        "declare ArchLayer 6, or above it when the host stacks private assemblies below itself, since an executable "
        + "is a composition root and ADR 0060 reserves those to layer 6 and up | "
        + "build it as a library";

    internal const string HostNotExecutableFormat = "declares layer {0} and is neither an executable nor a host's private assembly";

    internal const string HostNotExecutableDecide =
        "build it as an executable, since only a host's own assemblies sit at layer 6 and up | give it the layer of "
        + "what it is: an integration is layer 5 | if it is a host's private assembly, name its host in "
        + "VarveHostPartOf, below the host's layer (ADR 0252)";

    internal const string RootWithoutHostFormat = "sets ArchCompositionRoot but declares layer {0}";

    internal const string RootWithoutHostDecide =
        "remove ArchCompositionRoot, which is true only at layer 6 and up | if it is the composition root, it is an "
        + "executable at layer 6 or above: declare that";

    internal const string HostWithoutRootFormat = "declares layer {0} and does not set ArchCompositionRoot";

    internal const string HostWithoutRootDecide =
        "set ArchCompositionRoot to true, since layers 6 and up are the composition root (ADR 0060, ADR 0252) | if it is not a host, "
        + "give it the layer of what it is";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(VarveDiagnostics.LayerDeclaration);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(Analyze);
    }

    private static void Analyze(CompilationAnalysisContext context)
    {
        string? name = context.Compilation.Assembly.Name;

        if (name is null || !LayerDeclaration.IsVarveAssembly(name))
        {
            return;
        }

        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;

        string? declared = LayerDeclaration.Read(options, LayerDeclaration.LayerKey);
        bool packable = LayerDeclaration.ReadFlag(options, LayerDeclaration.PackableKey);
        bool root = LayerDeclaration.ReadFlag(options, LayerDeclaration.CompositionRootKey);
        bool executable = LayerDeclaration.IsExecutable(
            LayerDeclaration.Read(options, LayerDeclaration.OutputTypeKey));
        bool unlayered = LayerDeclaration.IsUnlayeredByName(name);

        if (declared is null)
        {
            if (packable)
            {
                Report(context, name, PackableMustDeclare, PackableMustDeclareDecide);
            }
            else if (!unlayered)
            {
                Report(context, name, MustDeclare, MustDeclareDecide);
            }

            // A test assembly with no layer is not a composition root either.
            if (root)
            {
                Report(context, name, Format(RootWithoutHostFormat, "none"), RootWithoutHostDecide);
            }

            return;
        }

        if (unlayered && !packable)
        {
            Report(context, name, Format(UnlayeredDeclaresFormat, declared), UnlayeredDeclaresDecide);
            return;
        }

        if (!LayerDeclaration.TryParseLayer(declared, out int layer))
        {
            Report(context, name, Format(MalformedFormat, declared), MalformedDecide);
            return;
        }

        bool host = layer >= LayerDeclaration.HostLayer;

        // ADR 0060: an executable is at a host layer, and a library is there
        // only as a host's private assembly, naming its host (ADR 0252).
        if (executable && !host)
        {
            Report(context, name, Format(ExecutableBelowHostFormat, layer), ExecutableBelowHostDecide);
        }
        else if (!executable && host
            && !LayerDeclaration.IsVarveAssembly(LayerDeclaration.Read(options, LayerDeclaration.HostPartOfKey)))
        {
            Report(context, name, Format(HostNotExecutableFormat, layer), HostNotExecutableDecide);
        }

        // And the composition root is the host layers, both ways.
        if (root && !host)
        {
            Report(context, name, Format(RootWithoutHostFormat, layer), RootWithoutHostDecide);
        }
        else if (!root && host)
        {
            Report(context, name, Format(HostWithoutRootFormat, layer), HostWithoutRootDecide);
        }
    }

    private static string Format(string format, object argument) =>
        string.Format(CultureInfo.InvariantCulture, format, argument);

    private static void Report(CompilationAnalysisContext context, string name, string finding, string decide) =>
        context.ReportDiagnostic(Diagnostic.Create(
            VarveDiagnostics.LayerDeclaration, Location.None, name, finding, decide));
}
