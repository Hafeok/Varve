// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol;

/// <summary>
/// The classifications a host keeps (ADR 0233): for a dataset with classifier
/// bindings, its classification at a view's position, its classifier for the
/// write rule, and the nodes reclassified at a position. The protocol applies
/// them to row-restricted callers, and knows no binding, projection or
/// shape of its own: <c>Varve.Classification</c> is at its layer.
/// </summary>
[Contract(typeof(TheClassificationProjection.OneClassificationProjectionPerBinding), Role = "the host's classification projections, as the protocol reads them")]
public interface IRowClassifications
{
    /// <summary>
    /// The classification of <paramref name="dataset"/> at <paramref name="view"/>'s
    /// position, in its handles, after one bounded wait; null when the dataset
    /// has no classifier binding.
    /// </summary>
    /// <exception cref="ClassificationBehindException">A projection had not reached the position when the wait ended.</exception>
    IRowClassification? At(DatasetName dataset, DatasetView view);

    /// <summary>The classifier of <paramref name="dataset"/>'s bindings, for the write rule; null when it has none.</summary>
    IClassifier? Classifier(DatasetName dataset);

    /// <summary>The nodes whose subject set changed at <paramref name="position"/>, in store handles (ADR 0237 point 4).</summary>
    IReadOnlyList<TermHandle> ChangedAt(DatasetName dataset, Position position);
}

/// <summary>
/// A row-restricted read at a position a classification projection has not
/// reached, after one bounded wait: answered <c>503 classification-behind</c>
/// (ADR 0233 point 7).
/// </summary>
[Contract(typeof(TheClassificationProjection.ClassificationLagNeverUnready), Role = "a row-restricted read above its classification")]
public sealed class ClassificationBehindException : Exception
{
    /// <summary>The projection of <paramref name="binding"/> is at <paramref name="projected"/>, below <paramref name="position"/>.</summary>
    public ClassificationBehindException(RdfTerm binding, Position position, Position projected)
        : base("The classification of this binding is at position " + projected.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", below the position read, " + position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". Retry, or read a position it has reached.")
    {
        ArgumentNullException.ThrowIfNull(binding);
        Binding = binding;
        Position = position;
        Projected = projected;
    }

    /// <summary>The read was refused; nothing more is known.</summary>
    public ClassificationBehindException()
    {
        Binding = RdfTerm.Iri("urn:x-varve:binding"u8);
    }

    /// <summary>The read was refused, and why.</summary>
    public ClassificationBehindException(string message)
        : base(message)
    {
        Binding = RdfTerm.Iri("urn:x-varve:binding"u8);
    }

    /// <summary>The read was refused, and why.</summary>
    public ClassificationBehindException(string message, Exception innerException)
        : base(message, innerException)
    {
        Binding = RdfTerm.Iri("urn:x-varve:binding"u8);
    }

    /// <summary>The binding whose projection is behind.</summary>
    public RdfTerm Binding { get; }

    /// <summary>The position read.</summary>
    public Position Position { get; }

    /// <summary>The position the projection reached.</summary>
    public Position Projected { get; }
}
