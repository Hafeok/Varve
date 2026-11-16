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
