// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Shacl.Model;
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
}
