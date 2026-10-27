// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Sparql.Entailment;

/// <summary>
/// Asserted triples the schema's saturation reads (ADR 0193 point 2): those
/// with a bound subject, predicate or object, or two of them.
/// </summary>
internal readonly record struct Scan(RdfTerm? Subject, RdfTerm? Predicate, RdfTerm? Object);
