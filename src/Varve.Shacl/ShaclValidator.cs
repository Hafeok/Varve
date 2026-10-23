// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Shapes;
using Varve.Shacl.Validation;

namespace Varve.Shacl;

/// <summary>
/// Validates a data graph against a shapes graph (SHACL 1.2 Core §6.6), over
/// any quad source: the data is read through the quad source contract and
/// never changed, and no term is added to it (ADR 0151).
/// </summary>
public static class ShaclValidator
{
    /// <summary>
    /// Validates the data graph <paramref name="dataGraph"/> of
    /// <paramref name="data"/> against <paramref name="shapes"/>: a report, or
    /// the problems of a failure. Cancellation throws.
    /// </summary>
    public static ValidationOutcome Validate(
        ShapesGraph shapes,
        IQuadSource data,
        GraphPattern dataGraph,
        ShaclOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(data);
        options ??= ShaclOptions.Default;

        DataGraph graph = new(data, dataGraph, ReferenceEquals(shapes.Source.Source, data) ? shapes.Source : null);
        ValidationRun run = new(shapes, graph, options, cancellationToken);
        List<ResultRecord> results = [];
        try
        {
            run.ValidateAll(results);
        }
        catch (ShaclFailureException failure)
        {
            return new ValidationOutcome(null, [failure.Problem]);
        }

        return new ValidationOutcome(ValidationReport.Create(run, results), []);
    }

    /// <summary>
    /// <see cref="Validate"/>, with the results also given per (shape, focus
    /// node) pair (ADR 0174): every targeted pair that has a result, in the
    /// order the validation reached them. The whole report is the
    /// <see cref="ValidationReport.Compose"/> of the pairs' reports.
    /// </summary>
    public static PairedOutcome ValidateByPair(
        ShapesGraph shapes,
        IQuadSource data,
        GraphPattern dataGraph,
        ShaclOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(data);
        options ??= ShaclOptions.Default;

        DataGraph graph = new(data, dataGraph, ReferenceEquals(shapes.Source.Source, data) ? shapes.Source : null);
        ValidationRun run = new(shapes, graph, options, cancellationToken);
        List<ResultRecord> results = [], pair = [];
        ResultSink sink = ResultSink.Collecting(pair);
        List<FocusPair> pairs = [];
        try
        {
            foreach (Shape shape in shapes.Shapes)
            {
                if (shape.Deactivated)
                {
                    continue;
                }

                foreach (Node focus in run.Targets(shape))
                {
                    pair.Clear();
                    run.ValidateFocus(shape, focus, sink);
                    if (pair.Count > 0)
                    {
                        results.AddRange(pair);
                        pairs.Add(new FocusPair(shape.Node, graph.Term(focus), ValidationReport.Create(run, pair)));
                    }
                }
            }
        }
        catch (ShaclFailureException failure)
        {
            return new PairedOutcome(null, [], [failure.Problem]);
        }

        return new PairedOutcome(ValidationReport.Create(run, results), pairs, []);
    }
}

/// <summary>A validation by pair's outcome: the whole report and the pairs with results, or the problems of a failure.</summary>
public sealed class PairedOutcome
{
    internal PairedOutcome(ValidationReport? report, IReadOnlyList<FocusPair> pairs, IReadOnlyList<ShaclProblem> problems)
    {
        Report = report;
        Pairs = pairs;
        Problems = problems;
    }

    /// <summary>The whole report, or null when the validation failed.</summary>
    public ValidationReport? Report { get; }

    /// <summary>Every targeted pair with at least one result; empty when the validation failed.</summary>
    public IReadOnlyList<FocusPair> Pairs { get; }

    /// <summary>Why it failed; empty when there is a report.</summary>
    public IReadOnlyList<ShaclProblem> Problems { get; }
}

/// <summary>A validation's outcome: a report, or the problems of a refusal or failure (ADR 0151).</summary>
public sealed class ValidationOutcome
{
    internal ValidationOutcome(ValidationReport? report, IReadOnlyList<ShaclProblem> problems)
    {
        Report = report;
        Problems = problems;
    }

    /// <summary>The report, or null when the validation failed.</summary>
    public ValidationReport? Report { get; }

    /// <summary>Why it failed; empty when there is a report.</summary>
    public IReadOnlyList<ShaclProblem> Problems { get; }

    /// <summary>Whether there is a report.</summary>
    public bool Succeeded => Report is not null;

    /// <summary>The outcome of a validation that produced <paramref name="report"/>: what a store serves from a projection's rows (ADR 0174).</summary>
    public static ValidationOutcome Reported(ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ValidationOutcome(report, []);
    }

    /// <summary>The outcome of a validation refused or failed with <paramref name="problems"/>, at least one.</summary>
    /// <exception cref="ArgumentException">No problem is given.</exception>
    public static ValidationOutcome Failed(IReadOnlyList<ShaclProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        return problems.Count > 0 ? new ValidationOutcome(null, problems) : throw new ArgumentException("A failed validation has a problem.", nameof(problems));
    }
}
