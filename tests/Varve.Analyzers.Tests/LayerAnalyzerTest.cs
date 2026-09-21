using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Varve.Analyzers.Tests;

/// <summary>
/// A compilation set up the way the layering rules expect to find one: an
/// assembly with a name and a declared layer, referencing other Varve
/// assemblies that carry layer metadata of their own.
/// </summary>
/// <remarks>
/// Two mechanisms the rules depend on are reproduced here rather than
/// simulated. The declaring project's layer arrives through a global analyzer
/// config carrying <c>build_property.VarveLayer</c>, which is how
/// <c>CompilerVisibleProperty</c> surfaces an MSBuild property to an analyzer.
/// A referenced assembly's layer arrives as a real
/// <c>[assembly: AssemblyMetadata]</c> attribute on a real second compilation,
/// built through the test state's <c>AdditionalProjects</c>, because
/// reading that attribute off a referenced assembly symbol is most of what the
/// rules do.
/// </remarks>
internal sealed class LayerAnalyzerTest<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    private readonly Dictionary<string, string> _assemblyNames = new(StringComparer.Ordinal);

    internal LayerAnalyzerTest(string declaringAssemblyName, string? declaredLayer, bool isPackable = false)
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80;

        _assemblyNames[DefaultTestProjectName] = declaringAssemblyName;

        TestCode = "namespace Declaring { internal sealed class Marker { } }";

        // IsPackable is always supplied, because the repository always has a
        // value for it: Directory.Build.props defaults it to false and a
        // package project opts in. VarveLayer is supplied only when declared,
        // so that "undeclared" is reproduced as an absent property rather than
        // an empty one.
        TestState.AnalyzerConfigFiles.Add(("/.globalconfig", GlobalConfig(declaredLayer, isPackable)));

        SolutionTransforms.Add((solution, _) =>
        {
            foreach (ProjectId projectId in solution.ProjectIds)
            {
                Project? project = solution.GetProject(projectId);
                if (project is null)
                {
                    continue;
                }

                if (_assemblyNames.TryGetValue(project.Name, out string? assemblyName))
                {
                    solution = solution.WithProjectAssemblyName(projectId, assemblyName);
                }
            }

            return solution;
        });
    }

    /// <summary>
    /// Adds a referenced assembly that declares <paramref name="layer"/> and
    /// carries the matching <c>Varve.Layer</c> metadata — an ordinary,
    /// correctly built package.
    /// </summary>
    internal LayerAnalyzerTest<TAnalyzer> ReferencingLayer(string assemblyName, int layer) =>
        Referencing(assemblyName, layer.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Adds a referenced assembly that declares <paramref name="rawLayer"/> and
    /// carries it as metadata verbatim, malformed values included. Declaration
    /// and metadata agree, as they would in a real build, so a malformed value
    /// is reported twice: once by the referenced project about itself, and once
    /// by the referencing project about the reference.
    /// </summary>
    internal LayerAnalyzerTest<TAnalyzer> Referencing(string assemblyName, string rawLayer) =>
        AddProject(
            assemblyName,
            rawLayer,
            "[assembly: System.Reflection.AssemblyMetadata(\"Varve.Layer\", \"" + rawLayer + "\")]"
            + Environment.NewLine
            + "namespace Referenced { public sealed class Marker { } }");

    /// <summary>
    /// Adds a referenced assembly that declares <c>none</c> and therefore emits
    /// no layer metadata — a test, benchmark or analyzer assembly.
    /// </summary>
    internal LayerAnalyzerTest<TAnalyzer> ReferencingNoLayer(string assemblyName) =>
        AddProject(assemblyName, "none", "namespace Referenced { public sealed class Marker { } }");

    /// <summary>
    /// Adds a referenced assembly that declares nothing at all and carries no
    /// metadata. This is the omission VARVE0002 exists to catch, so a
    /// <c>Varve.*</c> assembly added this way also reports against itself.
    /// </summary>
    internal LayerAnalyzerTest<TAnalyzer> ReferencingUndeclared(string assemblyName) =>
        AddProject(assemblyName, declaredLayer: null, "namespace Referenced { public sealed class Marker { } }");

    private LayerAnalyzerTest<TAnalyzer> AddProject(string assemblyName, string? declaredLayer, string source)
    {
        // The project name doubles as the key the solution transform uses to
        // set the assembly name, which is what the rules actually read.
        _assemblyNames[assemblyName] = assemblyName;

        ProjectState project = new(assemblyName, LanguageNames.CSharp, "/" + assemblyName + "/", "cs");
        project.Sources.Add(("Marker.cs", source));

        // The analyzer runs over every project in the test solution, not only
        // the primary one, so a referenced project needs its own declaration
        // for the same reason a real one does.
        if (declaredLayer is not null)
        {
            project.AnalyzerConfigFiles.Add((
                "/" + assemblyName + "/.globalconfig",
                GlobalConfig(declaredLayer, isPackable: false)));
        }

        TestState.AdditionalProjects.Add(assemblyName, project);
        TestState.AdditionalProjectReferences.Add(assemblyName);

        return this;
    }

    private static string GlobalConfig(string? layer, bool isPackable)
    {
        string config = "is_global = true" + Environment.NewLine
            + "build_property.IsPackable = " + (isPackable ? "true" : "false") + Environment.NewLine;

        return layer is null
            ? config
            : config + "build_property.VarveLayer = " + layer + Environment.NewLine;
    }
}
