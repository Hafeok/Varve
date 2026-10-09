// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Turtle;

/// <summary>
/// Which edition of RDF a document is read as, where the two editions'
/// grammars disagree (ADR 0110).
/// </summary>
/// <remarks>
/// <para>
/// RDF 1.2's Turtle, TriG, N-Triples and N-Quads are supersets of RDF 1.1's
/// in every respect but one: RDF 1.1 Turtle accepts a character outside the
/// basic plane written as two <c>\u</c> escapes forming a surrogate pair
/// (its test <c>test-38</c> requires it), and RDF 1.2 rejects every escape
/// that names a surrogate code point, because a surrogate is not a character
/// (its tests <c>surrogate-pair-bad-01</c> and <c>-02</c> require that). A
/// reader cannot do both, so the edition is an option. Every other RDF 1.2
/// construct is read whichever edition is named: the version announcement in
/// a document is a hint (RDF 1.2 Turtle §2.4), and this is the one point on
/// which the editions contradict each other rather than extend.
/// </para>
/// <para>
/// The default is <see cref="Rdf12"/>, the edition this library writes.
/// </para>
/// </remarks>
public enum RdfVersion : byte
{
    /// <summary>RDF 1.2: a <c>\u</c> or <c>\U</c> escape naming a surrogate code point is an error.</summary>
    Rdf12,

    /// <summary>RDF 1.1: two <c>\u</c> escapes forming a surrogate pair denote the character they encode.</summary>
    Rdf11,
}
