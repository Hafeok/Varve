// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.JsonLd;

/// <summary>Receives one quad; the view is valid for the duration of the call (ADR 0112, <c>TheQuadHandlerIsTheCallers</c>).</summary>
[Contract(typeof(RdfTermRepresentation.TermViewIsARefStruct), Role = "receives each quad as a view valid for the call")]
public delegate void JsonLdQuadHandler(in QuadView quad);

/// <summary>
/// Fetches a remote document — a context named by IRI in <c>@context</c> or
/// <c>@import</c> — for the processor. Returns false when the document is not
/// available, which is the error <c>loading remote context failed</c>.
/// </summary>
/// <remarks>
/// The package never opens a connection of its own (ADR 0112,
/// <c>RemoteContextsNeedALoader</c>): with no loader, every remote context
/// fails. A loader returns the document's bytes; relative IRIs in the context
/// resolve against <paramref name="iri"/>.
/// </remarks>
[Contract(typeof(JsonLdOverUtf8Json.RemoteContextsNeedALoader), Role = "fetches a remote context or an @import for the processor")]
public delegate bool JsonLdDocumentLoader(ReadOnlySpan<byte> iri, out ReadOnlyMemory<byte> document);

/// <summary>How to expand a JSON-LD document and turn it into RDF (JSON-LD 1.1 API §6.1 <c>JsonLdOptions</c>, the part this package takes).</summary>
/// <remarks>
/// <c>default(JsonLdOptions)</c> has no base, no document loader, no
/// expand context, and <see cref="RdfDirection.Native"/>.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly struct JsonLdOptions
{
    /// <summary>The document's IRI, against which relative IRIs resolve until a context's <c>@base</c> says otherwise. Empty means there is none.</summary>
    public ReadOnlyMemory<byte> BaseIri { get; init; }

    /// <summary>
    /// A context to apply before the document's own (the specification's
    /// <c>expandContext</c>): a JSON document holding a <c>@context</c>, or
    /// the context value itself. Empty means none.
    /// </summary>
    public ReadOnlyMemory<byte> ExpandContext { get; init; }

    /// <summary>The IRI <see cref="ExpandContext"/> was loaded from, for its relative IRIs. Empty means <see cref="BaseIri"/>.</summary>
    public ReadOnlyMemory<byte> ExpandContextIri { get; init; }

    /// <summary>Fetches remote contexts. Null, the default, refuses every one.</summary>
    public JsonLdDocumentLoader? DocumentLoader { get; init; }

    /// <summary>How <c>@direction</c> becomes RDF; <see cref="RdfDirection.Native"/> by default.</summary>
    public RdfDirection RdfDirection { get; init; }

    /// <summary>Whether the expanded document, when written, is indented. The toRdf path ignores it.</summary>
    public bool Indent { get; init; }
}

/// <summary>How to write RDF as JSON-LD (JSON-LD 1.1 API §6.1, the <c>fromRdf</c> part).</summary>
/// <remarks><c>default(JsonLdWriteOptions)</c> writes indented, keeps types as strings, keeps <c>rdf:type</c> as <c>@type</c>, and reads directions natively.</remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly struct JsonLdWriteOptions
{
#pragma warning disable IDE0032 // stored inverted so that default(JsonLdWriteOptions) indents
    private readonly bool _compact;
#pragma warning restore IDE0032

    /// <summary>Whether the document is indented. True by default.</summary>
    public bool Indent
    {
        get => !_compact;
        init => _compact = !value;
    }

    /// <summary>The specification's <c>useNativeTypes</c>: <c>xsd:integer</c>, <c>xsd:double</c> and <c>xsd:boolean</c> literals become JSON numbers and booleans. False by default.</summary>
    public bool UseNativeTypes { get; init; }

    /// <summary>The specification's <c>useRdfType</c>: <c>rdf:type</c> is written as a property rather than <c>@type</c>. False by default.</summary>
    public bool UseRdfType { get; init; }

    /// <summary>How a direction is read out of RDF; <see cref="RdfDirection.Native"/> by default.</summary>
    public RdfDirection RdfDirection { get; init; }
}
