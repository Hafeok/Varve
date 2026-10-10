// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using System.Xml;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.RdfXml;

/// <summary>
/// Writes RDF/XML, one triple at a time, over <see cref="XmlWriter"/> (ADR 0122).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Streaming, and that decides the output's shape.</strong> Each
/// triple is written as it arrives: an <c>rdf:Description</c> is opened when
/// the subject changes and closed when it changes again, so a caller whose
/// triples are grouped by subject gets one description per subject and a
/// caller whose triples are interleaved gets several. Both denote the same
/// graph. The alternative — one description per subject always — needs the
/// whole graph in memory before the first byte.
/// </para>
/// <para>
/// <strong>What it cannot spell, it refuses by name</strong> (<c>rdf-xml.md</c>
/// §6): a quad with a graph label, since RDF/XML has no graphs; a triple term
/// as subject or predicate; a predicate whose IRI has no suffix that is an
/// XML <c>NCName</c>; a term holding a character XML 1.0 cannot carry. Each
/// is an <see cref="InvalidOperationException"/> that names the term.
/// </para>
/// <para>
/// <strong>It invents namespace prefixes</strong>, which the Turtle writer
/// will not: a property element <em>must</em> be a qualified name, so a
/// predicate whose namespace nobody declared gets <c>ns0</c>, <c>ns1</c>, …
/// A prefix the caller declares is used instead.
/// </para>
/// <para>
/// Terms are written as characters through one reusable buffer, never as
/// strings; the one string per <em>distinct</em> predicate — its local name,
/// which the XML writer takes only as a string — is cached. What
/// <see cref="XmlWriter"/> allocates per element is measured and stated
/// (<c>rdf-xml.md</c> §7).
/// </para>
/// </remarks>
public sealed class RdfXmlWriter : IDisposable
{
    private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    private const string ItsNs = "http://www.w3.org/2005/11/its";

    private static ReadOnlySpan<byte> XsdString => "http://www.w3.org/2001/XMLSchema#string"u8;

    private readonly BufferWriterStream _stream;
    private readonly XmlWriter _xml;
    private readonly List<(string Prefix, string Namespace)> _declared = [];
    private readonly Dictionary<string, string> _prefixByNamespace = new(StringComparer.Ordinal);
    private readonly PredicateNames _names = new();
    private char[] _chars = new char[256];
    private byte[] _subject = new byte[64];
    private int _subjectLength;
    private RdfTermKind _subjectKind;
    private bool _rootWritten;
    private bool _descriptionOpen;
    private bool _descriptionIsRdf12;
    private int _generated;
    private bool _disposed;

