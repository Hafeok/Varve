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
using Varve.Iri;
using Varve.Rdf;
using Varve.RdfXml.Model;

namespace Varve.RdfXml;

/// <summary>
/// The RDF/XML grammar (RDF 1.1 XML Syntax §7, RDF 1.2 XML §6) over the
/// events one <see cref="XmlReader"/> reports.
/// </summary>
/// <remarks>
/// <para>
/// Recursive descent on element starts: a node element consumes its own
/// subtree and returns the arena slot of its subject; a property element
/// consumes its own subtree and emits its triple. Scope — <c>xml:base</c>,
/// <c>xml:lang</c>, <c>its:dir</c> — is a stack pushed per element.
/// </para>
/// <para>
/// Terms live in one <see cref="TermArena"/>, reset at each top-level node
/// element: that is the unit whose terms can all be referenced at once, the
/// counterpart of Turtle's statement. Text and attribute values come out of
/// the reader in chunks into one <c>char</c> buffer and are transcoded into
/// the arena; element and attribute names are the reader's atomised strings
/// and are compared, never copied. What the reader itself allocates per
/// element is measured and stated (<c>rdf-xml.md</c> §7), not hidden.
/// </para>
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class RdfXmlReaderCore
{
    private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNs = "http://www.w3.org/2000/xmlns/";
    private const string ItsNs = "http://www.w3.org/2005/11/its";

    private static ReadOnlySpan<byte> RdfNsBytes => "http://www.w3.org/1999/02/22-rdf-syntax-ns#"u8;

    private static ReadOnlySpan<byte> RdfType => "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8;

    private static ReadOnlySpan<byte> RdfFirst => "http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8;

    private static ReadOnlySpan<byte> RdfRest => "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8;

    private static ReadOnlySpan<byte> RdfNil => "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"u8;

    private static ReadOnlySpan<byte> RdfStatement => "http://www.w3.org/1999/02/22-rdf-syntax-ns#Statement"u8;

    private static ReadOnlySpan<byte> RdfSubject => "http://www.w3.org/1999/02/22-rdf-syntax-ns#subject"u8;

    private static ReadOnlySpan<byte> RdfPredicate => "http://www.w3.org/1999/02/22-rdf-syntax-ns#predicate"u8;

    private static ReadOnlySpan<byte> RdfObject => "http://www.w3.org/1999/02/22-rdf-syntax-ns#object"u8;

    private static ReadOnlySpan<byte> RdfReifies => "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8;

    private static ReadOnlySpan<byte> RdfXmlLiteral => "http://www.w3.org/1999/02/22-rdf-syntax-ns#XMLLiteral"u8;

    private readonly XmlReader _reader;
    private readonly IXmlLineInfo? _lines;
    private readonly RdfXmlQuadHandler _handler;
    private readonly NamespaceHandler? _onNamespace;
    private readonly bool _validateIris;
    private readonly byte[] _documentBase;
    private readonly char[] _chars = new char[512];
    private Scope[] _scopes = new Scope[16];
    private int _scopeCount;
    private HashSet<string>? _ids;
    private long _blankCounter;
    private long _quadCount;
    private Captured[] _captured = new Captured[4];
    private int _capturedCount;
    private int _captureDepth;
    private PropertyAttribute[] _attributes = new PropertyAttribute[8];

    internal RdfXmlReaderCore(XmlReader reader, RdfXmlQuadHandler handler, in RdfXmlOptions options)
    {
        _reader = reader;
        _lines = reader as IXmlLineInfo;
        _handler = handler;
        _onNamespace = options.OnNamespace;
        _validateIris = options.ValidateIris;
        _documentBase = options.BaseIri.ToArray();
    }

    /// <summary>What was in scope on an element: the base, the language and the direction.</summary>
    private struct Scope
    {
        /// <summary>The in-scope base, or null for the document's; <see cref="HasBase"/> false means none at all.</summary>
        internal byte[]? Base;
        internal bool HasBase;

        /// <summary>The in-scope language, or empty for none.</summary>
        internal byte[] Language;
        internal TextDirection Direction;

        /// <summary>Whether <c>rdf:version</c> in scope announces RDF 1.2 or later (RDF 1.2 XML §3.1).</summary>
        internal bool Rdf12;
    }

    private struct Captured
    {
        internal int Subject;
        internal int Predicate;
        internal int Object;
    }

    private struct PropertyAttribute
    {
        internal int Predicate;
        internal TermSpan Value;
        internal bool IsType;
    }

    private enum ParseType : byte
    {
        None,
        Resource,
        Collection,
        Literal,
        Triple,
    }

    private sealed class StopException(RdfXmlParseError error) : Exception(error.Message)
    {
        internal RdfXmlParseError Error { get; } = error;
    }

    [DesignDecision(typeof(RdfXmlOverSystemXml.NoRecoveryUnit), Scope = ExceptionScope.HotPath)]
    internal RdfXmlParseResult Run()
    {
        try
        {
            Document();
            return new RdfXmlParseResult(_quadCount, null);
        }
        catch (StopException stop)
        {
            return new RdfXmlParseResult(_quadCount, stop.Error);
        }
        catch (XmlException xml)
        {
            return new RdfXmlParseResult(
                _quadCount,
                new RdfXmlParseError(RdfXmlErrorKind.MalformedXml, xml.LineNumber, xml.LinePosition, xml.Message));
        }
    }

    // ------------------------------------------------------------ document

    /// <summary>[6.2.8] <c>doc</c>: an <c>rdf:RDF</c> element holding node elements, or one node element alone.</summary>
    private void Document()
    {
        _scopes[0] = new Scope { HasBase = _documentBase.Length > 0, Base = null, Language = [] };
        _scopeCount = 1;

        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (IsRdf("RDF"))
                    {
                        PushScope();
                        ReadAttributesOfRoot();

                        if (!_reader.IsEmptyElement)
                        {
                            NodeElementList(topLevel: true);
                        }

                        PopScope();
                    }
                    else
                    {
                        Arena.Reset();
                        NodeElement();
                    }

                    SkipTrailer();
                    return;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    Fail(RdfXmlErrorKind.UnexpectedText, "Text before the document's root element.");
                    return;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document holds no element.");
    }

    private void SkipTrailer()
    {
        while (_reader.Read())
        {
            // Well-formedness leaves nothing but whitespace, comments and
            // processing instructions after the root element; the reader
            // refuses a second root itself.
        }
    }

    /// <summary>The root's attributes: namespaces reported, scope taken, <c>rdf:version</c> allowed, nothing else.</summary>
    private void ReadAttributesOfRoot()
    {
        for (bool more = _reader.MoveToFirstAttribute(); more; more = _reader.MoveToNextAttribute())
        {
            if (TakeScopeAttribute())
            {
                continue;
            }

            if (IsRdfAttribute("version"))
            {
                TakeVersion();
                continue;
            }

            Fail(RdfXmlErrorKind.ForbiddenAttribute, "rdf:RDF takes no attribute but namespace declarations, xml:base, xml:lang, its:dir and rdf:version.");
        }

        _reader.MoveToElement();
    }

    /// <summary>[6.2.9] <c>nodeElementList</c>: node elements separated by whitespace, inside an element the caller is positioned on.</summary>
    private void NodeElementList(bool topLevel)
    {
        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (topLevel)
                    {
                        Arena.Reset();
                    }

                    NodeElement();
                    continue;

                case XmlNodeType.EndElement:
                    return;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    RequireWhitespaceText();
                    continue;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside an element.");
    }

    // -------------------------------------------------------- node elements

    /// <summary>
    /// [6.2.11] <c>nodeElement</c>, the reader positioned on its start tag.
    /// Consumes through its end tag and returns the arena slot of its subject.
    /// </summary>
    private int NodeElement()
    {
        string ns = _reader.NamespaceURI;
        string local = _reader.LocalName;

        if (ns.Length == 0)
        {
            Fail(RdfXmlErrorKind.ForbiddenElementName, "A node element must have a namespace name.");
        }

        if (string.Equals(ns, RdfNs, StringComparison.Ordinal) && IsForbiddenNodeElementName(local))
        {
            Fail(RdfXmlErrorKind.ForbiddenElementName, "rdf:", local, " is not a node element name (RDF 1.2 XML §6.2.5).");
        }

        PushScope();

        // Pass one: scope attributes, namespaces, and the one attribute that
        // names the subject.
        TermSpan about = TermSpan.None;
        TermSpan id = TermSpan.None;
        TermSpan nodeId = TermSpan.None;
        int subjectAttributes = 0;

        for (bool more = _reader.MoveToFirstAttribute(); more; more = _reader.MoveToNextAttribute())
        {
            if (TakeScopeAttribute())
            {
                continue;
            }

            string ans = _reader.NamespaceURI;
            string alocal = _reader.LocalName;

            if (string.Equals(ans, RdfNs, StringComparison.Ordinal))
            {
                switch (alocal)
                {
                    case "about":
                        about = Value();
                        subjectAttributes++;
                        continue;
                    case "ID":
                        id = Value();
                        subjectAttributes++;
                        continue;
                    case "nodeID":
                        nodeId = Value();
                        subjectAttributes++;
                        continue;
                    case "version":
                        TakeVersion();
                        continue;
                    case "type":
                        continue;
                    default:
                        if (IsForbiddenPropertyAttributeName(alocal))
                        {
                            Fail(RdfXmlErrorKind.ForbiddenAttribute, "rdf:", alocal, " is not a property attribute (RDF 1.2 XML §6.2.7).");
                        }

                        continue;
                }
            }

            if (ans.Length == 0 && !IsReservedXmlName(alocal))
            {
                Fail(RdfXmlErrorKind.ForbiddenAttribute, "An attribute with no namespace name is not a property attribute: '", alocal, "'.");
            }
        }

        if (subjectAttributes > 1)
        {
            Fail(RdfXmlErrorKind.ConflictingAttributes, "A node element takes at most one of rdf:ID, rdf:about and rdf:nodeID.");
        }

        int subject;

        if (about.IsPresent)
        {
            subject = Arena.AddIri(Resolve(about));
        }
        else if (id.IsPresent)
        {
            subject = Arena.AddIri(IdIri(id));
        }
        else if (nodeId.IsPresent)
        {
            subject = BlankNode(nodeId);
        }
        else
        {
            subject = FreshBlankNode();
        }

        if (!(string.Equals(ns, RdfNs, StringComparison.Ordinal) && string.Equals(local, "Description", StringComparison.Ordinal)))
        {
            Emit(subject, Iri(RdfType), ElementIri(ns, local));
        }

        // Pass two: property attributes, now that the subject is known and
        // xml:lang on this element is in scope.
        for (bool more = _reader.MoveToFirstAttribute(); more; more = _reader.MoveToNextAttribute())
        {
            string ans = _reader.NamespaceURI;

            if (string.Equals(ans, XmlnsNs, StringComparison.Ordinal) || string.Equals(ans, XmlNs, StringComparison.Ordinal) || string.Equals(ans, ItsNs, StringComparison.Ordinal))
            {
                continue;
            }

            string alocal = _reader.LocalName;

            if (ans.Length == 0)
            {
                // An attribute with no namespace whose name begins "xml" is
                // reserved by XML and ignored (RDF 1.1 XML Syntax §6.1.4);
                // pass one refused every other one.
                continue;
            }

            if (string.Equals(ans, RdfNs, StringComparison.Ordinal))
            {
                if (string.Equals(alocal, "type", StringComparison.Ordinal))
                {
                    Emit(subject, Iri(RdfType), Arena.AddIri(Resolve(Value())));
                    continue;
                }

                if (IsCoreSyntaxTerm(alocal))
                {
                    continue;
                }

                // rdf:value, rdf:_n, rdf:Seq and any other name in the RDF
                // namespace that §6.2.7 does not exclude is a property
                // attribute like any other (rdfms-rdf-names-use).
            }

            Emit(subject, ElementIri(ans, alocal), PlainLiteral(Value()));
        }

        _reader.MoveToElement();

        if (_reader.IsEmptyElement)
        {
            PopScope();
            return subject;
        }

        int li = 0;

        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    PropertyElement(subject, ref li);
                    continue;

                case XmlNodeType.EndElement:
                    PopScope();
                    return subject;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    RequireWhitespaceText();
                    continue;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside a node element.");
        return -1;
    }

    // ---------------------------------------------------- property elements

    /// <summary>[6.2.14] <c>propertyElt</c>, the reader positioned on its start tag. Consumes through its end tag.</summary>
    private void PropertyElement(int subject, ref int li)
    {
        string ns = _reader.NamespaceURI;
        string local = _reader.LocalName;

        if (ns.Length == 0)
        {
            Fail(RdfXmlErrorKind.ForbiddenElementName, "A property element must have a namespace name.");
        }

        int predicate;

        if (string.Equals(ns, RdfNs, StringComparison.Ordinal))
        {
            if (string.Equals(local, "li", StringComparison.Ordinal))
            {
                predicate = MemberPredicate(++li);
            }
            else
            {
                if (IsForbiddenPropertyElementName(local))
                {
                    Fail(RdfXmlErrorKind.ForbiddenElementName, "rdf:", local, " is not a property element name (RDF 1.2 XML §6.2.6).");
                }

                predicate = ElementIri(ns, local);
            }
        }
        else
        {
            predicate = ElementIri(ns, local);
        }

        PushScope();

        TermSpan id = TermSpan.None;
        TermSpan datatype = TermSpan.None;
        TermSpan resource = TermSpan.None;
        TermSpan nodeId = TermSpan.None;
        TermSpan annotation = TermSpan.None;
        TermSpan annotationNodeId = TermSpan.None;
        ParseType parseType = ParseType.None;
        int attributeCount = 0;

        for (bool more = _reader.MoveToFirstAttribute(); more; more = _reader.MoveToNextAttribute())
        {
            if (TakeScopeAttribute())
            {
                continue;
            }

            string ans = _reader.NamespaceURI;
            string alocal = _reader.LocalName;

            if (string.Equals(ans, RdfNs, StringComparison.Ordinal))
            {
                switch (alocal)
                {
                    case "ID":
                        id = Value();
                        continue;
                    case "parseType":
                        parseType = ParseTypeOf(Value());
                        continue;
                    case "datatype":
                        datatype = Value();
                        continue;
                    case "resource":
                        resource = Value();
                        continue;
                    case "nodeID":
                        nodeId = Value();
                        continue;
                    case "annotation":
                        annotation = Value();
                        continue;
                    case "annotationNodeID":
                        annotationNodeId = Value();
                        continue;
                    case "version":
                        TakeVersion();
                        continue;
                    case "type":
                        break;
                    default:
                        if (IsForbiddenPropertyAttributeName(alocal))
                        {
                            Fail(RdfXmlErrorKind.ForbiddenAttribute, "rdf:", alocal, " is not allowed on a property element.");
                        }

                        break;
                }
            }
            else if (ans.Length == 0)
            {
                if (IsReservedXmlName(alocal))
                {
                    continue;
                }

                Fail(RdfXmlErrorKind.ForbiddenAttribute, "An attribute with no namespace name is not a property attribute: '", alocal, "'.");
            }

            // A property attribute, which only the empty form admits; kept
            // until the form is known.
            if (attributeCount == _attributes.Length)
            {
                GrowAttributes();
            }

            bool isType = string.Equals(ans, RdfNs, StringComparison.Ordinal) && string.Equals(alocal, "type", StringComparison.Ordinal);
            _attributes[attributeCount++] = new PropertyAttribute
            {
                Predicate = isType ? -1 : ElementIri(ans, alocal),
                Value = Value(),
                IsType = isType,
            };
        }

        _reader.MoveToElement();

        if (resource.IsPresent && nodeId.IsPresent)
        {
            Fail(RdfXmlErrorKind.ConflictingAttributes, "A property element takes rdf:resource or rdf:nodeID, not both.");
        }

        if (annotation.IsPresent && annotationNodeId.IsPresent)
        {
            Fail(RdfXmlErrorKind.ConflictingAttributes, "A property element takes rdf:annotation or rdf:annotationNodeID, not both.");
        }

        int obj;

        switch (parseType)
        {
            case ParseType.Resource:
                RequireNoObjectAttributes(resource, nodeId, datatype, attributeCount, "rdf:parseType=\"Resource\"");
                obj = FreshBlankNode();
                Emit(subject, predicate, obj);
                Reify(id, subject, predicate, obj);
                Annotate(annotation, annotationNodeId, subject, predicate, obj);
                PropertyElementList(obj);
                break;

            case ParseType.Collection:
                RequireNoObjectAttributes(resource, nodeId, datatype, attributeCount, "rdf:parseType=\"Collection\"");
                obj = Collection();
                Emit(subject, predicate, obj);
                Reify(id, subject, predicate, obj);
                Annotate(annotation, annotationNodeId, subject, predicate, obj);
                break;

            case ParseType.Literal:
                RequireNoObjectAttributes(resource, nodeId, datatype, attributeCount, "rdf:parseType=\"Literal\"");
                obj = Arena.AddLiteral(CanonicalXml.Read(_reader, this), Arena.AppendScratch(RdfXmlLiteral), TermSpan.None, TextDirection.None);
                Emit(subject, predicate, obj);
                Reify(id, subject, predicate, obj);
                Annotate(annotation, annotationNodeId, subject, predicate, obj);
                break;

            case ParseType.Triple when !_scopes[_scopeCount - 1].Rdf12:
                // RDF 1.2 XML §3.1: without rdf:version announcing 1.2, the
                // RDF 1.2 forms are ignored, and the suite's tt-01 ("Ignored
                // triple term") expects no triple at all from this element.
                SkipSubtree();
                break;

            case ParseType.Triple:
                RequireNoObjectAttributes(resource, nodeId, datatype, attributeCount, "rdf:parseType=\"Triple\"");

                if (id.IsPresent || annotation.IsPresent || annotationNodeId.IsPresent)
                {
                    Fail(RdfXmlErrorKind.ForbiddenAttribute, "rdf:parseType=\"Triple\" takes no rdf:ID, rdf:annotation or rdf:annotationNodeID (RDF 1.2 XML §6.2.19).");
                }

                obj = TripleTerm();
                Emit(subject, predicate, obj);
                break;

            default:
                PropertyElementWithoutParseType(subject, predicate, id, datatype, resource, nodeId, annotation, annotationNodeId, attributeCount);
                break;
        }

        PopScope();
    }

    /// <summary>
    /// The three forms told apart by content: [6.2.15] resource (one node
    /// element), [6.2.16] literal (text), [6.2.21] empty (nothing).
    /// </summary>
    private void PropertyElementWithoutParseType(
        int subject,
        int predicate,
        TermSpan id,
        TermSpan datatype,
        TermSpan resource,
        TermSpan nodeId,
        TermSpan annotation,
        TermSpan annotationNodeId,
        int attributeCount)
    {
        if (_reader.IsEmptyElement)
        {
            EmptyPropertyElement(subject, predicate, id, datatype, resource, nodeId, annotation, annotationNodeId, attributeCount);
            return;
        }

        // Read the content: text accumulates into one scratch run; the first
        // element decides the resource form.
        int pending = 0;
        bool sawText = false;
        bool sawNonWhitespace = false;

        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    sawText = true;
                    AppendValue(ref pending, ref sawNonWhitespace);
                    continue;

                case XmlNodeType.Element:
                    if (sawNonWhitespace)
                    {
                        Fail(RdfXmlErrorKind.UnexpectedContent, "A property element holds either text or one node element, not both.");
                    }

                    RequireNoObjectAttributes(resource, nodeId, datatype, attributeCount, "a property element with a node element inside it");
                    {
                        int obj = NodeElement();
                        Emit(subject, predicate, obj);
                        Reify(id, subject, predicate, obj);
                        Annotate(annotation, annotationNodeId, subject, predicate, obj);
                        SkipWhitespaceToEndElement();
                        return;
                    }

                case XmlNodeType.EndElement:
                    if (!sawText)
                    {
                        EmptyPropertyElement(subject, predicate, id, datatype, resource, nodeId, annotation, annotationNodeId, attributeCount);
                        return;
                    }

                    {
                        RequireNoObjectAttributes(resource, nodeId, TermSpan.None, attributeCount, "a property element with text inside it");
                        TermSpan lexical = Arena.CommitScratch(pending);
                        int obj = !datatype.IsPresent
                            ? PlainLiteral(lexical)
                            : Arena.AddLiteral(lexical, Resolve(datatype), TermSpan.None, TextDirection.None);
                        Emit(subject, predicate, obj);
                        Reify(id, subject, predicate, obj);
                        Annotate(annotation, annotationNodeId, subject, predicate, obj);
                        return;
                    }

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside a property element.");
    }

    /// <summary>[6.2.21] <c>emptyPropertyElt</c>: a literal, or a resource described by attributes.</summary>
    private void EmptyPropertyElement(
        int subject,
        int predicate,
        TermSpan id,
        TermSpan datatype,
        TermSpan resource,
        TermSpan nodeId,
        TermSpan annotation,
        TermSpan annotationNodeId,
        int attributeCount)
    {
        int obj;

        if (!resource.IsPresent && !nodeId.IsPresent && attributeCount == 0)
        {
            obj = !datatype.IsPresent
                ? PlainLiteral(Arena.AppendScratch(default))
                : Arena.AddLiteral(Arena.AppendScratch(default), Resolve(datatype), TermSpan.None, TextDirection.None);
        }
        else
        {
            if (datatype.IsPresent)
            {
                Fail(RdfXmlErrorKind.ForbiddenAttribute, "rdf:datatype has no meaning on an empty property element describing a resource.");
            }

            obj = resource.IsPresent
                ? Arena.AddIri(Resolve(resource))
                : nodeId.IsPresent ? BlankNode(nodeId) : FreshBlankNode();

            for (int i = 0; i < attributeCount; i++)
            {
                PropertyAttribute attribute = _attributes[i];

                if (attribute.IsType)
                {
                    Emit(obj, Iri(RdfType), Arena.AddIri(Resolve(attribute.Value)));
                }
                else
                {
                    Emit(obj, attribute.Predicate, PlainLiteral(attribute.Value));
                }
            }
        }

        Emit(subject, predicate, obj);
        Reify(id, subject, predicate, obj);
        Annotate(annotation, annotationNodeId, subject, predicate, obj);
    }

    /// <summary>[6.2.18] the content of <c>rdf:parseType="Resource"</c>: property elements about a fresh node.</summary>
    private void PropertyElementList(int subject)
    {
        if (_reader.IsEmptyElement)
        {
            return;
        }

        int li = 0;

        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    PropertyElement(subject, ref li);
                    continue;

                case XmlNodeType.EndElement:
                    return;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    RequireWhitespaceText();
                    continue;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside rdf:parseType=\"Resource\" content.");
    }

    /// <summary>[6.2.19] <c>rdf:parseType="Collection"</c>: the node elements inside, as an <c>rdf:first</c>/<c>rdf:rest</c> list.</summary>
    private int Collection()
    {
        if (_reader.IsEmptyElement)
        {
            return Iri(RdfNil);
        }

        int head = -1;
        int tail = -1;

        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    {
                        int element = NodeElement();
                        int cell = FreshBlankNode();

                        if (head < 0)
                        {
                            head = cell;
                        }
                        else
                        {
                            Emit(tail, Iri(RdfRest), cell);
                        }

                        Emit(cell, Iri(RdfFirst), element);
                        tail = cell;
                        continue;
                    }

                case XmlNodeType.EndElement:
                    if (head < 0)
                    {
                        return Iri(RdfNil);
                    }

                    Emit(tail, Iri(RdfRest), Iri(RdfNil));
                    return head;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    RequireWhitespaceText();
                    continue;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside rdf:parseType=\"Collection\" content.");
        return -1;
    }

    /// <summary>
    /// RDF 1.2 XML §6.2.19 <c>rdf:parseType="Triple"</c>: exactly one node
    /// element describing exactly one triple, which becomes a triple term and
    /// is asserted nowhere.
    /// </summary>
    private int TripleTerm()
    {
        if (_reader.IsEmptyElement)
        {
            Fail(RdfXmlErrorKind.InvalidTripleTerm, "rdf:parseType=\"Triple\" needs one node element describing one triple; the element is empty.");
        }

        int before = _capturedCount;
        _captureDepth++;
        bool sawElement = false;

        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (sawElement)
                    {
                        Fail(RdfXmlErrorKind.InvalidTripleTerm, "rdf:parseType=\"Triple\" holds one node element; a second was found.");
                    }

                    sawElement = true;
                    NodeElement();
                    continue;

                case XmlNodeType.EndElement:
                    _captureDepth--;

                    if (!sawElement || _capturedCount != before + 1)
                    {
                        Fail(RdfXmlErrorKind.InvalidTripleTerm, "rdf:parseType=\"Triple\" needs one node element describing exactly one triple; it describes ", _capturedCount - before);
                    }

                    {
                        Captured triple = _captured[before];
                        _capturedCount = before;
                        return Arena.AddTripleTerm(triple.Subject, triple.Predicate, triple.Object);
                    }

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    RequireWhitespaceText();
                    continue;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside rdf:parseType=\"Triple\" content.");
        return -1;
    }

    // --------------------------------------------------------- emission

    [DesignDecision(typeof(RdfXmlOverSystemXml.TheCallbackIsTheCallers), Scope = ExceptionScope.HotPath)]
    private void Emit(int subject, int predicate, int obj)
    {
        if (_captureDepth > 0)
        {
            if (_capturedCount == _captured.Length)
            {
                GrowCaptured();
            }

            _captured[_capturedCount++] = new Captured { Subject = subject, Predicate = predicate, Object = obj };
            return;
        }

        QuadView quad = Arena.Quad(default, subject, predicate, obj, -1);
        _handler(in quad);
        _quadCount++;
    }

    /// <summary>[6.3] reification by <c>rdf:ID</c> on a property element: four triples about <c>base#ID</c>.</summary>
    private void Reify(TermSpan id, int subject, int predicate, int obj)
    {
        if (!id.IsPresent)
        {
            return;
        }

        int r = Arena.AddIri(IdIri(id));
        Emit(r, Iri(RdfType), Iri(RdfStatement));
        Emit(r, Iri(RdfSubject), subject);
        Emit(r, Iri(RdfPredicate), predicate);
        Emit(r, Iri(RdfObject), obj);
    }

    /// <summary>RDF 1.2 XML §6.2.20: <c>rdf:annotation</c> or <c>rdf:annotationNodeID</c> names a reifier of the triple term.</summary>
    private void Annotate(TermSpan annotation, TermSpan annotationNodeId, int subject, int predicate, int obj)
    {
        if (!annotation.IsPresent && !annotationNodeId.IsPresent)
        {
            return;
        }

        int reifier = annotation.IsPresent ? Arena.AddIri(Resolve(annotation)) : BlankNode(annotationNodeId);
        Emit(reifier, Iri(RdfReifies), Arena.AddTripleTerm(subject, predicate, obj));
    }

    // ---------------------------------------------------------- terms

    private int Iri(ReadOnlySpan<byte> iri) => Arena.AddIri(Arena.AppendScratch(iri));

    /// <summary>The IRI an element or attribute name denotes: its namespace name followed by its local name.</summary>
    private int ElementIri(string ns, string local)
    {
        int pending = 0;
        AppendUtf16(ns.AsSpan(), ref pending);
        AppendUtf16(local.AsSpan(), ref pending);
        TermSpan span = Arena.CommitScratch(pending);
        ValidateIri(span);
        return Arena.AddIri(span);
    }

    private int MemberPredicate(int n)
    {
        Span<byte> destination = Arena.ReserveScratch(RdfNsBytes.Length + 12);
        RdfNsBytes.CopyTo(destination);
        destination[RdfNsBytes.Length] = (byte)'_';
        n.TryFormat(destination[(RdfNsBytes.Length + 1)..], out int digits, provider: null);
        return Arena.AddIri(Arena.CommitScratch(RdfNsBytes.Length + 1 + digits));
    }

    private int PlainLiteral(TermSpan lexical)
    {
        ref Scope scope = ref _scopes[_scopeCount - 1];

        // A direction without a language is no term (RDF 1.2 Concepts §3.3;
        // RDF 1.2 XML §5.1.9): the literal is then a plain xsd:string. And a
        // direction is an RDF 1.2 form, read only where rdf:version announces
        // 1.2 (§3.1; the suite's dir-02 expects "bar"@en without one).
        return scope.Language.Length == 0
            ? Arena.AddLiteral(lexical, TermSpan.None, TermSpan.None, TextDirection.None)
            : Arena.AddLiteral(lexical, TermSpan.None, Arena.AppendScratch(scope.Language), scope.Rdf12 ? scope.Direction : TextDirection.None);
    }

    private int BlankNode(TermSpan label)
    {
        if (!XmlNames.IsNcName(Arena.Bytes(default, label)))
        {
            Fail(RdfXmlErrorKind.InvalidName, "rdf:nodeID must be an XML NCName.");
        }

        return Arena.AddBlankNode(label);
    }

    /// <summary>
    /// A fresh blank node, labelled with a number alone. <c>rdf:nodeID</c>
    /// must be an <c>NCName</c>, which cannot begin with a digit, so no label
    /// the document can write collides with one the parser invents — which
    /// Turtle, with no reserved space, cannot say (<c>turtle.md</c> §4).
    /// </summary>
    private int FreshBlankNode()
    {
        Span<byte> destination = Arena.ReserveScratch(20);
        (++_blankCounter).TryFormat(destination, out int digits, provider: null);
        return Arena.AddBlankNode(Arena.CommitScratch(digits));
    }

    /// <summary>
    /// <c>base#ID</c> (RDF 1.1 XML Syntax §5.3): an <c>NCName</c>, resolved
    /// against the in-scope base, unique in the document.
    /// </summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.IdsAreKeptAsStrings), Scope = ExceptionScope.HotPath)]
    private TermSpan IdIri(TermSpan id)
    {
        ReadOnlySpan<byte> name = Arena.Bytes(default, id);

        if (!XmlNames.IsNcName(name))
        {
            Fail(RdfXmlErrorKind.InvalidName, "rdf:ID must be an XML NCName.");
        }

        Span<byte> fragment = Arena.ReserveScratch(name.Length + 1);
        fragment[0] = (byte)'#';
        name = Arena.Bytes(default, id);
        name.CopyTo(fragment[1..]);
        TermSpan resolved = Resolve(Arena.CommitScratch(name.Length + 1));

        _ids ??= new HashSet<string>(StringComparer.Ordinal);

        if (!_ids.Add(Encoding.UTF8.GetString(Arena.Bytes(default, resolved))))
        {
            Fail(RdfXmlErrorKind.DuplicateId, "The same rdf:ID resolves to the same IRI twice in this document.");
        }

        return resolved;
    }

    /// <summary>Resolves a reference against the in-scope base (RFC 3986 §5.2, through <c>Varve.Iri</c>), and validates it.</summary>
    private TermSpan Resolve(TermSpan reference)
    {
        ReadOnlySpan<byte> text = Arena.Bytes(default, reference);

        if (_validateIris ? IriRef.IsAbsolute(text) : IriRef.StartsWithScheme(text))
        {
            ValidateIri(reference);
            return reference;
        }

        ReadOnlySpan<byte> baseIri = CurrentBase();

        if (baseIri.IsEmpty)
        {
            Fail(RdfXmlErrorKind.RelativeIri, "A relative reference with no base in scope: ", text);
        }

        int needed = IriRef.ResolveLength(baseIri, text);

        if (needed <= 0)
        {
            Fail(RdfXmlErrorKind.InvalidIri, "Not resolvable against the base: ", text);
        }

        Span<byte> destination = Arena.ReserveScratch(needed);
        baseIri = CurrentBase();
        text = Arena.Bytes(default, reference);

        if (!IriRef.TryResolve(baseIri, text, destination, out int written))
        {
            Fail(RdfXmlErrorKind.InvalidIri, "Not resolvable against the base: ", text);
        }

        TermSpan resolved = Arena.CommitScratch(written);
        ValidateIri(resolved);
        return resolved;
    }

    private void ValidateIri(TermSpan span)
    {
        if (!_validateIris)
        {
            return;
        }

        ReadOnlySpan<byte> text = Arena.Bytes(default, span);

        if (!IriRef.TryValidate(text, out IriComponents components, out IriError error))
        {
            Fail(RdfXmlErrorKind.InvalidIri, "Not an IRI: ", text);
        }

        if (!components.HasScheme)
        {
            Fail(RdfXmlErrorKind.RelativeIri, "A relative reference where an IRI is needed: ", text);
        }
    }

    private ReadOnlySpan<byte> CurrentBase()
    {
        ref Scope scope = ref _scopes[_scopeCount - 1];
        return scope.HasBase ? (scope.Base ?? _documentBase) : default;
    }

    // ---------------------------------------------------------- scope

    private void PushScope()
    {
        if (_scopeCount == _scopes.Length)
        {
            GrowScopes();
        }

        _scopes[_scopeCount] = _scopes[_scopeCount - 1];
        _scopeCount++;
    }

    private void PopScope() => _scopeCount--;

    /// <summary>
    /// Takes a namespace declaration, <c>xml:base</c>, <c>xml:lang</c> or
    /// <c>its:dir</c> from the attribute the reader is on. True when it was
    /// one of those; the caller then has nothing more to do with it.
    /// </summary>
    private bool TakeScopeAttribute()
    {
        string ns = _reader.NamespaceURI;

        if (string.Equals(ns, XmlnsNs, StringComparison.Ordinal))
        {
            if (_onNamespace is not null)
            {
                ReportNamespace();
            }

            return true;
        }

        ref Scope scope = ref _scopes[_scopeCount - 1];

        if (string.Equals(ns, XmlNs, StringComparison.Ordinal))
        {
            switch (_reader.LocalName)
            {
                case "base":
                    // xml:base is itself resolved against the outer base (XML
                    // Base §4.3), and a base without a scheme is still a base.
                    {
                        TermSpan value = Value();
                        ReadOnlySpan<byte> text = Arena.Bytes(default, value);

                        if (!IriRef.StartsWithScheme(text) && !CurrentBase().IsEmpty)
                        {
                            value = Resolve(value);
                        }

                        scope.Base = CopyScope(value);
                        scope.HasBase = true;
                    }

                    return true;

                case "lang":
                    {
                        TermSpan value = Value();
                        ReadOnlySpan<byte> text = Arena.Bytes(default, value);

                        if (text.IsEmpty)
                        {
                            scope.Language = [];
                        }
                        else
                        {
                            if (!LanguageTag.IsWellFormed(text))
                            {
                                Fail(RdfXmlErrorKind.InvalidLanguageTag, "xml:lang is not a well-formed language tag: ", text);
                            }

                            scope.Language = CopyScope(value);
                        }
                    }

                    return true;

                default:
                    return true;
            }
        }

        if (string.Equals(ns, ItsNs, StringComparison.Ordinal))
        {
            if (string.Equals(_reader.LocalName, "dir", StringComparison.Ordinal))
            {
                TermSpan value = Value();
                ReadOnlySpan<byte> text = Arena.Bytes(default, value);

                if (text.SequenceEqual("ltr"u8))
                {
                    scope.Direction = TextDirection.LeftToRight;
                }
                else if (text.SequenceEqual("rtl"u8))
                {
                    scope.Direction = TextDirection.RightToLeft;
                }
                else
                {
                    Fail(RdfXmlErrorKind.InvalidBaseDirection, "its:dir is \"ltr\" or \"rtl\".");
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>A scope attribute's value, copied out of the arena: it outlives the arena's reset.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.ScopeAttributesAreCopied), Scope = ExceptionScope.HotPath)]
    private byte[] CopyScope(TermSpan value) => Arena.Bytes(default, value).ToArray();

    /// <summary><c>rdf:version</c>: anything but "1.0" or "1.1" announces RDF 1.2 or later (RDF 1.2 XML §3.1).</summary>
    private void TakeVersion()
    {
        ReadOnlySpan<byte> text = Arena.Bytes(default, Value());
        _scopes[_scopeCount - 1].Rdf12 = !text.SequenceEqual("1.1"u8) && !text.SequenceEqual("1.0"u8);
    }

    /// <summary>An attribute name beginning "xml", in any case, is XML's own and is ignored where it has no namespace (RDF 1.1 XML Syntax §6.1.4).</summary>
    private static bool IsReservedXmlName(string local) =>
        local.AsSpan().StartsWith("xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Consumes the current element's content through its end tag, emitting nothing.</summary>
    private void SkipSubtree()
    {
        if (_reader.IsEmptyElement)
        {
            return;
        }

        int depth = 0;

        while (_reader.Read())
        {
            if (_reader.NodeType == XmlNodeType.Element && !_reader.IsEmptyElement)
            {
                depth++;
            }
            else if (_reader.NodeType == XmlNodeType.EndElement)
            {
                if (depth == 0)
                {
                    return;
                }

                depth--;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside an ignored element.");
    }

    [DesignDecision(typeof(HotPathScope.DirectivesAreNotPerQuad), Scope = ExceptionScope.HotPath)]
    private void ReportNamespace()
    {
        // "xmlns" alone declares the default namespace: its local name is the
        // reader's "xmlns" and its prefix is empty, and the reported prefix is
        // then empty too.
        string prefix = _reader.Prefix.Length == 0 ? string.Empty : _reader.LocalName;
        _onNamespace!(Encoding.UTF8.GetBytes(prefix), Encoding.UTF8.GetBytes(_reader.Value));
    }

    // ---------------------------------------------------------- values

    /// <summary>The current attribute's or text node's value, as UTF-8 in the arena.</summary>
    private TermSpan Value()
    {
        int pending = 0;
        bool nonWhitespace = false;
        AppendValue(ref pending, ref nonWhitespace);
        return Arena.CommitScratch(pending);
    }

    /// <summary>
    /// Appends the current node's value to the scratch run of
    /// <paramref name="pending"/> uncommitted bytes, in chunks through one
    /// char buffer, so that no string is made for a value.
    /// </summary>
    private void AppendValue(ref int pending, ref bool sawNonWhitespace)
    {
        if (!_reader.CanReadValueChunk)
        {
            ReadOnlySpan<char> value = _reader.Value.AsSpan();
            sawNonWhitespace |= !IsXmlWhitespace(value);
            AppendUtf16(value, ref pending);
            return;
        }

        int carried = 0;
        int read;

        while ((read = _reader.ReadValueChunk(_chars, carried, _chars.Length - carried)) > 0)
        {
            int available = carried + read;
            sawNonWhitespace |= !IsXmlWhitespace(_chars.AsSpan(carried, read));
            carried = AppendUtf16Chunk(_chars.AsSpan(0, available), final: false, ref pending);

            if (carried > 0)
            {
                _chars[0] = _chars[available - 1];
            }
        }

        if (carried > 0)
        {
            AppendUtf16Chunk(_chars.AsSpan(0, carried), final: true, ref pending);
        }
    }

    private void AppendUtf16(ReadOnlySpan<char> text, ref int pending) => AppendUtf16Chunk(text, final: true, ref pending);

    /// <summary>
    /// Transcodes into the scratch run; returns how many trailing chars were
    /// not consumed (a high surrogate awaiting its pair), at most one.
    /// </summary>
    private int AppendUtf16Chunk(ReadOnlySpan<char> text, bool final, ref int pending)
    {
        Span<byte> destination = Arena.ReserveScratch(pending + (text.Length * 3))[pending..];
        OperationStatus status = Utf8.FromUtf16(text, destination, out int read, out int written, replaceInvalidSequences: true, isFinalBlock: final);
        pending += written;

        return status == OperationStatus.NeedMoreData ? text.Length - read : 0;
    }

    /// <summary>
    /// Whether the current text node is whitespace only, read in chunks and
    /// consumed: the caller wants no value from it.
    /// </summary>
    private bool ConsumeIsWhitespace()
    {
        if (_reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
        {
            return true;
        }

        if (!_reader.CanReadValueChunk)
        {
            return IsXmlWhitespace(_reader.Value.AsSpan());
        }

        bool whitespace = true;
        int read;

        while ((read = _reader.ReadValueChunk(_chars, 0, _chars.Length)) > 0)
        {
            whitespace &= IsXmlWhitespace(_chars.AsSpan(0, read));
        }

        return whitespace;
    }

    private static bool IsXmlWhitespace(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (c is not (' ' or '\t' or '\r' or '\n'))
            {
                return false;
            }
        }

        return true;
    }

    private void RequireWhitespaceText()
    {
        if (!ConsumeIsWhitespace())
        {
            Fail(RdfXmlErrorKind.UnexpectedText, "Text where only elements may stand.");
        }
    }

    private void SkipWhitespaceToEndElement()
    {
        while (_reader.Read())
        {
            switch (_reader.NodeType)
            {
                case XmlNodeType.EndElement:
                    return;

                case XmlNodeType.Element:
                    Fail(RdfXmlErrorKind.UnexpectedContent, "A property element holds one node element; a second was found.");
                    return;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    RequireWhitespaceText();
                    continue;

                default:
                    continue;
            }
        }

        Fail(RdfXmlErrorKind.UnexpectedEnd, "The document ended inside a property element.");
    }

    private void RequireNoObjectAttributes(TermSpan resource, TermSpan nodeId, TermSpan datatype, int attributeCount, string form)
    {
        if (resource.IsPresent || nodeId.IsPresent || datatype.IsPresent || attributeCount > 0)
        {
            Fail(RdfXmlErrorKind.ForbiddenAttribute, "rdf:resource, rdf:nodeID, rdf:datatype and property attributes are not allowed on ", form, ".");
        }
    }

    private ParseType ParseTypeOf(TermSpan value)
    {
        ReadOnlySpan<byte> text = Arena.Bytes(default, value);

        if (text.SequenceEqual("Resource"u8))
        {
            return ParseType.Resource;
        }

        if (text.SequenceEqual("Collection"u8))
        {
            return ParseType.Collection;
        }

        if (text.SequenceEqual("Triple"u8))
        {
            return ParseType.Triple;
        }

        // "Literal", and any other value, which [6.2.20] reads as Literal.
        return ParseType.Literal;
    }

    // ---------------------------------------------------------- names

    private bool IsRdf(string local) =>
        string.Equals(_reader.NamespaceURI, RdfNs, StringComparison.Ordinal) && string.Equals(_reader.LocalName, local, StringComparison.Ordinal);

    private bool IsRdfAttribute(string local) => IsRdf(local);

    /// <summary>§6.2.5 <c>nodeElementIRIs</c>: anything but the core syntax terms, <c>rdf:li</c> and the withdrawn terms.</summary>
    private static bool IsForbiddenNodeElementName(string local) =>
        IsCoreSyntaxTerm(local) || string.Equals(local, "li", StringComparison.Ordinal) || IsOldTerm(local);

    /// <summary>§6.2.6 <c>propertyElementIRIs</c>: anything but the core syntax terms, <c>rdf:Description</c> and the withdrawn terms.</summary>
    private static bool IsForbiddenPropertyElementName(string local) =>
        IsCoreSyntaxTerm(local) || string.Equals(local, "Description", StringComparison.Ordinal) || IsOldTerm(local);

    /// <summary>§6.2.7 <c>propertyAttributeIRIs</c>: anything but the core syntax terms, <c>rdf:Description</c>, <c>rdf:li</c> and the withdrawn terms.</summary>
    private static bool IsForbiddenPropertyAttributeName(string local) =>
        IsCoreSyntaxTerm(local) || string.Equals(local, "Description", StringComparison.Ordinal)
            || string.Equals(local, "li", StringComparison.Ordinal) || IsOldTerm(local);

    private static bool IsCoreSyntaxTerm(string local) => local switch
    {
        "RDF" or "ID" or "about" or "parseType" or "resource" or "nodeID" or "datatype" or "version" => true,
        _ => false,
    };

    private static bool IsOldTerm(string local) => local switch
    {
        "aboutEach" or "aboutEachPrefix" or "bagID" => true,
        _ => false,
    };

    // ---------------------------------------------------------- growth

    [DesignDecision(typeof(RdfTermRepresentation.ViewsNestByArena), Scope = ExceptionScope.HotPath)]
    private void GrowScopes() => Array.Resize(ref _scopes, _scopes.Length * 2);

    [DesignDecision(typeof(RdfTermRepresentation.ViewsNestByArena), Scope = ExceptionScope.HotPath)]
    private void GrowCaptured() => Array.Resize(ref _captured, _captured.Length * 2);

    [DesignDecision(typeof(RdfTermRepresentation.ViewsNestByArena), Scope = ExceptionScope.HotPath)]
    private void GrowAttributes() => Array.Resize(ref _attributes, _attributes.Length * 2);

    // ---------------------------------------------------------- errors

    /// <summary>Ends the parse with an error at the reader's position.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.PositionsAreTheXmlReaders), Scope = ExceptionScope.HotPath)]
    internal void Fail(RdfXmlErrorKind kind, string message)
    {
        bool known = _lines is not null && _lines.HasLineInfo();
        throw new StopException(new RdfXmlParseError(kind, known ? _lines!.LineNumber : 0, known ? _lines!.LinePosition : 0, message));
    }

    /// <summary>Ends the parse with an error whose message names an XML name.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.PositionsAreTheXmlReaders), Scope = ExceptionScope.HotPath)]
    private void Fail(RdfXmlErrorKind kind, string before, string name, string after) => Fail(kind, before + name + after);

    /// <summary>Ends the parse with an error whose message quotes a term's text.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.PositionsAreTheXmlReaders), Scope = ExceptionScope.HotPath)]
    private void Fail(RdfXmlErrorKind kind, string message, ReadOnlySpan<byte> term) => Fail(kind, message + "'" + Encoding.UTF8.GetString(term) + "'.");

    /// <summary>Ends the parse with an error whose message ends in a count.</summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.PositionsAreTheXmlReaders), Scope = ExceptionScope.HotPath)]
    private void Fail(RdfXmlErrorKind kind, string message, int count) => Fail(kind, message + count.ToString(CultureInfo.InvariantCulture) + ".");

    /// <summary>The arena every term of the current top-level node element lives in; the canonicaliser commits its text into it too.</summary>
    internal TermArena Arena { get; } = new();

    /// <summary>Transcodes UTF-16 text into an uncommitted scratch run, for the canonicaliser.</summary>
    internal void AppendText(ReadOnlySpan<char> text, ref int pending) => AppendUtf16(text, ref pending);
}
