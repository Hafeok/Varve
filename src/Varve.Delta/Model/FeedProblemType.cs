// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Delta.Model;

/// <summary>
/// The problem type an error record names (<c>change-feed.md</c> §2.3): an IRI
/// under <c>https://w3id.org/varve/problems/</c> (ADR 0092), the same IRI the
/// server's RFC 9457 problems carry.
/// </summary>
public readonly record struct FeedProblemType
{
    /// <summary>The namespace every Varve problem type is in.</summary>
    public const string Namespace = "https://w3id.org/varve/problems/";

    private FeedProblemType(string iri) => Value = iri;

    /// <summary>The type's IRI.</summary>
    public string Value { get; }

    /// <summary>The type <paramref name="iri"/> names, when it is the namespace and at least one character after it.</summary>
    public static bool TryParse(string? iri, out FeedProblemType type)
    {
        if (iri is not null && iri.Length > Namespace.Length && iri.StartsWith(Namespace, StringComparison.Ordinal))
        {
            type = new FeedProblemType(iri);
            return true;
        }

        type = default;
        return false;
    }

    /// <summary>The type <paramref name="iri"/> names.</summary>
    /// <exception cref="ArgumentException"><paramref name="iri"/> is not under <see cref="Namespace"/>.</exception>
    public static FeedProblemType Parse(string iri) =>
        TryParse(iri, out FeedProblemType type) ? type : throw new ArgumentException("A problem type is an IRI under " + Namespace + ".", nameof(iri));

    /// <summary>Renders as the IRI.</summary>
    public override string ToString() => Value ?? string.Empty;
}
