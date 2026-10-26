// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Shacl.Model;

namespace Varve.Shacl;

/// <summary>
/// Constraints SHACL Core does not define — SHACL-SPARQL's, which
/// <c>Varve.Shacl.Sparql</c> implements over the evaluator (ADR 0148). An
/// extension is passed in <see cref="ShaclOptions.Extensions"/>; nothing
/// registers one anywhere else. Expressed over <c>Varve.Rdf</c> types only.
/// </summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ExtensionContractInShaclCore), Role = "an extension, offered each shapes graph")]
public interface IShaclExtension
{
    /// <summary>
    /// Called once per shapes graph, before its shapes are found: the
    /// extension reads what it needs from the shapes graph, declares the
    /// parameters that make their subject a shape, and returns its scope for
    /// that shapes graph, or refuses it.
    /// </summary>
    IShaclExtensionScope Begin(ShapesGraphContext context);
}

/// <summary>An extension's view of one shapes graph, from <see cref="IShaclExtension.Begin"/>.</summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ExtensionContractInShaclCore), Role = "an extension's view of one shapes graph")]
public interface IShaclExtensionScope
{
    /// <summary>Called once per shape: add the extension's constraints for it, or refuse it.</summary>
    void Load(ShapeContext shape);
}

/// <summary>
/// One constraint an extension adds to a shape. It refers to no other shape
/// (ADR 0150), so it never takes part in recursion.
/// </summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ExtensionContractInShaclCore), Role = "a constraint an extension adds to a shape")]
public interface IShaclConstraint
{
    /// <summary><c>sh:sourceConstraintComponent</c> of its results.</summary>
    RdfTerm Component { get; }

    /// <summary><c>sh:sourceConstraint</c> of its results, when the constraint has a node of its own.</summary>
    RdfTerm? SourceConstraint { get; }

    /// <summary>A severity for its results in place of the shape's, or null.</summary>
    RdfTerm? Severity { get; }

    /// <summary>
    /// Validates one focus node: every result goes to
    /// <see cref="ConstraintContext.Report"/>, a failure to
    /// <see cref="ConstraintContext.Fail"/>.
    /// </summary>
    void Validate(ConstraintContext context);
}

/// <summary>What an extension sees of a shapes graph at load.</summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ExtensionContractInShaclCore), Role = "what an extension sees of a shapes graph")]
public sealed class ShapesGraphContext
{
    private readonly List<RdfTerm> _parameters = [];
    private readonly List<ShaclProblem> _problems;

    internal ShapesGraphContext(IQuadSource source, GraphPattern graph, List<ShaclProblem> problems)
    {
        Source = source;
        Graph = graph;
        _problems = problems;
    }

    /// <summary>The source holding the shapes graph.</summary>
    public IQuadSource Source { get; }

    /// <summary>The shapes graph in it.</summary>
    public GraphPattern Graph { get; }

    /// <summary>
    /// Whether a constraint whose value depends on the clock or on chance must
    /// refuse the shapes graph, naming the shape and the function (ADR 0169):
    /// <see cref="ShaclOptions.RefuseNonDeterministicFunctions"/>.
    /// </summary>
    public bool RefuseNonDeterministicFunctions { get; internal init; }

    internal IReadOnlyList<RdfTerm> Parameters => _parameters;

    /// <summary>Declares a predicate whose subject is a shape (SHACL 1.2 Core §3.1), such as <c>sh:sparql</c>.</summary>
    public void DeclareShapeParameter(RdfTerm predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _parameters.Add(predicate);
    }

    /// <summary>Refuses the shapes graph.</summary>
    public void Refuse(ShaclProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        _problems.Add(problem);
    }
}

/// <summary>What an extension sees of one shape at load.</summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ExtensionContractInShaclCore), Role = "what an extension sees of one shape")]
public sealed class ShapeContext
{
    private readonly Graphs.ShapesSource _source;
    private readonly List<ShaclProblem> _problems;

