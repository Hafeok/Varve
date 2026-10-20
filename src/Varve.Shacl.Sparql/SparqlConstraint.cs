// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;

namespace Varve.Shacl.Sparql;

/// <summary>
/// A SPARQL-based constraint (§3.3) or a constraint of a SPARQL-based
/// constraint component (§4.3): the query, pre-bound per focus node by
/// Values Insertion, its solutions mapped to results by §3.3.2.
/// </summary>
internal sealed class SparqlConstraint(
    RdfTerm component,
    RdfTerm? sourceConstraint,
    RdfTerm? severity,
    List<RdfTerm> messages,
    Query query,
    List<(string Name, RdfTerm Value)> parameters,
    bool ask,
    bool nodeShape,
    EvaluationOptions options) : IShaclConstraint, IShaclFootprint
{
    private readonly QueryFootprint _footprint = QueryFootprint.Of(query, Prebound(parameters, ask));
    private const string FailureTrue = "true";

    public RdfTerm Component => component;

    public RdfTerm? SourceConstraint => sourceConstraint;

    public RdfTerm? Severity => severity;

    /// <summary>Scoped when every pattern is joined to <c>$this</c> or <c>$value</c>, full otherwise (ADR 0171 §4).</summary>
    public Maintainability Maintainability => _footprint.Maintainability;

    /// <summary>What decided the class.</summary>
    public string Construct => _footprint.Unanalysable is { } why
        ? "a SPARQL-based constraint that cannot be analysed: " + why
        : "a SPARQL-based constraint, by its join paths to $this";

    /// <summary>A function whose value depends on the clock or on chance, or null (ADR 0169).</summary>
    internal string? NonDeterministic => _footprint.NonDeterministic;

    /// <inheritdoc />
    public void Anchors(FootprintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _footprint.Anchors(context);
    }

    private static List<string> Prebound(List<(string Name, RdfTerm Value)> parameters, bool ask)
    {
        List<string> names = ["this"];
        foreach ((string name, RdfTerm _) in parameters)
        {
            names.Add(name);
        }

        if (ask)
        {
            names.Add("value");
        }

        return names;
    }

    public void Validate(ConstraintContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            if (ask)
            {
                // §4.3: one solution ($this, $value) for each value node the ASK query rejects.
                for (int i = 0; i < context.ValueNodes.Count; i++)
                {
                    List<(string, RdfTerm, TermHandle)> bindings = Bindings(context);
                    bindings.Add(("value", context.ValueNodes[i], context.ValueHandles[i]));
                    using QueryResults results = Evaluate(context, bindings);
                    if (results is BooleanResult { Value: false })
                    {
                        context.Report(context.ValueNodes[i], context.ValueHandles[i], null, Messages(bindings, null));
                    }
                }

                return;
            }

            List<(string, RdfTerm, TermHandle)> bound = Bindings(context);
            using SolutionResults solutions = (SolutionResults)Evaluate(context, bound);
            int value = Column(solutions, "value"), path = Column(solutions, "path"), message = Column(solutions, "message"), failure = Column(solutions, "failure");
            while (solutions.MoveNext())
            {
                if (Bound(solutions, failure, out RdfTerm? failed, out _) && failed!.Kind == RdfTermKind.Literal && Encoding.UTF8.GetString(failed.Lexical) == FailureTrue)
                {
                    context.Fail(new ShaclProblem(ShaclProblemKind.ExtensionFailure, null, context.FocusNode, "A SPARQL query bound ?failure to true (SHACL 1.2 SPARQL-Related Features §3.3)."));
                    return;
                }

                RdfTerm? valueTerm = null;
                TermHandle valueHandle = TermHandle.None;
                if (Bound(solutions, value, out RdfTerm? v, out TermHandle vh))
                {
                    valueTerm = v;
                    valueHandle = vh;
                }
                else if (nodeShape)
                {
                    // §3.3.2: sh:value is ?value, else the value node, which for a node shape is the focus node.
                    valueTerm = context.FocusNode;
                    valueHandle = context.FocusHandle;
                }

                RdfTerm? pathTerm = Bound(solutions, path, out RdfTerm? p, out _) && p!.Kind == RdfTermKind.Iri ? p : null;
                RdfTerm? messageTerm = Bound(solutions, message, out RdfTerm? m, out _) ? m : null;
                List<(string, RdfTerm, TermHandle)> row = [.. bound];
                foreach (Variable variable in solutions.Variables)
                {
                    int column = Column(solutions, variable.Name);
                    if (Bound(solutions, column, out RdfTerm? t, out TermHandle h))
                    {
                        row.Add((variable.Name, t!, h));
                    }
                }

                context.Report(valueTerm, valueHandle, pathTerm, Messages(row, messageTerm));
            }
        }
        catch (QueryEvaluationException e)
        {
            context.Fail(new ShaclProblem(ShaclProblemKind.ExtensionFailure, null, context.FocusNode, "A SHACL-SPARQL query failed: " + e.Message));
        }
    }

    private List<(string, RdfTerm, TermHandle)> Bindings(ConstraintContext context)
    {
        List<(string, RdfTerm, TermHandle)> bindings = [("this", context.FocusNode, context.FocusHandle)];
        foreach ((string name, RdfTerm value) in parameters)
        {
            bindings.Add((name, value, TermHandle.None));
        }

        return bindings;
    }

    private QueryResults Evaluate(ConstraintContext context, List<(string Name, RdfTerm Value, TermHandle Handle)> bindings)
    {
        Dictionary<RdfTerm, TermHandle> blanks = new(RdfTerm.Comparer);
        List<(string, RdfTerm)> values = [];
        foreach ((string name, RdfTerm value, TermHandle handle) in bindings)
        {
            values.Add((name, value));
            if (!handle.IsNone && value.Kind is RdfTermKind.BlankNode or RdfTermKind.TripleTerm)
            {
                blanks[value] = handle;
            }
        }

        Query prebound = PreBinding.Insert(query, values);
        PreboundSource source = new(context.Data, context.DataGraph, blanks);
        return new SparqlEvaluator(options).Evaluate(prebound, source, context.CancellationToken);
    }

    /// <summary>
    /// §3.3.2: <c>?message</c>, else the constraint's or validator's messages,
    /// with <c>{?name}</c> and <c>{$name}</c> replaced by the bound values; an
    /// empty list leaves the shape's messages.
    /// </summary>
    private List<RdfTerm>? Messages(List<(string Name, RdfTerm Value, TermHandle Handle)> bindings, RdfTerm? bound)
    {
        List<RdfTerm> chosen = bound is null ? messages : [bound];
        if (chosen.Count == 0)
        {
            return null;
        }

        List<RdfTerm> substituted = new(chosen.Count);
        foreach (RdfTerm message in chosen)
        {
            if (message.Kind != RdfTermKind.Literal)
            {
                substituted.Add(message);
                continue;
            }

            string text = Encoding.UTF8.GetString(message.Lexical);
            foreach ((string name, RdfTerm value, TermHandle _) in bindings)
            {
                string shown = Encoding.UTF8.GetString(value.Lexical);
                text = text.Replace("{?" + name + "}", shown, StringComparison.Ordinal).Replace("{$" + name + "}", shown, StringComparison.Ordinal);
            }

            byte[] lexical = Encoding.UTF8.GetBytes(text);
            substituted.Add(!message.Language.IsEmpty
                ? RdfTerm.Literal(lexical, message.Language, message.Direction)
                : message.Datatype is { } datatype ? RdfTerm.Literal(lexical, datatype) : RdfTerm.Literal(lexical));
        }

        return substituted;
    }

    private static int Column(SolutionResults solutions, string name)
    {
        IReadOnlyList<Variable> variables = solutions.Variables;
        for (int i = 0; i < variables.Count; i++)
        {
            if (variables[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Bound(SolutionResults solutions, int column, out RdfTerm? term, out TermHandle handle)
    {
        term = null;
        handle = TermHandle.None;
        if (column < 0 || !solutions.IsBound(new ColumnIndex(column)))
        {
            return false;
        }

        solutions.TryGetHandle(new ColumnIndex(column), out handle);
        return solutions.TryGetTerm(new ColumnIndex(column), out term) && term is not null;
    }
}
