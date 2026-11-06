// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Sparql.Entailment.Closure;

/// <summary>
/// A generalised triple (RDF 1.1 Semantics, Appendix A): any term in any
/// position, as the closure derives it. Only well-formed ones reach an answer.
/// </summary>
internal readonly record struct Triple(RdfTerm S, RdfTerm P, RdfTerm O);