    internal ShapeContext(Graphs.ShapesSource source, RdfTerm shape, RdfTerm? path, List<ShaclProblem> problems)
    {
        _source = source;
        Shape = shape;
        Path = path;
        _problems = problems;
    }

    /// <summary>The shape's node.</summary>
    public RdfTerm Shape { get; }

    /// <summary>The shape's <c>sh:path</c> node, for a property shape; null for a node shape.</summary>
    public RdfTerm? Path { get; }

    /// <summary>The source holding the shapes graph.</summary>
    public IQuadSource Source => _source.Source;

    /// <summary>The shapes graph in it.</summary>
    public GraphPattern Graph => _source.Graph;

    internal List<IShaclConstraint> Constraints { get; } = [];

    /// <summary>
    /// The shapes source's handle of a term this shapes graph holds — a blank
    /// node included, which only the handle it came with can name (ADR 0044).
    /// </summary>
    public bool TryGetHandle(RdfTerm term, out TermHandle handle) => _source.TryHandle(term, out handle);

    /// <summary>The term of a handle of the shapes source.</summary>
    public RdfTerm Term(TermHandle handle) => _source.Term(handle);

    /// <summary>Adds a constraint to the shape.</summary>
    public void Add(IShaclConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        Constraints.Add(constraint);
    }

    /// <summary>Refuses the shapes graph because of this shape.</summary>
    public void Refuse(ShaclProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        _problems.Add(problem);
    }
}

/// <summary>
/// What a constraint sees when it validates one focus node: the data, the
/// focus node and its value nodes, each with the data source's handle when
/// the source holds it.
/// </summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ExtensionContractInShaclCore), Role = "what a constraint sees of one focus node")]
public sealed class ConstraintContext
{
    private readonly Action<RdfTerm?, TermHandle, RdfTerm?, IReadOnlyList<RdfTerm>?> _report;
    private readonly Action<ShaclProblem> _fail;

    internal ConstraintContext(
        IQuadSource data,
        GraphPattern dataGraph,
        RdfTerm focusNode,
        TermHandle focusHandle,
        IReadOnlyList<RdfTerm> valueNodes,
        IReadOnlyList<TermHandle> valueHandles,
        Action<RdfTerm?, TermHandle, RdfTerm?, IReadOnlyList<RdfTerm>?> report,
        Action<ShaclProblem> fail,
        CancellationToken cancellationToken)
    {
        Data = data;
        DataGraph = dataGraph;
        FocusNode = focusNode;
        FocusHandle = focusHandle;
        ValueNodes = valueNodes;
        ValueHandles = valueHandles;
        CancellationToken = cancellationToken;
        _report = report;
        _fail = fail;
    }

    /// <summary>The data source.</summary>
    public IQuadSource Data { get; }

    /// <summary>The data graph in it.</summary>
    public GraphPattern DataGraph { get; }

    /// <summary>The focus node.</summary>
    public RdfTerm FocusNode { get; }

    /// <summary>The focus node's handle, or <see cref="TermHandle.None"/> when the data does not hold it.</summary>
    public TermHandle FocusHandle { get; }

    /// <summary>The value nodes (SHACL 1.2 Core §6.9).</summary>
    public IReadOnlyList<RdfTerm> ValueNodes { get; }

    /// <summary>The value nodes' handles, <see cref="TermHandle.None"/> for one the data does not hold.</summary>
    public IReadOnlyList<TermHandle> ValueHandles { get; }

    /// <summary>The validation's cancellation.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// A validation result: its value (with its handle, or
    /// <see cref="TermHandle.None"/>), a predicate path in place of the shape's
    /// path, and messages in place of the shape's, each optional.
    /// </summary>
    public void Report(RdfTerm? value, TermHandle valueHandle, RdfTerm? pathPredicate, IReadOnlyList<RdfTerm>? messages) =>
        _report(value, valueHandle, pathPredicate, messages);

    /// <summary>A failure (§6.6.1): the validation ends with this problem and no report.</summary>
    public void Fail(ShaclProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        _fail(problem);
    }
}
