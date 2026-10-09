// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.JsonLd.Model;

namespace Varve.JsonLd.Processing;

/// <summary>
/// The first error ends the processing (ADR 0112,
/// <c>TheFirstErrorEndsTheParse</c>): the algorithms throw this and the entry
/// point turns it into a result. Only the error path allocates.
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.TheFirstErrorEndsTheParse), Scope = ExceptionScope.Boundary)]
internal sealed class JsonLdException : Exception
{
    internal JsonLdException(JsonLdErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    internal JsonLdErrorCode Code { get; }

    internal JsonLdError ToError() => new(Code, Message);
}
