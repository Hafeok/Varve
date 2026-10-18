// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// The dataset's writer epoch has passed to another writer: a create found its
/// log slot already taken by a newer epoch, so nothing more is appended here
/// (ADR 0136). The dataset stays open for reads.
/// </summary>
/// <remarks>
/// A storage backend throws it from <see cref="ISegmentStore.FlushAsync"/> or
/// <see cref="IBlobWriter.PublishAsync"/>; the store fails the batch with it
/// and refuses every later write the same way. The writer that took over
/// adopted every commit made durable before it did, so none of them is lost;
/// the batch that failed was not written.
/// </remarks>
[DesignDecision(typeof(OneWriterByAnEpochAndALogSlotFence.SupersededWriterStops), Scope = ExceptionScope.Boundary)]
public sealed class WriterSupersededException : Exception
{
    /// <summary>The writer was superseded.</summary>
    public WriterSupersededException()
    {
    }

    /// <summary>The writer was superseded, and how.</summary>
    public WriterSupersededException(string message)
        : base(message)
    {
    }

    /// <summary>The writer was superseded, how, and the cause.</summary>
    public WriterSupersededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
