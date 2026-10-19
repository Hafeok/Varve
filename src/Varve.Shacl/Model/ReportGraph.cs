// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Shacl.Shapes;
using Varve.Xsd;

namespace Varve.Shacl.Model;

/// <summary>
/// The report as RDF (ADR 0154). Its own blank nodes — the report, the
/// results, path structures, and shapes graph nodes that are not data — are
/// labelled by RDFC-1.0 with every data term held fixed; a data blank node
/// keeps its source's label; the two kinds of label never collide.
/// </summary>
internal sealed class ReportGraph
{
    private const string FixedPrefix = "urn:x-varve-report-fixed:";

    private readonly List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> _triples;

    private ReportGraph(List<(RdfTerm, RdfTerm, RdfTerm)> triples) => _triples = triples;

    internal static ReportGraph Build(ValidationReport report, ImmutableArray<ShapePath?> paths, bool shapesAreData)
    {
        // 1. The graph with provisional labels for the report's own nodes.
        List<(RdfTerm, RdfTerm, RdfTerm)> draft = [];
        HashSet<RdfTerm> own = new(ReferenceEqualityComparer.Instance);
        RdfTerm reportNode = Own("report", own);
        draft.Add((reportNode, Vocabulary.RdfType, Vocabulary.ValidationReportClass));
        draft.Add((reportNode, Vocabulary.Conforms, report.Conforms ? Vocabulary.True : Vocabulary.False));
        foreach (RdfTerm severity in report.ConformanceDisallows ?? [])
        {
            draft.Add((reportNode, Vocabulary.ConformanceDisallows, severity));
        }

        foreach (RdfTerm imported in report.ImportsNotFollowed)
        {
            draft.Add((reportNode, Vocabulary.ImportNotFollowed, imported));
        }

        int pathBlank = 0;
        for (int i = 0; i < report.Results.Count; i++)
        {
            ShaclResult result = report.Results[i];
            RdfTerm node = Own("r" + i.ToString(CultureInfo.InvariantCulture), own);
            draft.Add((reportNode, Vocabulary.Result, node));
            draft.Add((node, Vocabulary.RdfType, Vocabulary.ValidationResultClass));
            draft.Add((node, Vocabulary.FocusNode, result.FocusNode));
            if (paths[i] is { } path)
            {
                // Each result has its own copy of a complex path (§6.8.2.2: a deep copy).
                List<(RdfTerm, RdfTerm, RdfTerm)> structure = [];
                RdfTerm root = path.WriteRdf(structure, ref pathBlank);
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in structure)
                {
                    draft.Add((PathNode(s, own), p, PathNode(o, own)));
                }

                draft.Add((node, Vocabulary.ResultPath, PathNode(root, own)));
            }

            if (result.Value is { } value)
            {
                draft.Add((node, Vocabulary.Value, value));
            }

            draft.Add((node, Vocabulary.SourceShape, ShapesNode(result.SourceShape, shapesAreData, own)));
            if (result.SourceConstraint is { } constraint)
            {
                draft.Add((node, Vocabulary.SourceConstraint, ShapesNode(constraint, shapesAreData, own)));
            }

            draft.Add((node, Vocabulary.SourceConstraintComponent, result.SourceConstraintComponent));
            draft.Add((node, Vocabulary.ResultSeverity, result.Severity));
            foreach (RdfTerm message in result.Messages)
            {
                draft.Add((node, Vocabulary.ResultMessage, message));
            }
        }

        // 2. Every term that mentions a data blank node is held fixed, as an IRI no report contains.
        SortedSet<string> fixedTexts = new(CodePointComparer.Instance);
        Dictionary<string, RdfTerm> fixedTerms = new(StringComparer.Ordinal);
        HashSet<string> dataLabels = new(StringComparer.Ordinal);
        foreach ((RdfTerm s, RdfTerm _, RdfTerm o) in draft)
        {
            foreach (RdfTerm term in (ReadOnlySpan<RdfTerm>)[s, o])
            {
                if (!own.Contains(term) && MentionsBlank(term, dataLabels))
                {
                    string text = Text(term);
                    fixedTexts.Add(text);
                    fixedTerms[text] = term;
                }
            }
        }

        string prefix = FixedPrefix;
        while (draft.Exists(t => StartsWith(t.Item1, prefix) || StartsWith(t.Item3, prefix)))
        {
            prefix = "urn:x-" + prefix[4..];
        }

        Dictionary<RdfTerm, RdfTerm> skolem = new(RdfTerm.Comparer);
        int n = 0;
        foreach (string text in fixedTexts)
        {
            skolem[fixedTerms[text]] = RdfTerm.Iri(Encoding.UTF8.GetBytes(prefix + (n++).ToString(CultureInfo.InvariantCulture)));
        }

