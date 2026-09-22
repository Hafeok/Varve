// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Turtle;

/// <summary>How to write.</summary>
/// <remarks>
/// <para>
/// <c>default(WriteOptions)</c> writes canonical N-Triples, which is why
/// <see cref="Canonical"/> is stored inverted: a bool field defaults to false
/// and the safe default here is true.
/// </para>
/// <para>
/// Canonical form is N-Triples §4 — one space after the subject, the predicate
/// and the object, no comments, and a character written directly wherever it
/// can be rather than as a <c>UCHAR</c>. N-Quads has no canonical form section
/// of its own; we apply the same rules with the graph label after the object,
/// which is an extension of a specification rather than a reading of one, and
/// <c>docs/spec/n-triples.md</c> §5 says so.
/// </para>
/// </remarks>
public readonly struct WriteOptions
{
    // IDE0032 wants an auto property here and cannot have one: the field is
    // stored inverted precisely so that default(WriteOptions) is canonical.
#pragma warning disable IDE0032
    private readonly bool _notCanonical;
#pragma warning restore IDE0032

    /// <summary>Which syntax to write. N-Triples by default.</summary>
    public RdfSyntax Syntax { get; init; }

    /// <summary>
    /// Whether to write canonical form. True by default. Turning it off escapes
    /// every non-ASCII character as a <c>UCHAR</c> with uppercase hex, for a
    /// consumer that needs ASCII; the result is still well-formed, and is no
    /// longer byte-comparable with anyone else's output.
    /// </summary>
    public bool Canonical
    {
        get => !_notCanonical;
        init => _notCanonical = !value;
    }
}
