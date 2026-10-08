// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Protocol.Client.Model;

/// <summary>An address the <see cref="EndpointPolicy"/> refused (ADR 0102), with the address and the reason.</summary>
public sealed class EndpointRefusedException : Exception
{
    /// <summary>Creates one for <paramref name="iri"/>, refused for <paramref name="reason"/>.</summary>
    public EndpointRefusedException(RdfTerm iri, string reason)
        : base("<" + (iri is null ? string.Empty : Encoding.UTF8.GetString(iri.Lexical)) + "> is refused by the endpoint policy: " + reason + ".")
    {
        Iri = iri;
        Reason = reason;
    }

    /// <summary>Creates one with a message.</summary>
    public EndpointRefusedException(string message)
        : base(message)
    {
        Reason = message;
    }

    /// <summary>Creates one with a message and the error underneath.</summary>
    public EndpointRefusedException(string message, Exception inner)
        : base(message, inner)
    {
        Reason = message;
    }

    /// <summary>Creates one with no message.</summary>
    public EndpointRefusedException()
    {
        Reason = string.Empty;
    }

    /// <summary>The address, as written, when one was given.</summary>
    public RdfTerm? Iri { get; }

    /// <summary>Why the policy refused it.</summary>
    [DesignDecision(typeof(ModelNamespacesForLayers3To5.FailureTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string Reason { get; }
}
