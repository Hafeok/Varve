// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Turtle;

/// <summary>What a whole parse came to.</summary>
/// <remarks>
/// <see cref="Succeeded"/> means no errors, not "some quads were produced". A
/// parse of an empty document succeeds and yields nothing; a parse that lost
/// one line out of a million did not succeed.
/// </remarks>
public readonly struct ParseResult : IEquatable<ParseResult>
{
    /// <summary>Builds a result.</summary>
    public ParseResult(long quadCount, long errorCount, ParseError firstError)
    {
        QuadCount = quadCount;
        ErrorCount = errorCount;
        FirstError = firstError;
    }

    /// <summary>How many quads were handed to the handler.</summary>
    public long QuadCount { get; }

    /// <summary>How many lines were rejected.</summary>
    public long ErrorCount { get; }

    /// <summary>The first rejection, which is the one worth reporting.</summary>
    public ParseError FirstError { get; }

    /// <summary>Whether the whole input was well-formed.</summary>
    public bool Succeeded => ErrorCount == 0;

    /// <inheritdoc />
    public bool Equals(ParseResult other) =>
        QuadCount == other.QuadCount
        && ErrorCount == other.ErrorCount
        && FirstError.Equals(other.FirstError);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ParseResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(QuadCount, ErrorCount, FirstError);

    /// <summary>Compares every component.</summary>
    public static bool operator ==(ParseResult left, ParseResult right) => left.Equals(right);

    /// <summary>Compares every component.</summary>
    public static bool operator !=(ParseResult left, ParseResult right) => !left.Equals(right);
}