    /// <summary>Creates a writer over <paramref name="output"/>.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.XmlWriterDoesTheXml), Scope = ExceptionScope.Boundary)]
    public RdfXmlWriter(IBufferWriter<byte> output, in RdfXmlWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        _stream = new BufferWriterStream(output);
        _xml = XmlWriter.Create(_stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = options.Indent,
            IndentChars = "  ",
            NewLineChars = "\n",
            CloseOutput = false,
            CheckCharacters = true,
        });
    }

    /// <summary>
    /// Declares a namespace prefix, used for every predicate in that
    /// namespace from here on. Declared before the first triple it goes on
    /// <c>rdf:RDF</c>; declared after, on the first element that needs it.
    /// </summary>
    /// <param name="prefix">The prefix, without its colon; an <c>NCName</c>.</param>
    /// <param name="iri">The namespace name.</param>
    public void DeclarePrefix(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> iri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!XmlNames.IsNcName(prefix))
        {
            throw new ArgumentException("A namespace prefix is an XML NCName.", nameof(prefix));
        }

        string p = Encoding.UTF8.GetString(prefix);
        string ns = Encoding.UTF8.GetString(iri);

        if (string.Equals(p, "xml", StringComparison.Ordinal) || string.Equals(p, "xmlns", StringComparison.Ordinal) || p.StartsWith("xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Prefixes beginning with 'xml' are reserved by Namespaces in XML.", nameof(prefix));
        }

        _prefixByNamespace[ns] = p;

        if (!_rootWritten)
        {
            _declared.Add((p, ns));
        }
    }

    /// <summary>Writes one triple. The quad must be in the default graph.</summary>
    public void Write(in QuadView quad)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (quad.HasGraph)
        {
            throw new InvalidOperationException(
                "RDF/XML has no graphs, and this quad names one: <" + Encoding.UTF8.GetString(quad.Graph.Lexical) + ">. "
                + "A dataset wants TriG or N-Quads.");
        }

        if (quad.Subject.Kind == RdfTermKind.TripleTerm || quad.Predicate.Kind == RdfTermKind.TripleTerm)
        {
            ThrowTripleTermOutOfPlace();
        }

        bool rdf12 = IsRdf12(quad.Object.Kind, quad.Object.Direction);
        EnsureRoot();
        EnsureDescription(quad.Subject.Kind, quad.Subject.Lexical, rdf12);
        WriteProperty(quad.Predicate.Lexical, quad.Object);
    }

    /// <summary>
    /// Writes one triple whose terms live in a quad source, materialising each
    /// term as it goes. The allocating path, for a caller holding handles.
    /// </summary>
    public void Write(in Quad quad, IQuadSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        if (!quad.IsDefaultGraph)
        {
            throw new InvalidOperationException("RDF/XML has no graphs, and this quad names one. A dataset wants TriG or N-Quads.");
        }

        RdfTerm subject = Externalise(source, quad.Subject);
        RdfTerm predicate = Externalise(source, quad.Predicate);
        RdfTerm obj = Externalise(source, quad.Object);

        if (subject.Kind == RdfTermKind.TripleTerm || predicate.Kind == RdfTermKind.TripleTerm)
        {
            ThrowTripleTermOutOfPlace();
        }

        bool rdf12 = IsRdf12(obj.Kind, obj.Direction);
        EnsureRoot();
        EnsureDescription(subject.Kind, subject.Lexical, rdf12);
        WriteProperty(predicate.Lexical, obj);
    }

    /// <summary>Flushes what the XML writer holds to the output. The open description stays open.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _xml.Flush();
    }

    /// <summary>Closes the description and the document, and flushes. A document with no triples is an empty <c>rdf:RDF</c>.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        EnsureRoot();
        CloseDescription();
        _xml.WriteEndElement();
        _xml.WriteEndDocument();
        _xml.Flush();
        _xml.Dispose();
        _stream.Dispose();
        _disposed = true;
    }

    // ---------------------------------------------------------- structure

    private void EnsureRoot()
    {
        if (_rootWritten)
        {
            return;
        }

        _rootWritten = true;
        _xml.WriteStartDocument();
        _xml.WriteStartElement("rdf", "RDF", RdfNs);

        // Declared explicitly and first: XmlWriter would otherwise put the
        // element's own namespace after every attribute written here.
        _xml.WriteAttributeString("xmlns", "rdf", null, RdfNs);

        foreach ((string prefix, string ns) in _declared)
        {
            _xml.WriteAttributeString("xmlns", prefix, null, ns);
        }
    }

    /// <summary>
    /// Opens a description for <paramref name="subject"/> unless the open one
    /// is its. A triple that needs RDF 1.2 reopens the description with
    /// <c>rdf:version="1.2"</c> when the open one lacks it: the attribute
    /// goes on a node element in scope (RDF 1.2 XML §3.1), and a streaming
    /// writer cannot know at the root what is coming.
    /// </summary>
    private void EnsureDescription(RdfTermKind kind, ReadOnlySpan<byte> subject, bool rdf12)
    {
        if (_descriptionOpen && kind == _subjectKind && subject.SequenceEqual(_subject.AsSpan(0, _subjectLength)) && (!rdf12 || _descriptionIsRdf12))
        {
            return;
        }

        CloseDescription();

        if (subject.Length > _subject.Length)
        {
            GrowSubject(subject.Length);
        }

        subject.CopyTo(_subject);
        _subjectLength = subject.Length;
        _subjectKind = kind;
        _descriptionOpen = true;
        _descriptionIsRdf12 = rdf12;

        _xml.WriteStartElement("rdf", "Description", RdfNs);

        if (rdf12)
        {
            _xml.WriteAttributeString("rdf", "version", RdfNs, "1.2");
        }

        WriteNodeAttribute(kind, subject, aboutName: "about");
    }

    private void CloseDescription()
    {
        if (!_descriptionOpen)
        {
            return;
        }

        _descriptionOpen = false;
        _xml.WriteEndElement();
    }

    /// <summary><c>rdf:about</c> or <c>rdf:resource</c> for an IRI, <c>rdf:nodeID</c> for a blank node.</summary>
    private void WriteNodeAttribute(RdfTermKind kind, ReadOnlySpan<byte> lexical, string aboutName)
    {
        if (kind == RdfTermKind.BlankNode)
        {
            _xml.WriteStartAttribute("rdf", "nodeID", RdfNs);

            // rdf:nodeID is an NCName. A label this library invents is a
            // number, which cannot begin one, so it is prefixed; a document's
            // own label is an NCName already and passes through.
            if (!XmlNames.IsNcName(lexical))
            {
                _xml.WriteChars(['b'], 0, 1);
            }

            WriteChars(lexical);
            _xml.WriteEndAttribute();
            return;
        }

        _xml.WriteStartAttribute("rdf", aboutName, RdfNs);
        WriteChars(lexical);
        _xml.WriteEndAttribute();
    }

    private void WriteProperty(ReadOnlySpan<byte> predicate, in RdfTermView obj)
    {
        StartProperty(predicate);

        switch (obj.Kind)
        {
            case RdfTermKind.Iri:
                WriteNodeAttribute(RdfTermKind.Iri, obj.Lexical, aboutName: "resource");
                break;

            case RdfTermKind.BlankNode:
                WriteNodeAttribute(RdfTermKind.BlankNode, obj.Lexical, aboutName: "resource");
                break;

            case RdfTermKind.TripleTerm:
                _xml.WriteAttributeString("rdf", "parseType", RdfNs, "Triple");
                _xml.WriteStartElement("rdf", "Description", RdfNs);
                WriteNodeAttribute(obj.Subject.Kind, obj.Subject.Lexical, aboutName: "about");
                WriteProperty(obj.Predicate.Lexical, obj.Object);
                _xml.WriteEndElement();
                break;

            default:
                WriteLiteral(obj.Lexical, obj.HasLanguage ? obj.Language : default, obj.HasDatatype ? obj.Datatype : default, obj.Direction);
                break;
        }

        _xml.WriteEndElement();
    }

    private void WriteProperty(ReadOnlySpan<byte> predicate, RdfTerm obj)
    {
        StartProperty(predicate);

        switch (obj.Kind)
        {
            case RdfTermKind.Iri:
                WriteNodeAttribute(RdfTermKind.Iri, obj.Lexical, aboutName: "resource");
                break;

            case RdfTermKind.BlankNode:
                WriteNodeAttribute(RdfTermKind.BlankNode, obj.Lexical, aboutName: "resource");
                break;

            case RdfTermKind.TripleTerm:
                if (obj.Subject!.Kind == RdfTermKind.TripleTerm || obj.Predicate!.Kind == RdfTermKind.TripleTerm)
                {
                    ThrowTripleTermOutOfPlace();
                }

                _xml.WriteAttributeString("rdf", "parseType", RdfNs, "Triple");
                _xml.WriteStartElement("rdf", "Description", RdfNs);
                WriteNodeAttribute(obj.Subject.Kind, obj.Subject.Lexical, aboutName: "about");
                WriteProperty(obj.Predicate!.Lexical, obj.Object!);
                _xml.WriteEndElement();
                break;

            default:
                WriteLiteral(obj.Lexical, obj.Language, obj.Datatype is null ? default : obj.Datatype.Lexical, obj.Direction);
                break;
        }

        _xml.WriteEndElement();
    }

    private void StartProperty(ReadOnlySpan<byte> predicate)
    {
        (string prefix, string local, string ns) = _names.Get(predicate, this);
        _xml.WriteStartElement(prefix, local, ns);
    }

    private void WriteLiteral(ReadOnlySpan<byte> lexical, ReadOnlySpan<byte> language, ReadOnlySpan<byte> datatype, TextDirection direction)
    {
        if (!language.IsEmpty)
        {
            _xml.WriteStartAttribute("xml", "lang", XmlNs);
            WriteChars(language);
            _xml.WriteEndAttribute();

            if (direction != TextDirection.None)
            {
                _xml.WriteAttributeString("its", "dir", ItsNs, direction == TextDirection.LeftToRight ? "ltr" : "rtl");
            }
        }
        else if (!datatype.IsEmpty && !datatype.SequenceEqual(XsdString))
        {
            // A plain literal is xsd:string (RDF 1.1 Concepts §3.3), so the
            // datatype is written only when it says something.
            _xml.WriteStartAttribute("rdf", "datatype", RdfNs);
            WriteChars(datatype);
            _xml.WriteEndAttribute();
        }

        WriteChars(lexical);
    }

    /// <summary>
    /// The namespace and local name a predicate is written as, with the prefix
    /// for the namespace: the caller's, or an invented <c>nsN</c>.
    /// </summary>
    private (string Prefix, string Local, string Namespace) Name(ReadOnlySpan<byte> predicate)
    {
        if (!XmlNames.TrySplitQName(predicate, out int localStart))
        {
            throw new InvalidOperationException(
                "RDF/XML writes a predicate as a qualified name, and no suffix of <" + Encoding.UTF8.GetString(predicate)
                + "> is an XML NCName: it ends in a character that cannot end a name, or begins no name. "
                + "The triple cannot be spelt in this syntax (RDF 1.1 XML Syntax §2.7).");
        }

        string ns = Encoding.UTF8.GetString(predicate[..localStart]);
        string local = Encoding.UTF8.GetString(predicate[localStart..]);

        if (!_prefixByNamespace.TryGetValue(ns, out string? prefix))
        {
            prefix = string.Equals(ns, RdfNs, StringComparison.Ordinal) ? "rdf" : NextPrefix();
            _prefixByNamespace[ns] = prefix;
        }

        return (prefix, local, ns);
    }

    private string NextPrefix()
    {
        while (true)
        {
            string candidate = string.Create(CultureInfo.InvariantCulture, $"ns{_generated++}");

            if (!_prefixByNamespace.ContainsValue(candidate))
            {
                return candidate;
            }
        }
    }

    // ---------------------------------------------------------- characters

    /// <summary>Writes UTF-8 bytes as characters, through the one buffer, refusing a character XML 1.0 cannot carry.</summary>
    private void WriteChars(ReadOnlySpan<byte> utf8)
    {
        if (_chars.Length < utf8.Length)
        {
            GrowChars(utf8.Length);
        }

        OperationStatus status = Utf8.ToUtf16(utf8, _chars, out _, out int written, replaceInvalidSequences: true, isFinalBlock: true);

        if (status != OperationStatus.Done)
        {
            throw new InvalidOperationException("A term's bytes are not UTF-8.");
        }

        for (int i = 0; i < written; i++)
        {
            char c = _chars[i];

            if (c < 0x20 && c is not ('\t' or '\n' or '\r'))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"XML 1.0 cannot carry the character U+{(int)c:X4}, which this term holds; the triple cannot be spelt in RDF/XML."));
            }
        }

        _xml.WriteChars(_chars, 0, written);
    }

    private static bool IsRdf12(RdfTermKind kind, TextDirection direction) =>
        kind == RdfTermKind.TripleTerm || (kind == RdfTermKind.Literal && direction != TextDirection.None);

    private static void ThrowTripleTermOutOfPlace() =>
        throw new InvalidOperationException(
            "A triple term is an object and nothing else in RDF/XML (RDF 1.2 XML §2.19). "
            + "This quad carries one as its subject or predicate, and no document could spell it.");

    private static RdfTerm Externalise(IQuadSource source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term)
            ? term
            : throw new InvalidOperationException(
                "The quad source cannot externalise a term of this quad. A term whose key has been "
                + "destroyed has no serialisation, and writing a placeholder would claim it does.");

    [DesignDecision(typeof(RdfTermRepresentation.ViewsNestByArena), Scope = ExceptionScope.HotPath)]
    private void GrowChars(int needed) => Array.Resize(ref _chars, Math.Max(needed, _chars.Length * 2));

    [DesignDecision(typeof(RdfTermRepresentation.ViewsNestByArena), Scope = ExceptionScope.HotPath)]
    private void GrowSubject(int needed) => Array.Resize(ref _subject, Math.Max(needed, _subject.Length * 2));

    /// <summary>
    /// The names of the predicates seen so far, keyed by their bytes: one
    /// split and one set of strings per distinct predicate, none per triple.
    /// </summary>
    private sealed class PredicateNames
    {
        private byte[][] _keys = new byte[16][];
        private (string Prefix, string Local, string Namespace)[] _values = new (string, string, string)[16];
        private int _count;

        internal (string Prefix, string Local, string Namespace) Get(ReadOnlySpan<byte> predicate, RdfXmlWriter writer)
        {
            for (int i = 0; i < _count; i++)
            {
                if (predicate.SequenceEqual(_keys[i]))
                {
                    return _values[i];
                }
            }

            return Add(predicate, writer);
        }

        [DesignDecision(typeof(RdfXmlOverSystemXml.PredicateNamesAreCached), Scope = ExceptionScope.HotPath)]
        private (string Prefix, string Local, string Namespace) Add(ReadOnlySpan<byte> predicate, RdfXmlWriter writer)
        {
            (string, string, string) value = writer.Name(predicate);

            if (_count == _keys.Length)
            {
                Array.Resize(ref _keys, _count * 2);
                Array.Resize(ref _values, _count * 2);
            }

            _keys[_count] = predicate.ToArray();
            _values[_count] = value;
            _count++;
            return value;
        }
    }
}
