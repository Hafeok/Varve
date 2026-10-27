// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Entailment.Model;

namespace Varve.Sparql.Entailment;

/// <summary>
/// The regimes' IRIs, as SPARQL 1.1 Entailment Regimes names them (ADR 0192).
/// The <c>Varve-Entailment</c> header's tokens are the protocol's.
/// </summary>
[Contract(typeof(EntailmentRegimesOverHttp.EntailmentHeader), Role = "the regimes' IRIs")]
public static class EntailmentRegimes
{
    private const string Namespace = "http://www.w3.org/ns/entailment/";

    /// <summary>The regime's IRI.</summary>
    public static RdfTerm Iri(EntailmentRegime regime) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Namespace + Local(regime)));

    /// <summary>Reads a regime IRI; false for any other term, or a regime this package does not rewrite.</summary>
    public static bool TryFromIri(RdfTerm iri, [NotNullWhen(true)] out EntailmentRegime? regime)
    {
        ArgumentNullException.ThrowIfNull(iri);
        regime = null;
        if (iri.Kind != RdfTermKind.Iri)
        {
            return false;
        }

        foreach (EntailmentRegime candidate in Enum.GetValues<EntailmentRegime>())
        {
            if (iri.Equals(Iri(candidate)))
            {
                regime = candidate;
                return true;
            }
        }

        return false;
    }

    private static string Local(EntailmentRegime regime) => regime switch
    {
        EntailmentRegime.Simple => "Simple",
        EntailmentRegime.Rdf => "RDF",
        EntailmentRegime.Rdfs => "RDFS",
        EntailmentRegime.D => "D",
        _ => throw new ArgumentOutOfRangeException(nameof(regime), regime, "No such regime."),
    };
}
