// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.Sparql.Entailment;

/// <summary>
/// The datatypes each regime recognises (RDF 1.1 Semantics §7, Entailment
/// Regimes §5.1): <c>xsd:string</c> and <c>rdf:langString</c> under RDF and
/// RDFS, and under D Varve.Xsd's whole map with <c>rdf:langString</c>
/// (ADR 0193 point 7).
/// </summary>
internal static class Datatypes
{
    internal static FrozenSet<RdfTerm> RdfRecognised { get; } =
        new[] { Vocabulary.XsdString, Vocabulary.LangString }.ToFrozenSet(RdfTerm.Comparer);

    internal static FrozenSet<RdfTerm> DatatypeMap { get; } = Map().ToFrozenSet(RdfTerm.Comparer);

    private static IEnumerable<RdfTerm> Map()
    {
        yield return Vocabulary.LangString;
        foreach (XsdDatatype datatype in Enum.GetValues<XsdDatatype>())
        {
            if (datatype != XsdDatatype.None)
            {
                yield return RdfTerm.Iri(XsdDatatypes.Iri(datatype));
            }
        }
    }
}
