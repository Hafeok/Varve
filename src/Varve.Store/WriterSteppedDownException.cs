// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// The writer released the dataset's writer epoch, abdicating for
/// <see cref="Reason"/> (ADR 0179 point 6), and commits nothing more: a
/// commit that was not yet appended when it stepped down is refused with it,
/// and nothing in it was written. A standby, or the host's own restart, takes
/// the dataset over; the request is sent again.
/// </summary>
/// <remarks>
/// <see cref="Dataset.StepDown"/> raises it. A commit already in the batch
/// being appended finishes; the store refuses the rest.
/// </remarks>
[DesignDecision(typeof(WriterHandoverWithinAStore.SteppedDownAnswers), Scope = ExceptionScope.Boundary)]
public sealed class WriterSteppedDownException : Exception
{
    /// <summary>The writer stepped down.</summary>
    public WriterSteppedDownException()
    {
    }

    /// <summary>The writer stepped down, and how.</summary>
    public WriterSteppedDownException(string message)
        : base(message)
    {
    }

    /// <summary>The writer stepped down, how, and the cause.</summary>
    public WriterSteppedDownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The writer stepped down for <paramref name="reason"/>, and how.</summary>
    public WriterSteppedDownException(string reason, string message)
        : base(message) => Reason = reason;

    /// <summary>Why it released the epoch, as the writer record names it: <c>shutdown</c>, <c>placement</c>, <c>storage-error</c> or <c>local-disk-error</c>.</summary>
    public string? Reason { get; }
}
