// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;

namespace Varve.Protocol.Http;

/// <summary>
/// <c>Varve-Entailment</c> (ADR 0192 point 1): one token, or one regime IRI in
/// angle brackets, matched case-sensitively. The tokens of the regimes a
/// later part answers are refused, as an unknown value is (decision
/// <c>EntailmentRegimesOverHttp.EntailmentHeader</c>).
/// </summary>
internal static class EntailmentHeader
{
    internal const string Name = "Varve-Entailment";

    /// <summary>The tokens this server serves, in the table's order.</summary>
    internal static readonly ImmutableArray<string> Supported = ["simple", "rdf", "rdfs", "d"];

    /// <summary>
    /// <c>Varve-Equality: not-applied</c>: an answer under <c>owl2-rl</c> by
    /// a binding with <c>varve:sameAs varve:SameAsOff</c>, which applies no
    /// equality rule (ADR 0197, the ruling of 2026-11-19). Every OWL2RL
    /// binding of this part is one.
    /// </summary>
    internal const string EqualityName = "Varve-Equality";

    /// <summary>The value of <see cref="EqualityName"/>: the eq-* rules were not applied.</summary>
    internal const string EqualityNotApplied = "not-applied";

    /// <summary>
    /// <c>Varve-Closure: per-graph</c>: an answer under <c>owl2-rl</c> whose
    /// default graph merges two or more graphs, answered by the union of
    /// each graph's closure, not the closure of their merge (ADR 0197, the
    /// ruling of 2026-11-19).
    /// </summary>
    internal const string ClosureName = "Varve-Closure";

    /// <summary>The value of <see cref="ClosureName"/>: each graph's closure, merged.</summary>
    internal const string ClosurePerGraph = "per-graph";

    /// <summary>
    /// <c>Varve-Unread-Lists</c>: an answer under <c>owl2-rl</c> whose
    /// closure leaves asserted list cells unread in a graph the caller reads
    /// or in the TBox, as of the answer's position; the value is how many
    /// (ADR 0197, amended 2026-11-26). Absent when there are none.
    /// </summary>
    internal const string UnreadListsName = "Varve-Unread-Lists";

    /// <summary>
    /// <c>Varve-Without-Closure</c>: an answer under <c>owl2-rl</c>, or over a
    /// view, whose <c>GRAPH ?g</c> could read named graphs no binding gives a
    /// closure, read with their explicit quads only; the value names them,
    /// twenty at most, then how many more (ADR 0197 point 6).
    /// </summary>
    internal const string WithoutClosureName = "Varve-Without-Closure";

    /// <summary>The token of the OWL 2 RL regime, which a closure answers (ADR 0197 point 6).</summary>
    internal const string Owl2RlToken = "owl2-rl";

    /// <summary>Whether a header names the OWL 2 RL regime: <c>owl2-rl</c>, or <c>ent:OWL-RDF-Based</c> in angle brackets.</summary>
    internal static bool IsOwl2Rl(StringValues header)
    {
        if (header.Count != 1 || header[0] is not { } value)
        {
            return false;
        }

        value = value.Trim();
        return value == Owl2RlToken || value == "<http://www.w3.org/ns/entailment/OWL-RDF-Based>";
    }

    /// <summary>The regime a header names; false for anything else, more than one value among it.</summary>
    internal static bool TryRead(StringValues header, out EntailmentRegime regime)
    {
        regime = EntailmentRegime.Simple;
        if (header.Count != 1 || header[0] is not { } value || value.Contains(',', StringComparison.Ordinal))
        {
            return false;
        }

        value = value.Trim();
        if (value.Length > 2 && value[0] == '<' && value[^1] == '>')
        {
            if (!EntailmentRegimes.TryFromIri(RdfTerm.Iri(Encoding.UTF8.GetBytes(value[1..^1])), out EntailmentRegime? named))
            {
                return false;
            }

            regime = named.Value;
            return true;
        }

        switch (value)
        {
            case "simple":
                regime = EntailmentRegime.Simple;
                return true;
            case "rdf":
                regime = EntailmentRegime.Rdf;
                return true;
            case "rdfs":
                regime = EntailmentRegime.Rdfs;
                return true;
            case "d":
                regime = EntailmentRegime.D;
                return true;
            default:
                return false;
        }
    }

    /// <summary>A regime's token, as the response names it.</summary>
    internal static string Token(EntailmentRegime regime) => regime switch
    {
        EntailmentRegime.Rdf => "rdf",
        EntailmentRegime.Rdfs => "rdfs",
        EntailmentRegime.D => "d",
        _ => "simple",
    };

    /// <summary>The value a problem echoes: the header as sent.</summary>
    internal static string Shown(StringValues header) => string.Join(", ", (IEnumerable<string?>)header.ToArray());
}
