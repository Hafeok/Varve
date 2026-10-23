// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Graphs;
using Varve.Shacl.Model;
using Varve.Shacl.Shapes;

namespace Varve.Shacl;

/// <summary>
/// A shapes graph, loaded once: its shapes found, checked and stratified
/// (ADR 0150, 0151). Immutable and safe to share across threads and
/// validations. It exposes no shape: the shapes model is internal until the
/// SHACL shapes model milestone (ADR 0155).
/// </summary>
public sealed class ShapesGraph
{
    private ShapesGraph(ShapesSource source, IReadOnlyList<Shape> shapes, IReadOnlyList<RdfTerm> imports, IReadOnlyList<IShaclExtension> extensions)
    {
        Source = source;
        Shapes = [.. shapes];
        Imports = imports;
        Extensions = extensions;
    }

    internal ShapesSource Source { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ImmutableArray<Shape> Shapes { get; }

    /// <summary>The IRIs the shapes graph imports with <c>owl:imports</c>, none of which was followed (ADR 0151).</summary>
    internal IReadOnlyList<RdfTerm> Imports { get; }

    internal IReadOnlyList<IShaclExtension> Extensions { get; }

    /// <summary>What the shapes read of the data, worked out once (ADR 0171). Two threads may build it at once; both build the same.</summary>
    internal Incremental.ShapesFootprint Footprint => field ??= Incremental.ShapesFootprint.Of(this);

    /// <summary>
    /// The class of every shape — incremental, scoped or full — and the
    /// constructs that decide it (ADR 0171), from the shapes graph alone.
    /// </summary>
    [DesignDecision(typeof(WhichShapesAreIncrementallyMaintainable.ClassificationIsStatic), Scope = ExceptionScope.Boundary)]
    public ShapesClassification Classify() => Footprint.Classification;

    /// <summary>
    /// Loads the shapes graph <paramref name="graph"/> of
    /// <paramref name="source"/>: the shapes, or the problems that refuse them.
    /// The source must not change while the shapes graph is in use.
    /// </summary>
    public static ShapesLoad Load(IQuadSource source, GraphPattern graph, ShaclOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= ShaclOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();

        ShapesSource shapes = new(source, graph);
        ShapesReader reader = new(shapes, options.Extensions, options.RefuseNonDeterministicFunctions);
        List<Shape> read = reader.Read();
        List<ShaclProblem> problems = [.. reader.Problems];
        if (problems.Count == 0)
        {
            problems.AddRange(Stratification.Apply(read));
        }

        if (problems.Count > 0)
        {
            return new ShapesLoad(null, problems);
        }

        List<RdfTerm> imports = [];
        foreach ((RdfTerm _, RdfTerm imported) in shapes.Pairs(Vocabulary.OwlImports))
        {
            if (!imports.Contains(imported))
            {
                imports.Add(imported);
            }
        }

        return new ShapesLoad(new ShapesGraph(shapes, read, imports, options.Extensions), []);
    }
}

/// <summary>A loaded shapes graph, or the problems that refused it.</summary>
public sealed class ShapesLoad
{
    internal ShapesLoad(ShapesGraph? shapes, IReadOnlyList<ShaclProblem> problems)
    {
        Shapes = shapes;
        Problems = problems;
    }

    /// <summary>The shapes graph, or null when it was refused.</summary>
    public ShapesGraph? Shapes { get; }

    /// <summary>Why it was refused; empty when it was loaded.</summary>
    public IReadOnlyList<ShaclProblem> Problems { get; }

    /// <summary>Whether it was loaded.</summary>
    public bool Succeeded => Shapes is not null;
}
