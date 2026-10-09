// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// A number of bytes an evaluation may hold in its materialising operators
/// (ADR 0114): the join table, the sort table, the group table and the
/// distinct set charge it as they grow, and a charge over it fails the
/// evaluation with <see cref="MemoryBudgetExceededException"/>. Counted,
/// not collected: the bound is on what the operators keep, as they keep it.
/// </summary>
public readonly record struct MemoryBytes
{
    /// <summary>A budget of <paramref name="bytes"/>, one or more.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is not positive.</exception>
    public MemoryBytes(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bytes, 1L);
        Bytes = bytes;
    }

    /// <summary>The count, in bytes.</summary>
    public long Bytes { get; }

    /// <summary>Renders as the count.</summary>
    public override string ToString() => Bytes.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// An evaluation charged more than its <see cref="EvaluationOptions.MemoryBudget"/>
/// to the operators that hold rows (ADR 0114). The host answers it as a
/// refusal by policy, never as the collector's failure.
/// </summary>
public sealed class MemoryBudgetExceededException : Exception
{
    /// <summary>An exception with no detail.</summary>
    public MemoryBudgetExceededException()
        : base("The evaluation needs more memory than its budget allows.")
    {
    }

    /// <summary>An exception naming its cause.</summary>
    public MemoryBudgetExceededException(string message)
        : base(message)
    {
    }

    /// <summary>An exception naming its cause, wrapping another.</summary>
    public MemoryBudgetExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The budget that was exceeded, and what the operators had charged when it was.</summary>
    public MemoryBudgetExceededException(MemoryBytes limit, MemoryBytes actual)
        : base("The evaluation's materialising operators hold " + actual.ToString() + " bytes, over the budget of " + limit.ToString() + " (ADR 0114).")
    {
        Limit = limit;
        Actual = actual;
    }

    /// <summary>The budget.</summary>
    public MemoryBytes Limit { get; }

    /// <summary>What was charged when the budget was exceeded.</summary>
    public MemoryBytes Actual { get; }
}
