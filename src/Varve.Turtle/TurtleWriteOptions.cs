// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Turtle;

/// <summary>How to write Turtle or TriG.</summary>
/// <remarks>
/// <c>default(TurtleWriteOptions)</c> is canonical Turtle, which is why
/// <see cref="Canonical"/> is stored inverted, as in <see cref="WriteOptions"/>.
/// </remarks>
public readonly struct TurtleWriteOptions
{
    // IDE0032 wants an auto property here and cannot have one: the field is
    // stored inverted precisely so that default(TurtleWriteOptions) is canonical.
#pragma warning disable IDE0032
    private readonly bool _notCanonical;
#pragma warning restore IDE0032

    /// <summary>Turtle or TriG. Turtle by default.</summary>
    /// <remarks>
    /// Turtle has no graphs, so a quad with one is written without it. That is
    /// the writer dropping information the syntax cannot carry, and a caller
    /// with a dataset wants TriG.
    /// </remarks>
    public RdfSyntax Syntax { get; init; }

    /// <summary>
    /// Whether to write canonical form: a character directly rather than as a
    /// <c>UCHAR</c>, and <c>ECHAR</c> only where one is required
    /// (<c>turtle.md</c> §7). True by default.
    /// </summary>
    public bool Canonical
    {
        get => !_notCanonical;
        init => _notCanonical = !value;
    }
}
