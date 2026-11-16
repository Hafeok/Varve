// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Sparql.Entailment.Model;

/// <summary>
/// A SPARQL 1.1 entailment regime this package answers (ADR 0192): simple
/// entailment, and the three ADR 0193 rewrites.
/// </summary>
public enum EntailmentRegime : byte
{
    /// <summary><c>http://www.w3.org/ns/entailment/Simple</c>: the query as written.</summary>
    Simple = 0,

    /// <summary><c>http://www.w3.org/ns/entailment/RDF</c> (Entailment Regimes §2).</summary>
    Rdf = 1,

    /// <summary><c>http://www.w3.org/ns/entailment/RDFS</c> (Entailment Regimes §4).</summary>
    Rdfs = 2,

    /// <summary><c>http://www.w3.org/ns/entailment/D</c> (Entailment Regimes §5.1), with Varve.Xsd's datatype map.</summary>
    D = 3,
}
