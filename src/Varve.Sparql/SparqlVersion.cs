// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql;

/// <summary>
/// A SPARQL version label (SPARQL 1.2 Query §4.4.1), as a caller's option and
/// as a <c>VERSION</c> declaration's value.
/// </summary>
/// <remarks>
/// Ordered by inclusion: everything that conforms to <see cref="Sparql11"/>
/// conforms to <see cref="Sparql12Basic"/>, and everything that conforms to
/// that conforms to <see cref="Sparql12"/>. A declaration in the text may
/// narrow the caller's option and never widen it (<c>docs/spec/sparql-grammar.md</c>
/// §5).
/// </remarks>
public enum SparqlVersion : byte
{
    /// <summary><c>"1.1"</c>: SPARQL 1.1 Query and Update syntax.</summary>
    Sparql11 = 1,

    /// <summary>
    /// <c>"1.2-basic"</c>: SPARQL 1.2 without triple terms and without nested
    /// triple patterns. The <c>VERSION</c> declaration, base direction on
    /// language-tagged strings, and the language and direction functions.
    /// </summary>
    Sparql12Basic = 2,

    /// <summary><c>"1.2"</c>: all of SPARQL 1.2. The default for an API caller.</summary>
    Sparql12 = 3,
}
