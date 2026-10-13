// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// An OAuth 2.0 access token and the instant it expires (ADR 0127): what a
/// <see cref="Varve.Protocol.Client.BearerTokenProvider"/> answers for one request.
/// </summary>
/// <remarks>
/// The token is opaque here. The client sends it as <c>Authorization:
/// Bearer</c> and reads nothing in it; the expiry is the one the issuer
/// stated (<c>expires_in</c>, or MSAL's <c>ExpiresOn</c>), so the client can
/// refuse to send a token it knows has expired.
/// </remarks>
public readonly record struct BearerToken
{
    /// <summary>A token that expires at <paramref name="expiresOn"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public BearerToken(string value, DateTimeOffset expiresOn)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
        ExpiresOn = expiresOn;
    }

    /// <summary>The access token, as the issuer gave it.</summary>
    [DesignDecision(typeof(OnBehalfOfAndTheTokenProvider.TokenIsOpaqueAtTheBoundary), Scope = ExceptionScope.Boundary)]
    public string Value { get; }

    /// <summary>When the token expires.</summary>
    [DesignDecision(typeof(OnBehalfOfAndTheTokenProvider.TokenIsOpaqueAtTheBoundary), Scope = ExceptionScope.Boundary)]
    public DateTimeOffset ExpiresOn { get; }

    /// <summary>
    /// A provider that answers <paramref name="value"/> for every request, as
    /// one that never expires: a token given on the command line, or a test's.
    /// </summary>
    [DesignDecision(typeof(OnBehalfOfAndTheTokenProvider.TokenIsOpaqueAtTheBoundary), Scope = ExceptionScope.Boundary)]
    public static BearerTokenProvider Fixed(string value)
    {
        BearerToken token = new(value, DateTimeOffset.MaxValue);
        return _ => ValueTask.FromResult<BearerToken?>(token);
    }

    /// <summary>Renders without the token, which is a credential and never written anywhere.</summary>
    public override string ToString() => "bearer token, expires " + ExpiresOn.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The provider answered a token that expires within the client's skew, and
/// answered it again when asked once more (ADR 0127): the request is not sent.
/// </summary>
public sealed class BearerTokenExpiredException : Exception
{
    /// <summary>Creates one for a token that expired at <paramref name="expiresOn"/>.</summary>
    public BearerTokenExpiredException(DateTimeOffset expiresOn)
        : base("The token provider answered a token that expires at " + expiresOn.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + ", twice; the request is not sent with a token known to have expired.")
    {
    }

    /// <summary>Creates one with a message.</summary>
    public BearerTokenExpiredException(string message)
        : base(message)
    {
    }

    /// <summary>Creates one with a message and the error underneath.</summary>
    public BearerTokenExpiredException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Creates one with no message.</summary>
    public BearerTokenExpiredException()
    {
    }
}
