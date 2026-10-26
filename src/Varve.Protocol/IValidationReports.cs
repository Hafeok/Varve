// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The validation reports a host serves as reserved graphs (ADR 0174): for
/// a dataset, one per shapes binding whose projection the host keeps. The
/// protocol composes each into a read the caller may make of it, and knows
/// no binding, projection or store of its own.
/// </summary>
[Contract(typeof(TheValidationProjection.ReservedReportGraph), Role = "the host's validation projections, as report graphs")]
public interface IValidationReports
{
    /// <summary>The report graphs of <paramref name="dataset"/>; empty when it has none.</summary>
    IReadOnlyList<IValidationReportGraph> Of(DatasetName dataset);
}

/// <summary>One binding's report, readable as the graph <see cref="Graph"/>.</summary>
[Contract(typeof(TheValidationProjection.ReservedReportGraph), Role = "one binding's report graph")]
public interface IValidationReportGraph
{
    /// <summary>The binding's IRI.</summary>
    RdfTerm Binding { get; }

    /// <summary>The reserved graph: <c>https://w3id.org/varve/ns#validation/{enc(binding IRI)}</c>.</summary>
    RdfTerm Graph { get; }

    /// <summary>The graphs the binding validates: a caller reads the report only when it reads every one (ADR 0107, amended).</summary>
    GraphScope Validates { get; }

    /// <summary>The report at <paramref name="position"/> if the projection has reached it, without waiting.</summary>
    bool TryReportAt(Position position, out ValidationOutcome outcome);

    /// <summary>
    /// The report at <paramref name="position"/>, after one bounded wait for
    /// the projection to reach it.
    /// </summary>
    /// <exception cref="ValidationBehindException">The projection had not reached the position when the wait ended.</exception>
    ValidationOutcome ReportAt(Position position);
}

/// <summary>
/// A read of a report graph at a position its projection has not reached,
/// after one bounded wait: answered <c>503 validation-behind</c> (ADR 0174).
/// </summary>
[Contract(typeof(TheValidationProjection.ValidationBehind), Role = "a report graph read above its projection")]
public sealed class ValidationBehindException : Exception
{
    /// <summary>The projection of <paramref name="binding"/> is at <paramref name="projected"/>, below <paramref name="position"/>.</summary>
    public ValidationBehindException(RdfTerm binding, Position position, Position projected)
        : base("The validation report of this binding is at position " + projected.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", below the position read, " + position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". Retry, or read a position it has reached.")
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        Position = position;
        Projected = projected;
    }

    /// <summary>The read was refused; nothing more is known.</summary>
    public ValidationBehindException()
    {
        Binding = RdfTerm.Iri("urn:x-varve:binding"u8);
    }

    /// <summary>The read was refused, and why.</summary>
    public ValidationBehindException(string message)
        : base(message)
    {
        Binding = RdfTerm.Iri("urn:x-varve:binding"u8);
    }

    /// <summary>The read was refused, why, and the cause.</summary>
    public ValidationBehindException(string message, Exception innerException)
        : base(message, innerException)
    {
        Binding = RdfTerm.Iri("urn:x-varve:binding"u8);
    }

    /// <summary>The binding.</summary>
    public RdfTerm Binding { get; }

    /// <summary>The position read.</summary>
    public Position Position { get; }

    /// <summary>The position the projection had reached.</summary>
    public Position Projected { get; }
}
