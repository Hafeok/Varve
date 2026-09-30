// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Store.Log;

/// <summary>A validator's decision: accept, optionally with an attachment, or reject with a report.</summary>
public readonly struct ValidationVerdict : IEquatable<ValidationVerdict>
{
    private ValidationVerdict(bool accepted, RdfTerm? attachment, IReadOnlyList<RdfTerm>? report)
    {
        IsAccepted = accepted;
        Attachment = attachment;
        Report = report ?? [];
    }

    /// <summary>Whether the commit may land.</summary>
    public bool IsAccepted { get; }

    /// <summary>A term recorded in the commit's metadata, for example a validation report's IRI.</summary>
    public RdfTerm? Attachment { get; }

    /// <summary>Why the commit was refused. Triple terms can carry a report graph.</summary>
    public IReadOnlyList<RdfTerm> Report { get; }

    /// <summary>Accept.</summary>
    public static ValidationVerdict Accept() => new(true, null, null);

    /// <summary>Accept, and record a term in the commit's metadata.</summary>
    public static ValidationVerdict Accept(RdfTerm attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return new ValidationVerdict(true, attachment, null);
    }

    /// <summary>Refuse. Nothing reaches the log or the dictionary.</summary>
    public static ValidationVerdict Reject(IReadOnlyList<RdfTerm> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ValidationVerdict(false, null, report);
    }

    /// <inheritdoc />
    public bool Equals(ValidationVerdict other) =>
        IsAccepted == other.IsAccepted && Equals(Attachment, other.Attachment) && ReferenceEquals(Report, other.Report);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValidationVerdict other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsAccepted, Attachment);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator ==(ValidationVerdict left, ValidationVerdict right) => left.Equals(right);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator !=(ValidationVerdict left, ValidationVerdict right) => !left.Equals(right);
}
