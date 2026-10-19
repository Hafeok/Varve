// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Sparql.Evaluation;

namespace Varve.Shacl.Sparql;

/// <summary>
/// SHACL-SPARQL (SHACL 1.2 SPARQL-Related Features §3, §4) for
/// <c>Varve.Shacl</c>: pass <see cref="Extension"/> in
/// <c>ShaclOptions.Extensions</c> (ADR 0148).
/// </summary>
[Contract(typeof(VarveShaclAndVarveShaclSparql.ShaclSparqlAtLayerFour), Role = "the SHACL-SPARQL extension")]
public static class SparqlConstraints
{
    /// <summary>
    /// The extension, evaluating queries with <paramref name="options"/> —
    /// its <c>RegexTimeout</c>, functions and service handler, which refuses
    /// <c>SERVICE</c> by default as Appendix A requires.
    /// </summary>
    public static IShaclExtension Extension(EvaluationOptions? options = null) => new SparqlExtension(options ?? EvaluationOptions.Default);
}
