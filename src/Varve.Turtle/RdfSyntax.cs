// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Turtle;

/// <summary>Which syntax is being read or written.</summary>
/// <remarks>
/// <para>
/// N-Triples and N-Quads differ only in whether a fourth term is permitted
/// before the <c>.</c>, so one parser reads both and finding a graph label in
/// N-Triples is an error rather than an unknown production. Turtle and TriG
/// stand in the same relation to each other, and are read by a second parser
/// because their grammar is not line-based.
/// </para>
/// </remarks>
public enum RdfSyntax : byte
{
    /// <summary>RDF 1.1 N-Triples.</summary>
    NTriples,

    /// <summary>RDF 1.1 N-Quads.</summary>
    NQuads,

    /// <summary>RDF 1.1 Turtle.</summary>
    Turtle,

    /// <summary>RDF 1.1 TriG.</summary>
    TriG,
}
