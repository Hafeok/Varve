// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.JsonLd.Model;

/// <summary>Why a JSON-LD document was rejected: the specification's code, and a message naming what was wrong.</summary>
/// <remarks>
/// JSON-LD is processed as a whole document, so there is no position to
/// report: a context error belongs to a term, not to a byte, and the
/// expansion that found it may be far from where the term was written. The
/// message names the term, the IRI or the value.
/// </remarks>
public sealed class JsonLdError
{
    internal JsonLdError(JsonLdErrorCode code, string message)
    {
        Code = code;
        Message = message;
    }

    /// <summary>The specification's error code.</summary>
    public JsonLdErrorCode Code { get; }

    /// <summary>What was wrong, naming the term, IRI or value. Text for a person; <see cref="Code"/> is the half a program reads.</summary>
    [DesignDecision(typeof(SyntaxModelSurfaces.ErrorMessagesAreDisplayText), Scope = ExceptionScope.Boundary)]
    public string Message { get; }

    /// <summary>The code's specification text, a colon, and the message.</summary>
    public override string ToString() => JsonLdErrorCodes.Text(Code) + ": " + Message;
}
