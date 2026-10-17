// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Shapes;

namespace Varve.Shacl.Validation;

/// <summary>An extension's constraint (ADR 0148) inside the shapes model: its results and failures pass through the contract's context.</summary>
internal sealed class ExtensionConstraint : Constraint
{
    private readonly IShaclConstraint _inner;

    internal ExtensionConstraint(IShaclConstraint inner)
        : base(inner.Component)
    {
        _inner = inner;
        Severity = inner.Severity;
    }

    internal override RdfTerm? SourceConstraint => _inner.SourceConstraint;

    [DesignDecision(typeof(ShaclHotPathScope.ExtensionsAreTheCallersCode), Scope = ExceptionScope.HotPath)]
    internal override void Validate(ValidationRun run, Shape shape, Node focus, List<Node> values, ResultSink sink)
    {
        List<RdfTerm> terms = new(values.Count);
        List<TermHandle> handles = new(values.Count);
        foreach (Node value in values)
        {
            terms.Add(run.Data.Term(value));
            handles.Add(value.Handle);
        }

        RdfTerm focusTerm = run.Data.Term(focus);
        ConstraintContext context = new(
            run.Data.Source,
            run.Data.Graph,
            focusTerm,
            focus.Handle,
            terms,
            handles,
            (value, valueHandle, predicate, messages) =>
            {
                Node? node = value is null ? null : valueHandle.IsNone ? run.Data.Resolve(value) : Node.HeldAs(valueHandle, value);
                sink.Add(
                    shape,
                    this,
                    focus,
                    node,
                    predicate is null ? null : new PredicatePath(predicate),
                    messages is { Count: > 0 } ? [.. messages] : null);
            },
            problem => throw new ShaclFailureException(problem),
            run.CancellationToken);
        _inner.Validate(context);
    }
}
