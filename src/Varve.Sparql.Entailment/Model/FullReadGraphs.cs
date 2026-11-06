// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Sparql.Entailment.Model;

/// <summary>
/// The graphs whose content makes a schema read its scoping graph whole
/// (ADR 0193 point 2): each holds a triple that says something of the RDF or
/// RDFS vocabulary itself.
/// </summary>
/// <param name="Graphs">The graphs that hold one: null, first, for the default graph, then the named graphs in IRI order.</param>
public sealed record FullReadGraphs(IReadOnlyList<RdfTerm?> Graphs);
