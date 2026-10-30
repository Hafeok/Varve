// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// A derived blob found damaged while it is read: a block that does not
/// match its check, a page of a term section that does not, a block that
/// does not decode, or a blob shorter than its directory says (ADR 0213).
/// The dataset quarantines the blob and answers from the view below it; it
/// never reaches a caller. An <see cref="IOException"/>, so a path that
/// already takes an unreadable derived blob as a miss keeps doing so.
/// </summary>
[DesignDecision(typeof(VerifiedDerivedData.EveryBlockIsChecked), Scope = ExceptionScope.Boundary)]
internal sealed class DerivedDamageException : IOException
{
    internal DerivedDamageException(string message)
        : base(message)
    {
    }

    internal DerivedDamageException()
    {
    }

    internal DerivedDamageException(string message, System.Exception inner)
        : base(message, inner)
    {
    }
}
