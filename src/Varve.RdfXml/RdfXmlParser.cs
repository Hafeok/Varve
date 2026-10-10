// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using System.Xml;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.RdfXml;

/// <summary>
/// Reads RDF/XML, handing each triple to a callback.
/// </summary>
/// <remarks>
/// <para>
/// Over <see cref="XmlReader"/> (ADR 0122): the BCL's streaming reader does
/// the XML — encodings, entities, namespaces, well-formedness — and this
/// package does the RDF/XML grammar over the events it reports. A DTD is
/// refused, which is also what keeps entity expansion out; nothing is
/// resolved from outside the document.
/// </para>
/// <para>
/// A triple arrives as a <c>QuadView</c> valid only for the duration of the
/// call, in the default graph: RDF/XML has no graphs. There is no recovery
/// unit (<c>rdf-xml.md</c> §5): the first error ends the parse, and the
/// triples handed out before it stand.
/// </para>
/// <para>
/// There is no asynchronous entry point (<c>rdf-xml.md</c> §4): the XML
/// reader's asynchronous surface is a second copy of every call, and a
/// caller with an asynchronous source reads it into a stream and parses that.
/// </para>
/// </remarks>
public static class RdfXmlParser
{
    /// <summary>Parses a whole document held in memory.</summary>
    public static RdfXmlParseResult Parse(ReadOnlyMemory<byte> utf8, RdfXmlQuadHandler handler, in RdfXmlOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        using ReadOnlyMemoryStream stream = new(utf8);
        return Parse(stream, handler, in options);
    }

    /// <summary>Parses a whole document held as a sequence of buffers, read segment by segment.</summary>
    public static RdfXmlParseResult Parse(in ReadOnlySequence<byte> utf8, RdfXmlQuadHandler handler, in RdfXmlOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        using ReadOnlySequenceStream stream = new(utf8);
        return Parse(stream, handler, in options);
    }

    /// <summary>Parses a stream.</summary>
    public static RdfXmlParseResult Parse(Stream stream, RdfXmlQuadHandler handler, in RdfXmlOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(handler);

        using XmlReader reader = XmlReader.Create(stream, Settings());
        RdfXmlReaderCore core = new(reader, handler, in options);
        return core.Run();
    }

    /// <summary>
    /// The reader's settings: no DTD, no resolver, comments and processing
    /// instructions kept because an XML literal carries them (exclusive
    /// canonicalisation with comments), characters checked.
    /// </summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.XmlReaderDoesTheXml), Scope = ExceptionScope.Boundary)]
    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = false,
        IgnoreProcessingInstructions = false,
        IgnoreWhitespace = false,
        CheckCharacters = true,
        CloseInput = false,
    };
}
