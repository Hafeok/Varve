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
/// Canonical form is RDF 1.2 N-Triples §3's (ADR 0061) — one space after the
/// subject, the predicate and the object, no comments, a lowercase language
/// tag, <c>ECHAR</c> for BS, HT, LF, FF, CR, <c>"</c> and <c>\</c>, a
/// <c>UCHAR</c> for the other controls, DEL, U+FFFE and U+FFFF, and every
/// other character written directly. N-Quads 1.2 adds the graph label after
/// the object; <c>docs/spec/n-triples.md</c> §5 has it all.
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
    /// consumer that needs ASCII, and writes a language tag as the term holds
    /// it; the result is still well-formed, and is no longer byte-comparable
    /// with anyone else's output.
    /// </summary>
    public bool Canonical
    {
        get => !_notCanonical;
        init => _notCanonical = !value;
    }
}
