// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Sparql.Algebra;

/// <summary>
/// Where in the input a node came from: byte offsets, and the line and byte
/// column of the start.
/// </summary>
/// <remarks>
/// <para>
/// The units of <c>docs/spec/sparql-grammar.md</c> §6: offsets from the start
/// of the UTF-8 input (the transcoded form, for UTF-16 input), a 1-based line
/// and a 1-based column <strong>counted in bytes</strong>, as every parser in
/// this repository counts them.
/// </para>
/// <para>
/// A span takes no part in algebra equality (<c>docs/spec/sparql-algebra.md</c>
/// §2.2): the node types exclude it, so two trees that mean the same thing are
/// equal wherever their text came from. This type's own equality is the
/// ordinary one.
/// </para>
/// </remarks>
[DesignDecision(typeof(SyntaxModelSurfaces.SourceCoordinatesAreOffsetsLinesAndColumns), Scope = ExceptionScope.Boundary)]
public readonly struct SourceSpan : IEquatable<SourceSpan>
{
    /// <summary>Builds a span.</summary>
    public SourceSpan(long start, long end, int line, int column)
    {
        Start = start;
        End = end;
        Line = line;
        Column = column;
    }

    /// <summary>Byte offset of the first byte, from the start of the input.</summary>
    public long Start { get; }

    /// <summary>Byte offset one past the last byte.</summary>
    public long End { get; }

    /// <summary>The line of the first byte, counting from one.</summary>
    public int Line { get; }

    /// <summary>The byte within that line, counting from one.</summary>
    public int Column { get; }

    /// <summary>The span's length in bytes.</summary>
    public long Length => End - Start;

    /// <summary>The span of a node the translation invented, with no text of its own.</summary>
    public static SourceSpan None => default;

    /// <inheritdoc />
    public bool Equals(SourceSpan other) =>
        Start == other.Start && End == other.End && Line == other.Line && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SourceSpan other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, End, Line, Column);

    /// <summary>Renders as <c>line:column</c> followed by the byte range.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column} (bytes {Start}-{End})");

    /// <summary>Same positions.</summary>
    public static bool operator ==(SourceSpan left, SourceSpan right) => left.Equals(right);

    /// <summary>Different positions.</summary>
    public static bool operator !=(SourceSpan left, SourceSpan right) => !left.Equals(right);
}
