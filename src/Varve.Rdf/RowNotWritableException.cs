// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Rdf;

/// <summary>
/// A row-restricted write that <see cref="RowWriteRule"/> refuses (ADR 0236).
/// It names no node, subject or binding: every reason is the same refusal, so
/// that no write's outcome says more than what its writer reads. At layer 1
/// beside the rule, for the reason <see cref="GraphNotWritableException"/> is.
/// </summary>
public sealed class RowNotWritableException : Exception
{
    /// <summary>The refusal.</summary>
    public RowNotWritableException()
        : base("The request writes a row outside the writable subject set.")
    {
    }

    /// <summary>A refusal with a message.</summary>
    public RowNotWritableException(string message)
        : base(message)
    {
    }

    /// <summary>A refusal with a message and its cause.</summary>
    public RowNotWritableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
