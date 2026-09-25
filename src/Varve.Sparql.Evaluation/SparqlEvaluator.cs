// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Compile;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Optimisation;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// Evaluates SPARQL queries over any <see cref="IQuadSource"/>
/// (<c>docs/spec/sparql-evaluation.md</c>). Immutable and reusable; each
/// <see cref="Evaluate"/> is one execution with its own state.
/// </summary>
public sealed class SparqlEvaluator
{
    private readonly EvaluationOptions _options;

    /// <summary>An evaluator with the given options, or <see cref="EvaluationOptions.Default"/>.</summary>
    public SparqlEvaluator(EvaluationOptions? options = null) => _options = options ?? EvaluationOptions.Default;

    /// <summary>
    /// Evaluates a query over a source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The source is not the evaluator's</strong> (ADR 0052). The
    /// evaluator never pins, never disposes it, and never learns whether it is
    /// a pinned view of a store. The caller keeps it alive until the results
    /// are disposed, and releases it afterwards — for a store,
    /// <c>Dataset.Pin()</c> before and the view's <c>Dispose()</c> after the
    /// results'. Releasing it while results are still being read is a caller
    /// error, reported by the source itself.
    /// </para>
    /// <para>
    /// <strong>Cancellation</strong> is checked at every operator boundary and
    /// inside scans, path searches, sorts and groupings, so a runaway query
    /// stops with an <see cref="OperationCanceledException"/> from whichever
    /// call noticed.
    /// </para>
    /// </remarks>
    /// <exception cref="QueryEvaluationException">The query fails as a whole: a <c>SERVICE</c> that failed without <c>SILENT</c>, <c>NOW()</c> without a clock, and the like.</exception>
    public QueryResults Evaluate(Query query, IQuadSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        Query prepared = new PathNormaliser().Rewrite(query);
        if (_options.Optimise)
        {
            prepared = Optimiser.Optimise(prepared, source, _options);
        }

        Compiler compiler = new(source, _options);
        Template? template = prepared is ConstructQuery construct ? Template.Compile(construct.Template, compiler) : null;
        int[] described = prepared is DescribeQuery describe ? DescribedSlots(describe, compiler) : [];
        CompiledQuery compiled = compiler.Compile(prepared);

        Exec exec = new(source, _options, compiled.Width, cancellationToken) { BaseIri = prepared.Prologue.Base };
        SetDataset(exec, prepared.Dataset, source);

        IEnumerator<ulong[]> solutions = compiled.Root.Open(exec, exec.NewRow(), ActiveGraph.Default);
        switch (prepared)
        {
            case SelectQuery:
                return new SolutionResults(exec, solutions, compiled.Columns, compiled.ColumnSlots);
            case AskQuery:
                using (solutions)
                {
                    return new BooleanResult(solutions.MoveNext());
                }

            case ConstructQuery:
                return new TripleResults(template!.Instantiate(exec, solutions));
            default:
                return new TripleResults(Description.Describe(exec, solutions, ((DescribeQuery)prepared).Resources, described));
        }
    }

    private static int[] DescribedSlots(DescribeQuery query, Compiler compiler)
    {
        List<int> slots = [];
        foreach (PatternTerm resource in query.Resources)
        {
            slots.Add(resource is VariablePattern variable ? compiler.Slot(variable.Variable.Name) : -1);
        }

        return [.. slots];
    }

    /// <summary>§13.2: <c>FROM</c> and <c>FROM NAMED</c> replace the source's dataset.</summary>
    private static void SetDataset(Exec exec, DatasetSpec? dataset, IQuadSource source)
    {
        if (dataset is null)
        {
            return;
        }

        List<TermHandle> defaults = [];
        foreach (RdfTerm graph in dataset.DefaultGraphs)
        {
            if (source.TryInternalise(graph, out TermHandle handle) && !defaults.Contains(handle))
            {
                defaults.Add(handle);
            }
        }

        List<TermHandle> named = [];
        foreach (RdfTerm graph in dataset.NamedGraphs)
        {
            if (source.TryInternalise(graph, out TermHandle handle) && !named.Contains(handle))
            {
                named.Add(handle);
            }
        }

        exec.DefaultGraphs = [.. defaults];
        exec.NamedGraphList = named;
        exec.NamedGraphSet = new HashSet<TermHandle>(named, source.TermComparer);
    }
}
