// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The object store does not enforce conditional writes: the probe an open
/// runs under the reserved key <c>probe/</c> saw a second create-if-absent of
/// one name, or a replace against a stale version, succeed (ADR 0134). The
/// backend refuses to open on such a store, because the epoch fence and the
/// writer record rest on exactly those refusals (ADR 0136). Garage is one: it
/// accepts <c>If-None-Match: *</c> and ignores it.
/// </summary>
[DesignDecision(typeof(TheObjectStoreContract.ConditionalWritesProbedAtOpen), Scope = ExceptionScope.Boundary)]
public sealed class ConditionalWritesNotEnforcedException : Exception
{
    /// <summary>The store does not enforce conditional writes.</summary>
    public ConditionalWritesNotEnforcedException()
    {
    }

    /// <summary>The store does not enforce conditional writes, and how that was seen.</summary>
    public ConditionalWritesNotEnforcedException(string message)
        : base(message)
    {
    }

    /// <summary>The store does not enforce conditional writes, how, and the cause.</summary>
    public ConditionalWritesNotEnforcedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
