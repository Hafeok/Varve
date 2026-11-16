// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Sparql.Entailment.Closure;

/// <summary>
/// Which entailment rules a closure applies, and which datatypes it
/// recognises (RDF 1.1 Semantics §7–9): the RDF rules alone, or the RDFS
/// rules with them.
/// </summary>
internal sealed class RuleSet
{
    internal RuleSet(bool rdfs, IReadOnlySet<RdfTerm> recognised)
    {
        Rdfs = rdfs;
        Recognised = recognised;
    }

    /// <summary>Whether rdfs1–rdfs13 apply, as well as rdfD2 and GrdfD1.</summary>
    internal bool Rdfs { get; }

    /// <summary>The datatypes D the closure recognises: GrdfD1 types their literals, rdfs1 makes them datatypes.</summary>
    internal IReadOnlySet<RdfTerm> Recognised { get; }

    /// <summary>Whether a literal is its value: D recognises a datatype beside <c>xsd:string</c> and <c>rdf:langString</c>, whose values have one spelling.</summary>
    internal bool ByValue => Recognised.Count > 2;
}
