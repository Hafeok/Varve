// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Turtle;

/// <summary>Where in the input something happened.</summary>
/// <remarks>
/// <strong>Column is counted in bytes</strong>, not in characters or grapheme
/// clusters. The parser works in UTF-8, and counting characters would mean
/// decoding every line twice — once to parse it and once to count. A
/// byte-oriented tool wants the byte anyway. See <c>docs/spec/n-triples.md</c>
/// §4.
/// </remarks>
public readonly struct ParsePosition : IEquatable<ParsePosition>
{
    /// <summary>Builds a position.</summary>
    public ParsePosition(long byteOffset, int line, int column)
    {
        ByteOffset = byteOffset;
        Line = line;
        Column = column;
    }

    /// <summary>Bytes from the start of the input.</summary>
    public long ByteOffset { get; }

    /// <summary>The line, counting from one.</summary>
    public int Line { get; }

    /// <summary>The byte within the line, counting from one.</summary>
    public int Column { get; }

    /// <inheritdoc />
    public bool Equals(ParsePosition other) =>
        ByteOffset == other.ByteOffset && Line == other.Line && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ParsePosition other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ByteOffset, Line, Column);

    /// <summary>Renders as <c>line:column</c> followed by the byte offset.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column} (byte {ByteOffset})");

    /// <summary>Compares every component.</summary>
    public static bool operator ==(ParsePosition left, ParsePosition right) => left.Equals(right);

    /// <summary>Compares every component.</summary>
    public static bool operator !=(ParsePosition left, ParsePosition right) => !left.Equals(right);
}
