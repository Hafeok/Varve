// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Xml;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.RdfXml;

/// <summary>
/// The lexical form of an <c>rdf:XMLLiteral</c>: the content of an
/// <c>rdf:parseType="Literal"</c> element in Exclusive XML Canonicalization
/// 1.0 with comments, as RDF 1.1 XML Syntax §7.2.17 requires.
/// </summary>
/// <remarks>
/// <para>
/// Written from the reader's events rather than from a DOM: an element
/// start is its name, the namespace declarations it <em>visibly uses</em>
/// that no enclosing element of the literal has already rendered — sorted
/// by prefix, the default namespace first — and its attributes sorted by
/// namespace name and then local name; an empty element is a start and an
/// end tag; text, attribute values, comments and processing instructions are
/// escaped as the canonicalization says.
/// </para>
/// <para>
/// This is the one place the reader allocates in proportion to its input:
/// the lists that sort an element's attributes and track rendered
/// namespaces, per XML literal. An XML literal is rare, and the allocation
/// is stated (<c>rdf-xml.md</c> §7) rather than hidden.
/// </para>
/// </remarks>
internal static class CanonicalXml
{
    private readonly record struct Attribute(string Prefix, string Local, string Namespace, string Value);

    /// <summary>
    /// Reads the content of the element the reader is on, to and including
    /// its end tag, and commits the canonical text to the arena.
    /// </summary>
    [DesignDecision(typeof(RdfXmlOverSystemXml.XmlLiteralsAreCanonicalised), Scope = ExceptionScope.HotPath)]
    internal static TermSpan Read(XmlReader reader, RdfXmlReaderCore core)
    {
        int pending = 0;

        if (reader.IsEmptyElement)
        {
            return core.Arena.CommitScratch(0);
        }

        List<(string Prefix, string Namespace)> rendered = [];
        Stack<int> marks = new();
        List<Attribute> attributes = [];
        List<(string Prefix, string Namespace)> declarations = [];
        int depth = 0;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    StartElement(reader, core, ref pending, rendered, marks, attributes, declarations);

                    if (reader.IsEmptyElement)
                    {
                        EndElement(reader, core, ref pending, rendered, marks);
                    }
                    else
                    {
                        depth++;
                    }

                    continue;

                case XmlNodeType.EndElement:
                    if (depth == 0)
                    {
                        return core.Arena.CommitScratch(pending);
                    }

                    depth--;
                    EndElement(reader, core, ref pending, rendered, marks);
                    continue;

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    Text(reader.Value, core, ref pending);
                    continue;

                case XmlNodeType.Comment:
                    core.AppendText("<!--".AsSpan(), ref pending);
                    core.AppendText(reader.Value.AsSpan(), ref pending);
                    core.AppendText("-->".AsSpan(), ref pending);
                    continue;

                case XmlNodeType.ProcessingInstruction:
                    core.AppendText("<?".AsSpan(), ref pending);
                    core.AppendText(reader.Name.AsSpan(), ref pending);

                    if (reader.Value.Length > 0)
                    {
                        core.AppendText(" ".AsSpan(), ref pending);
                        core.AppendText(reader.Value.AsSpan(), ref pending);
                    }

                    core.AppendText("?>".AsSpan(), ref pending);
                    continue;

                default:
                    continue;
            }
        }

        core.Fail(Model.RdfXmlErrorKind.UnexpectedEnd, "The document ended inside an XML literal.");
        return TermSpan.None;
    }

    private static void StartElement(
        XmlReader reader,
        RdfXmlReaderCore core,
        ref int pending,
        List<(string Prefix, string Namespace)> rendered,
        Stack<int> marks,
        List<Attribute> attributes,
        List<(string Prefix, string Namespace)> declarations)
    {
        string prefix = reader.Prefix;
        string local = reader.LocalName;
        attributes.Clear();
        declarations.Clear();

        // The element's own prefix is visibly used; so is each attribute's.
        AddVisible(reader, prefix, rendered, declarations);

        for (bool more = reader.MoveToFirstAttribute(); more; more = reader.MoveToNextAttribute())
        {
            if (string.Equals(reader.NamespaceURI, "http://www.w3.org/2000/xmlns/", StringComparison.Ordinal))
            {
                continue;
            }

            if (reader.Prefix.Length > 0 && !string.Equals(reader.Prefix, "xml", StringComparison.Ordinal))
            {
                AddVisible(reader, reader.Prefix, rendered, declarations);
            }

            attributes.Add(new Attribute(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value));
        }

        reader.MoveToElement();

        declarations.Sort(static (a, b) => string.CompareOrdinal(a.Prefix, b.Prefix));
        attributes.Sort(static (a, b) =>
        {
            int byNamespace = string.CompareOrdinal(a.Namespace, b.Namespace);
            return byNamespace != 0 ? byNamespace : string.CompareOrdinal(a.Local, b.Local);
        });

        core.AppendText("<".AsSpan(), ref pending);
        QName(prefix, local, core, ref pending);

        foreach ((string declaredPrefix, string ns) in declarations)
        {
            core.AppendText(declaredPrefix.Length == 0 ? " xmlns=\"".AsSpan() : " xmlns:".AsSpan(), ref pending);

            if (declaredPrefix.Length > 0)
            {
                core.AppendText(declaredPrefix.AsSpan(), ref pending);
                core.AppendText("=\"".AsSpan(), ref pending);
            }

            AttributeValue(ns, core, ref pending);
            core.AppendText("\"".AsSpan(), ref pending);
        }

        foreach (Attribute attribute in attributes)
        {
            core.AppendText(" ".AsSpan(), ref pending);
            QName(attribute.Prefix, attribute.Local, core, ref pending);
            core.AppendText("=\"".AsSpan(), ref pending);
            AttributeValue(attribute.Value, core, ref pending);
            core.AppendText("\"".AsSpan(), ref pending);
        }

        core.AppendText(">".AsSpan(), ref pending);

        marks.Push(rendered.Count);
        rendered.AddRange(declarations);
    }

    /// <summary>
    /// Adds the declaration a visibly used prefix needs, unless an enclosing
    /// element of the literal has rendered the same binding (exc-c14n §3).
    /// </summary>
    private static void AddVisible(
        XmlReader reader,
        string prefix,
        List<(string Prefix, string Namespace)> rendered,
        List<(string Prefix, string Namespace)> declarations)
    {
        string ns = reader.LookupNamespace(prefix) ?? string.Empty;

        for (int i = rendered.Count - 1; i >= 0; i--)
        {
            if (string.Equals(rendered[i].Prefix, prefix, StringComparison.Ordinal))
            {
                if (string.Equals(rendered[i].Namespace, ns, StringComparison.Ordinal))
                {
                    return;
                }

                break;
            }
        }

        if (prefix.Length == 0 && ns.Length == 0)
        {
            // No default namespace, and no enclosing element rendered one:
            // nothing to undeclare.
            bool anyDefaultRendered = false;

            for (int i = rendered.Count - 1; i >= 0; i--)
            {
                if (rendered[i].Prefix.Length == 0)
                {
                    anyDefaultRendered = rendered[i].Namespace.Length > 0;
                    break;
                }
            }

            if (!anyDefaultRendered)
            {
                return;
            }
        }

        foreach ((string declaredPrefix, _) in declarations)
        {
            if (string.Equals(declaredPrefix, prefix, StringComparison.Ordinal))
            {
                return;
            }
        }

        declarations.Add((prefix, ns));
    }

    private static void EndElement(
        XmlReader reader,
        RdfXmlReaderCore core,
        ref int pending,
        List<(string Prefix, string Namespace)> rendered,
        Stack<int> marks)
    {
        core.AppendText("</".AsSpan(), ref pending);
        QName(reader.Prefix, reader.LocalName, core, ref pending);
        core.AppendText(">".AsSpan(), ref pending);

        int mark = marks.Pop();
        rendered.RemoveRange(mark, rendered.Count - mark);
    }

    private static void QName(string prefix, string local, RdfXmlReaderCore core, ref int pending)
    {
        if (prefix.Length > 0)
        {
            core.AppendText(prefix.AsSpan(), ref pending);
            core.AppendText(":".AsSpan(), ref pending);
        }

        core.AppendText(local.AsSpan(), ref pending);
    }

    private static void Text(string text, RdfXmlReaderCore core, ref int pending)
    {
        int from = 0;

        for (int i = 0; i < text.Length; i++)
        {
            string? escape = text[i] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\r' => "&#xD;",
                _ => null,
            };

            if (escape is null)
            {
                continue;
            }

            core.AppendText(text.AsSpan(from, i - from), ref pending);
            core.AppendText(escape.AsSpan(), ref pending);
            from = i + 1;
        }

        core.AppendText(text.AsSpan(from), ref pending);
    }

    private static void AttributeValue(string text, RdfXmlReaderCore core, ref int pending)
    {
        int from = 0;

        for (int i = 0; i < text.Length; i++)
        {
            string? escape = text[i] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '"' => "&quot;",
                '\t' => "&#x9;",
                '\n' => "&#xA;",
                '\r' => "&#xD;",
                _ => null,
            };

            if (escape is null)
            {
                continue;
            }

            core.AppendText(text.AsSpan(from, i - from), ref pending);
            core.AppendText(escape.AsSpan(), ref pending);
            from = i + 1;
        }

        core.AppendText(text.AsSpan(from), ref pending);
    }
}
