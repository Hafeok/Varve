// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The writer record is of a format this version does not read: a later
/// version wrote it (ADR 0217). Nothing is written on it, so this version
/// neither claims the epoch nor takes it over; a read-only open goes on as a
/// follower that knows no writer, while the log reads.
/// </summary>
[DesignDecision(typeof(CoordinationRecordsReadConservatively.AnUnknownWriterRecordFormatIsNeverWritten), Scope = ExceptionScope.Boundary)]
public sealed class WriterRecordFormatUnknownException : Exception
{
    /// <summary>The record's format is not one this version reads.</summary>
    public WriterRecordFormatUnknownException()
    {
    }

    /// <summary>The record's format is not one this version reads, as the message says.</summary>
    public WriterRecordFormatUnknownException(string message)
        : base(message)
    {
    }

    /// <summary>The record's format is not one this version reads, as the message says, and the cause.</summary>
    public WriterRecordFormatUnknownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The record is of <paramref name="format"/>; this version reads <paramref name="reads"/>.</summary>
    public WriterRecordFormatUnknownException(int format, int reads)
        : base("The writer record is of format " + format.ToString(CultureInfo.InvariantCulture) + "; this version of Varve reads format "
            + reads.ToString(CultureInfo.InvariantCulture) + ", so it neither claims the writer epoch nor takes it over. Upgrade Varve to write this dataset.") =>
        Format = format;

    /// <summary>The record's format, or 0 when not given.</summary>
    public int Format { get; }
}
