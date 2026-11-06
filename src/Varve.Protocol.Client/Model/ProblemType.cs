// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A problem's type, the IRI an RFC 9457 body names (ADRs 0092, 0119): under
/// <see cref="Namespace"/> for every problem Varve answers, <c>about:blank</c>
/// when a body names none.
/// </summary>
public readonly record struct ProblemType
{
    /// <summary>The namespace every Varve problem type is in.</summary>
    public const string Namespace = "https://w3id.org/varve/problems/";

    /// <summary>The type <paramref name="value"/>, an IRI.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public ProblemType(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The type of a body that names none (RFC 9457 §4.2.1).</summary>
    public static ProblemType AboutBlank { get; } = new("about:blank");

    /// <summary><c>not-ready</c>: readiness's answer when the instance is not ready, whose body is the readiness.</summary>
    public static ProblemType NotReady { get; } = new(Namespace + "not-ready");

    /// <summary>The type's IRI.</summary>
    public string Value { get; }

    /// <summary>
    /// The name after <see cref="Namespace"/>, which is also the name of the
    /// type's page in <c>docs/problems/</c>; null for a type outside it.
    /// </summary>
    public string? Name =>
        Value is not null && Value.StartsWith(Namespace, StringComparison.Ordinal) && Value.Length > Namespace.Length
            ? Value[Namespace.Length..]
            : null;

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
