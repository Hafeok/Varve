// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Algebra;

/// <summary>An update request: the prologue and its operations in order (<c>docs/spec/sparql-algebra.md</c> §5).</summary>
public sealed record Update(Prologue Prologue, AlgebraList<UpdateOperation> Operations) : AlgebraNode;

/// <summary>One operation of SPARQL 1.1 Update §3. A record of the request; nothing here executes.</summary>
[Contract(typeof(OptimiserAndEvaluatorOnePackageAlgebraInAlgebraOut.AlgebraNodesAreSealedRecords), Role = "one operation of an update request")]
public abstract record UpdateOperation : AlgebraNode
{
    private protected UpdateOperation()
    {
    }
}

/// <summary><c>INSERT DATA { }</c>: ground quads, blank nodes allowed.</summary>
public sealed record InsertData(AlgebraList<QuadPattern> Quads) : UpdateOperation;

/// <summary><c>DELETE DATA { }</c>: ground quads, no blank nodes.</summary>
public sealed record DeleteData(AlgebraList<QuadPattern> Quads) : UpdateOperation;

/// <summary><c>DELETE WHERE { }</c>: the quad patterns are both the template and the pattern.</summary>
public sealed record DeleteWhere(AlgebraList<QuadPattern> Quads) : UpdateOperation;

/// <summary>
/// <c>WITH … DELETE { } INSERT { } USING … WHERE { }</c>. <c>WITH</c> and
/// <c>USING</c> are kept as written; the executor applies Update §3.1.3.
/// </summary>
public sealed record Modify(
    RdfTerm? With,
    AlgebraList<QuadPattern> Delete,
    AlgebraList<QuadPattern> Insert,
    DatasetSpec? Using,
    QueryPattern Where) : UpdateOperation;

/// <summary><c>LOAD source INTO GRAPH graph</c>; a <see langword="null"/> graph is the default graph.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Load(RdfTerm Source, RdfTerm? Graph, bool Silent) : UpdateOperation;

/// <summary><c>CLEAR</c>.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Clear(GraphTarget Target, bool Silent) : UpdateOperation;

/// <summary><c>DROP</c>.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Drop(GraphTarget Target, bool Silent) : UpdateOperation;

/// <summary><c>CREATE GRAPH</c>.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Create(RdfTerm Graph, bool Silent) : UpdateOperation;

/// <summary><c>ADD from TO to</c>.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Add(GraphOrDefault From, GraphOrDefault To, bool Silent) : UpdateOperation;

/// <summary><c>MOVE from TO to</c>.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Move(GraphOrDefault From, GraphOrDefault To, bool Silent) : UpdateOperation;

/// <summary><c>COPY from TO to</c>.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Copy(GraphOrDefault From, GraphOrDefault To, bool Silent) : UpdateOperation;

/// <summary>Which graphs <c>CLEAR</c> and <c>DROP</c> act on: production <c>[49] GraphRefAll</c>.</summary>
public enum GraphTargetKind : byte
{
    /// <summary><c>DEFAULT</c></summary>
    Default,

    /// <summary><c>NAMED</c>: every named graph.</summary>
    Named,

    /// <summary><c>ALL</c></summary>
    All,

    /// <summary><c>GRAPH iri</c></summary>
    Graph,
}

/// <summary>The target of <c>CLEAR</c> and <c>DROP</c>.</summary>
public readonly record struct GraphTarget
{
    private GraphTarget(GraphTargetKind kind, RdfTerm? graph)
    {
        Kind = kind;
        Graph = graph;
    }

    /// <summary>Which graphs.</summary>
    public GraphTargetKind Kind { get; }

    /// <summary>The graph, when <see cref="Kind"/> is <see cref="GraphTargetKind.Graph"/>.</summary>
    public RdfTerm? Graph { get; }

    /// <summary><c>DEFAULT</c></summary>
    public static GraphTarget Default => new(GraphTargetKind.Default, null);

    /// <summary><c>NAMED</c></summary>
    public static GraphTarget Named => new(GraphTargetKind.Named, null);

    /// <summary><c>ALL</c></summary>
    public static GraphTarget All => new(GraphTargetKind.All, null);

    /// <summary><c>GRAPH iri</c></summary>
    public static GraphTarget Of(RdfTerm graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new GraphTarget(GraphTargetKind.Graph, graph);
    }
}

/// <summary>A graph or the default graph: production <c>[47] GraphOrDefault</c>.</summary>
public readonly record struct GraphOrDefault
{
    private GraphOrDefault(RdfTerm? graph) => Graph = graph;

    /// <summary>The named graph, or <see langword="null"/> for the default graph.</summary>
    public RdfTerm? Graph { get; }

    /// <summary>True for the default graph.</summary>
    public bool IsDefault => Graph is null;

    /// <summary><c>DEFAULT</c></summary>
    public static GraphOrDefault Default => new(null);

    /// <summary>A named graph.</summary>
    public static GraphOrDefault Of(RdfTerm graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new GraphOrDefault(graph);
    }
}
