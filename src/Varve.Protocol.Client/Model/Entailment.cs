// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Varve.Rdf;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A <c>Varve-Entailment</c> value (ADR 0192 point 1): a regime's token,
/// <c>simple</c>, <c>rdf</c>, <c>rdfs</c> or <c>d</c>, or a regime IRI in
/// angle brackets.
/// </summary>
public readonly record struct Entailment
{
    private const string Namespace = "http://www.w3.org/ns/entailment/";

    /// <summary>The value <paramref name="value"/>, as the header carries it.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public Entailment(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Simple entailment: the data as asserted.</summary>
    public static Entailment Simple { get; } = new("simple");

    /// <summary>The value as the header carries it.</summary>
    public string Value { get; }

    /// <summary>The token of a regime IRI the service description names, or null for one this client knows no token of.</summary>
    public static Entailment? OfRegime(RdfTerm regime)
    {
        ArgumentNullException.ThrowIfNull(regime);
        return regime.Kind != RdfTermKind.Iri ? null : Encoding.UTF8.GetString(regime.Lexical) switch
        {
            Namespace + "Simple" => Simple,
            Namespace + "RDF" => new Entailment("rdf"),
            Namespace + "RDFS" => new Entailment("rdfs"),
            Namespace + "D" => new Entailment("d"),
            _ => null,
        };
    }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
