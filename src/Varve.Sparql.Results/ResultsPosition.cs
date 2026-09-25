// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Sparql.Results;

/// <summary>
/// A place in a result document: a byte offset, and the 1-based line and
/// 1-based byte column of that byte, as <c>sparql-grammar.md</c> §6 counts them.
/// </summary>
public readonly struct ResultsPosition : IEquatable<ResultsPosition>
{
    /// <summary>A position.</summary>
    public ResultsPosition(long byteOffset, int line, int column)
    {
        ByteOffset = byteOffset;
        Line = line;
        Column = column;
    }

    /// <summary>Bytes from the start of the document.</summary>
    public long ByteOffset { get; }

    /// <summary>The line, from 1. A line ends at LF.</summary>
    public int Line { get; }

    /// <summary>The byte column within the line, from 1.</summary>
    public int Column { get; }

    /// <inheritdoc />
    public bool Equals(ResultsPosition other) =>
        ByteOffset == other.ByteOffset && Line == other.Line && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ResultsPosition other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ByteOffset, Line, Column);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"line {Line}, column {Column} (byte {ByteOffset})");

    /// <summary>Compares offset, line and column.</summary>
    public static bool operator ==(ResultsPosition left, ResultsPosition right) => left.Equals(right);

    /// <summary>Compares offset, line and column.</summary>
    public static bool operator !=(ResultsPosition left, ResultsPosition right) => !left.Equals(right);
}
