// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.RdfXml;

/// <summary>Receives each triple an RDF/XML document states, as a view valid for the call.</summary>
[Contract(typeof(RdfTermRepresentation.TermViewIsARefStruct), Role = "receives each parsed triple as a view valid for the call")]
public delegate void RdfXmlQuadHandler(in QuadView quad);

/// <summary>Receives a namespace declaration as the document makes it: the prefix, without its colon, and the namespace IRI.</summary>
/// <remarks>
/// Reported for the same reason Turtle reports its prefixes (ADR 0030): a
/// writer needs them to reproduce a document, and recovering them from the
/// IRIs afterwards is guesswork. Every declaration is reported, in document
/// order, including one that rebinds a prefix inside an element.
/// </remarks>
[Contract(typeof(TurtleRecoveryAndPrefixes.PrefixesReportedAsDeclared), Role = "receives each namespace declaration as the document makes it")]
public delegate void NamespaceHandler(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> iri);

/// <summary>How to parse RDF/XML.</summary>
/// <remarks>
/// <c>default(RdfXmlOptions)</c> validates IRIs and has no base, so a relative
/// reference with no <c>xml:base</c> is an error rather than something
/// silently accepted — which is why <see cref="ValidateIris"/> is stored
/// inverted, as in the other syntax packages.
/// </remarks>
public readonly struct RdfXmlOptions
{
#pragma warning disable IDE0032 // stored inverted so that default(RdfXmlOptions) validates
    private readonly bool _skipIriValidation;
#pragma warning restore IDE0032

    /// <summary>
    /// The document's retrieval IRI, against which a relative reference and
    /// every <c>rdf:ID</c> is resolved until an <c>xml:base</c> says
    /// otherwise (RDF 1.1 XML Syntax §5.3). Empty means there is none.
    /// </summary>
    public ReadOnlyMemory<byte> BaseIri { get; init; }

    /// <summary>Called for each namespace declaration, in document order.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public NamespaceHandler? OnNamespace { get; init; }

    /// <summary>
    /// Whether each IRI is checked against RFC 3987 and required to have a
    /// scheme once resolved. True by default.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool ValidateIris
    {
        get => !_skipIriValidation;
        init => _skipIriValidation = !value;
    }
}