        // 3. RDFC-1.0 over the report with the fixed terms in place.
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in draft)
        {
            builder.Add(skolem.GetValueOrDefault(s, s), p, skolem.GetValueOrDefault(o, o));
        }

        CanonicalDataset canonical = RdfCanonicaliser.Canonicalise(builder.ToDataset(), report.Canonicalisation);

        // 4. The report's labels, disjoint from every data label.
        string labelPrefix = "c14n";
        while (StartsWithAny(dataLabels, labelPrefix))
        {
            labelPrefix = "r" + labelPrefix;
        }

        Dictionary<RdfTerm, RdfTerm> relabel = new(ReferenceEqualityComparer.Instance);
        foreach (RdfTerm term in own)
        {
            string label = Encoding.UTF8.GetString(term.Lexical);
            string issued = canonical.IssuedIdentifiers.TryGetValue(label, out string? c) ? c : label;
            relabel[term] = RdfTerm.BlankNode(Encoding.UTF8.GetBytes(labelPrefix + issued["c14n".Length..]));
        }

        List<(RdfTerm, RdfTerm, RdfTerm)> triples = new(draft.Count);
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in draft)
        {
            triples.Add((relabel.GetValueOrDefault(s, s), p, relabel.GetValueOrDefault(o, o)));
        }

        return new ReportGraph(triples);
    }

    internal InMemoryDataset ToDataset()
    {
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in _triples)
        {
            builder.Add(s, p, o);
        }

        return builder.ToDataset();
    }

    internal byte[] ToNQuads()
    {
        SortedSet<string> lines = new(CodePointComparer.Instance);
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in _triples)
        {
            lines.Add(Text(s) + " " + Text(p) + " " + Text(o) + " .\n");
        }

        StringBuilder text = new();
        foreach (string line in lines)
        {
            text.Append(line);
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>A term in RDF 1.2's canonical N-Triples form (ADR 0061).</summary>
    internal static string Text(RdfTerm term)
    {
        StringBuilder text = new();
        Write(text, term);
        return text.ToString();
    }

    private static void Write(StringBuilder text, RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                text.Append('<').Append(Encoding.UTF8.GetString(term.Lexical)).Append('>');
                return;
            case RdfTermKind.BlankNode:
                text.Append("_:").Append(Encoding.UTF8.GetString(term.Lexical));
                return;
            case RdfTermKind.TripleTerm:
                text.Append("<<( ");
                Write(text, term.Subject!);
                text.Append(' ');
                Write(text, term.Predicate!);
                text.Append(' ');
                Write(text, term.Object!);
                text.Append(" )>>");
                return;
        }

        text.Append('"');
        foreach (char c in Encoding.UTF8.GetString(term.Lexical))
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
        if (!term.Language.IsEmpty)
        {
            text.Append('@').Append(Encoding.UTF8.GetString(term.Language).ToLowerInvariant());
            if (term.Direction != TextDirection.None)
            {
                text.Append(term.Direction == TextDirection.LeftToRight ? "--ltr" : "--rtl");
            }
        }
        else if (!term.DatatypeIri.SequenceEqual(Vocabulary.XsdString.Lexical))
        {
            text.Append("^^<").Append(Encoding.UTF8.GetString(term.DatatypeIri)).Append('>');
        }
    }

    private static RdfTerm Own(string label, HashSet<RdfTerm> own)
    {
        RdfTerm node = RdfTerm.BlankNode(Encoding.UTF8.GetBytes(label));
        own.Add(node);
        return node;
    }

    /// <summary>A path structure's blank node, which is the report's own.</summary>
    private static RdfTerm PathNode(RdfTerm term, HashSet<RdfTerm> own) =>
        term.Kind == RdfTermKind.BlankNode ? Own("path." + Encoding.UTF8.GetString(term.Lexical), own) : term;

    /// <summary>A shapes graph node: the report's own when it is a blank node of a shapes graph that is not the data; otherwise as it is.</summary>
    private static RdfTerm ShapesNode(RdfTerm term, bool shapesAreData, HashSet<RdfTerm> own) =>
        term.Kind == RdfTermKind.BlankNode && !shapesAreData ? Own("shape." + Encoding.UTF8.GetString(term.Lexical), own) : term;

    private static bool MentionsBlank(RdfTerm term, HashSet<string> labels)
    {
        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                labels.Add(Encoding.UTF8.GetString(term.Lexical));
                return true;
            case RdfTermKind.TripleTerm:
                bool s = MentionsBlank(term.Subject!, labels), p = MentionsBlank(term.Predicate!, labels), o = MentionsBlank(term.Object!, labels);
                return s || p || o;
            default:
                return false;
        }
    }

    private static bool StartsWith(RdfTerm term, string prefix) =>
        term.Kind == RdfTermKind.Iri && Encoding.UTF8.GetString(term.Lexical).StartsWith(prefix, StringComparison.Ordinal);

    private static bool StartsWithAny(HashSet<string> labels, string prefix)
    {
        foreach (string label in labels)
        {
            if (label.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Strings in code point order, which is the order of their UTF-8 bytes.</summary>
    private sealed class CodePointComparer : IComparer<string>
    {
        internal static readonly CodePointComparer Instance = new();

        public int Compare(string? x, string? y) => XsdString.CompareCodePoints(x.AsSpan(), y.AsSpan());
    }
}
