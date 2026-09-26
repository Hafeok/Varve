// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Rdf;

/// <summary>
/// A quad source's answer to "how many quads match this pattern": exact,
/// estimated, or unknown.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0049. Three promises, and they are the contract. An <see cref="IsExact"/>
/// answer is the number of quads <see cref="IQuadSource.Match"/> would yield
/// for the same pattern at the source's current state, and a test may assert
/// equality. An <see cref="IsUnknown"/> answer is honest: the source cannot
/// say, and <see cref="Count"/> is meaningless. An answer that is neither is a
/// count the source has reason to believe, and the source's documentation says
/// how it was derived. No source in this repository returns one.
/// </para>
/// <para>
/// Three states rather than a sentinel because "exact" is a separate fact from
/// "known", and a <c>-1</c> would be the ambiguity ADR 0022's amendment removed
/// from the graph position.
/// </para>
/// </remarks>
public readonly struct CardinalityEstimate : IEquatable<CardinalityEstimate>
{
    private const byte UnknownState = 0;
    private const byte EstimatedState = 1;
    private const byte ExactState = 2;

    private readonly byte _state;

    private CardinalityEstimate(QuadCount count, byte state)
    {
        Count = count;
        _state = state;
    }

    /// <summary>The count, when there is one. Zero when <see cref="IsUnknown"/>.</summary>
    public QuadCount Count { get; }

    /// <summary><see cref="Count"/> is the number of quads <c>Match</c> would yield.</summary>
    public bool IsExact => _state == ExactState;

    /// <summary>The source cannot say; <see cref="Count"/> is meaningless.</summary>
    public bool IsUnknown => _state == UnknownState;

    /// <summary>A count the source believes but does not guarantee.</summary>
    public bool IsEstimated => _state == EstimatedState;

    /// <summary>The source cannot say.</summary>
    public static CardinalityEstimate Unknown => default;

    /// <summary>An exact count.</summary>
    public static CardinalityEstimate Exact(QuadCount count) => new(count, ExactState);

    /// <summary>A count the source believes but does not guarantee.</summary>
    public static CardinalityEstimate Estimated(QuadCount count) => new(count, EstimatedState);

    /// <inheritdoc />
    public bool Equals(CardinalityEstimate other) => _state == other._state && Count == other.Count;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CardinalityEstimate other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_state, Count);

    /// <inheritdoc />
    public override string ToString() => _state switch
    {
        ExactState => Count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        EstimatedState => "~" + Count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "?",
    };

    /// <summary>Same state and count.</summary>
    public static bool operator ==(CardinalityEstimate left, CardinalityEstimate right) => left.Equals(right);

    /// <summary>Different state or count.</summary>
    public static bool operator !=(CardinalityEstimate left, CardinalityEstimate right) => !left.Equals(right);
}
