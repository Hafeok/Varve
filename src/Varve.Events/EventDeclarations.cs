// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Events.Model;

namespace Varve.Events;

/// <summary>
/// Reads declarations out of catalogue graphs (docs/spec/events.md §3.2). A
/// declaration that is not well formed is left out of the set and reported,
/// with the reason the validator would refuse it for.
/// </summary>
[Contract(typeof(TheEventCatalogue.DeclarationVocabularyInVarve), Role = "the reader of the declaration vocabulary")]
public static partial class EventDeclarations
{
    /// <summary>Every catalogue graph of <paramref name="source"/> that declares a type, read whole.</summary>
    public static DeclarationSet ReadAll(IQuadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Read(source, CatalogueGraphs(source));
    }

    /// <summary>The named catalogue graphs of <paramref name="source"/>, read whole.</summary>
    public static DeclarationSet Read(IQuadSource source, IEnumerable<RdfTerm> catalogueGraphs)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogueGraphs);
        List<EventTypeDeclaration> types = [];
        List<DeclarationProblem> problems = [];
        SortedSet<RdfTerm> graphs = new(catalogueGraphs, TermOrder.Instance);

        foreach (RdfTerm graph in graphs)
        {
            ReadGraph(graph, Triples(source, graph), types, problems);
        }

        types.Sort(static (a, b) =>
        {
            int byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : TermOrder.Instance.Compare(a.Node, b.Node);
        });

        return new DeclarationSet(types, problems);
    }

    /// <summary>The catalogue graphs <paramref name="source"/> holds a <c>varve:EventType</c> in.</summary>
    public static IReadOnlyList<RdfTerm> CatalogueGraphs(IQuadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SortedSet<RdfTerm> graphs = new(TermOrder.Instance);

        if (!source.TryInternalise(Vocabulary.Type, out TermHandle type) || !source.TryInternalise(Vocabulary.EventType, out TermHandle eventType))
        {
            return [];
        }

        HashSet<TermHandle> seen = new(source.TermComparer);
        using IQuadCursor cursor = source.Match(TermHandle.None, type, eventType, GraphPattern.AnyNamed);

        while (cursor.MoveNext())
        {
            TermHandle handle = cursor.Current.Graph;

            if (seen.Add(handle) && source.TryExternalise(handle, out RdfTerm? graph) && EventCatalogue.IsUnderNamespace(graph))
            {
                graphs.Add(graph);
            }
        }

        return [.. graphs];
    }

    internal static List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> Triples(IQuadSource source, RdfTerm graph)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> triples = [];

        if (!source.TryInternalise(graph, out TermHandle handle))
        {
            return triples;
        }

        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(handle));

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;

            if (source.TryExternalise(quad.Subject, out RdfTerm? s)
                && source.TryExternalise(quad.Predicate, out RdfTerm? p)
                && source.TryExternalise(quad.Object, out RdfTerm? o))
            {
                triples.Add((s, p, o));
            }
        }

        return triples;
    }

    /// <summary>Reads one catalogue graph's triples into declarations and problems.</summary>
    internal static void ReadGraph(
        RdfTerm graph,
        IReadOnlyList<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> triples,
        List<EventTypeDeclaration> types,
        List<DeclarationProblem> problems)
    {
        if (triples.Count == 0)
        {
            return;
        }

        if (!EventCatalogue.TryGetSourceGraph(graph, out RdfTerm? sourceGraph))
        {
            problems.Add(new(graph, Reasons.Graph, "the graph is under the catalogue namespace and is no source graph's catalogue"));
            return;
        }

        foreach ((RdfTerm s, RdfTerm _, RdfTerm o) in triples)
        {
            if (s.Kind == RdfTermKind.BlankNode || o.Kind == RdfTermKind.BlankNode)
            {
                problems.Add(new(graph, Reasons.BlankNode, "a catalogue graph holds no blank node: every type and field node is an IRI"));
                return;
            }
        }

        // Group by subject, in a stable order.
        SortedDictionary<RdfTerm, List<(RdfTerm Predicate, RdfTerm Object)>> nodes = new(TermOrder.Instance);

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            if (!nodes.TryGetValue(s, out List<(RdfTerm, RdfTerm)>? list))
            {
                nodes[s] = list = [];
            }

            list.Add((p, o));
        }

        HashSet<RdfTerm> typeNodes = new(RdfTerm.Comparer);
        HashSet<RdfTerm> fieldNodes = new(RdfTerm.Comparer);

        foreach ((RdfTerm node, List<(RdfTerm Predicate, RdfTerm Object)> properties) in nodes)
        {
            bool isType = false, isField = false;

            foreach ((RdfTerm p, RdfTerm o) in properties)
            {
                if (IsIri(p, Vocabulary.TypeIri))
                {
                    isType |= IsIri(o, Vocabulary.EventTypeIri);
                    isField |= IsIri(o, Vocabulary.EventFieldIri);
                }
            }

            if (isType && isField)
            {
                problems.Add(new(node, Reasons.Term, "a node is a type and a field at once"));
            }
            else if (isType)
            {
                typeNodes.Add(node);
            }
            else if (isField)
            {
                fieldNodes.Add(node);
            }
            else
            {
                problems.Add(new(node, Reasons.Term, "a catalogue graph holds only type and field nodes"));
            }
        }

        // Which type each field node belongs to.
        Dictionary<RdfTerm, RdfTerm> owner = new(RdfTerm.Comparer);
        HashSet<RdfTerm> badTypes = new(RdfTerm.Comparer);

        foreach (RdfTerm typeNode in typeNodes)
        {
            foreach ((RdfTerm p, RdfTerm o) in nodes[typeNode])
            {
                if (!IsIri(p, Vocabulary.EventFieldPropertyIri))
                {
                    continue;
                }

                if (!fieldNodes.Contains(o))
                {
                    problems.Add(new(typeNode, Reasons.Field, "varve:eventField names a node that is no varve:EventField in this graph"));
                    badTypes.Add(typeNode);
                }
                else if (owner.TryGetValue(o, out RdfTerm? other) && !other.Equals(typeNode))
                {
                    problems.Add(new(o, Reasons.Field, "a field node belongs to two types"));
                    badTypes.Add(typeNode);
                    badTypes.Add(other);
                }
                else
                {
                    owner[o] = typeNode;
                }
            }
        }

        foreach (RdfTerm fieldNode in fieldNodes)
        {
            if (!owner.ContainsKey(fieldNode))
            {
                problems.Add(new(fieldNode, Reasons.Field, "a field node belongs to no type"));
            }
        }

        Dictionary<RdfTerm, RdfTerm> classes = new(RdfTerm.Comparer);

        foreach (RdfTerm typeNode in typeNodes)
        {
            if (badTypes.Contains(typeNode))
            {
                continue;
            }

            EventTypeDeclaration? declaration = ReadType(typeNode, sourceGraph, nodes, owner, problems);

            if (declaration is null)
            {
                continue;
            }

            if (classes.TryGetValue(declaration.Class, out RdfTerm? first))
            {
                problems.Add(new(typeNode, Reasons.ClassTaken, "the class is declared by another type of this source graph"));
                continue;
            }

            classes[declaration.Class] = typeNode;
            types.Add(declaration);
        }
    }

    private static EventTypeDeclaration? ReadType(
        RdfTerm typeNode,
        RdfTerm? sourceGraph,
        SortedDictionary<RdfTerm, List<(RdfTerm Predicate, RdfTerm Object)>> nodes,
        Dictionary<RdfTerm, RdfTerm> owner,
        List<DeclarationProblem> problems)
    {
        if (typeNode.Kind != RdfTermKind.Iri)
        {
            problems.Add(new(typeNode, Reasons.BlankNode, "a type node is an IRI"));
            return null;
        }

        Properties properties = new(nodes[typeNode]);
        string? error = properties.Unknown(
            Vocabulary.TypeIri, Vocabulary.EventNameIri, Vocabulary.EventClassIri, Vocabulary.EventVersionIri,
            Vocabulary.EventSelectIri, Vocabulary.EventSubjectIri, Vocabulary.EventRetiredIri, Vocabulary.EventFieldPropertyIri,
            Vocabulary.CommentIri);

        string? name = null, select = null, subject = null, description = null;
        RdfTerm? @class = null;
        int version = 0;
        bool retired = false;

        error ??= properties.String(Vocabulary.EventNameIri, required: true, out name)
            ?? (NamePattern().IsMatch(name!) ? null : "varve:eventName is not a short name ([A-Za-z][A-Za-z0-9._-]{0,127})")
            ?? properties.Iri(Vocabulary.EventClassIri, out @class)
            ?? properties.Version(out version)
            ?? properties.String(Vocabulary.EventSelectIri, required: true, out select)
            ?? properties.String(Vocabulary.EventSubjectIri, required: false, out subject)
            ?? properties.String(Vocabulary.CommentIri, required: false, out description)
            ?? properties.Boolean(Vocabulary.EventRetiredIri, required: false, out retired);

        if (error is not null)
        {
            problems.Add(new(typeNode, Reasons.Term, error));
            return null;
        }

        List<EventFieldDeclaration> fields = [];
        HashSet<string> fieldNames = new(StringComparer.Ordinal);

        foreach ((RdfTerm fieldNode, RdfTerm fieldOwner) in owner)
        {
            if (!fieldOwner.Equals(typeNode))
            {
                continue;
            }

            EventFieldDeclaration? field = ReadField(fieldNode, nodes[fieldNode], problems);

            if (field is null)
            {
                return null;
            }

            if (!fieldNames.Add(field.Name))
            {
                problems.Add(new(typeNode, Reasons.Field, "the field name " + field.Name + " is given twice"));
                return null;
            }

            fields.Add(field);
        }

        fields.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        if (subject is not null && fieldNames.Contains(subject))
        {
            problems.Add(new(typeNode, Reasons.Term, "the subject variable is also a field"));
            return null;
        }

        if (subject is not null && !FieldNamePattern().IsMatch(subject))
        {
            problems.Add(new(typeNode, Reasons.Term, "varve:eventSubject is not a variable name"));
            return null;
        }

        HashSet<string> projected = new(fieldNames, StringComparer.Ordinal);

        if (subject is not null)
        {
            projected.Add(subject);
        }

        if (!CompiledSelect.TryCompile(select!, projected, out CompiledSelect? compiled, out string? reason))
        {
            problems.Add(new(typeNode, Reasons.Select, reason!));
            return null;
        }

        return new EventTypeDeclaration(typeNode, sourceGraph, name!, @class!, version, select!, subject, description, retired, fields, compiled!);
    }

    private static EventFieldDeclaration? ReadField(RdfTerm fieldNode, List<(RdfTerm Predicate, RdfTerm Object)> triples, List<DeclarationProblem> problems)
    {
        if (fieldNode.Kind != RdfTermKind.Iri)
        {
            problems.Add(new(fieldNode, Reasons.BlankNode, "a field node is an IRI"));
            return null;
        }

        Properties properties = new(triples);
        string? error = properties.Unknown(
            Vocabulary.TypeIri, Vocabulary.FieldNameIri, Vocabulary.FieldDatatypeIri, Vocabulary.FieldRequiredIri,
            Vocabulary.FieldAsIri, Vocabulary.FieldKeepLanguageIri, Vocabulary.CommentIri);

        string? name = null, @as = null, description = null;
        RdfTerm? datatype = null;
        bool required = false, keepLanguage = false;

        error ??= properties.String(Vocabulary.FieldNameIri, required: true, out name)
            ?? (FieldNamePattern().IsMatch(name!) ? null : "varve:fieldName is not a field name ([A-Za-z_][A-Za-z0-9_]{0,63})")
            ?? properties.Iri(Vocabulary.FieldDatatypeIri, out datatype)
            ?? properties.Boolean(Vocabulary.FieldRequiredIri, required: true, out required)
            ?? properties.String(Vocabulary.FieldAsIri, required: false, out @as)
            ?? (@as is null or "string" ? null : "varve:fieldAs is \"string\" or absent")
            ?? properties.Boolean(Vocabulary.FieldKeepLanguageIri, required: false, out keepLanguage)
            ?? properties.String(Vocabulary.CommentIri, required: false, out description);

        if (error is not null)
        {
            problems.Add(new(fieldNode, Reasons.Term, error));
            return null;
        }

        if (name == CompiledSelect.EventVariable.Name)
        {
            problems.Add(new(fieldNode, Reasons.Field, "a field is not named event, which is the event node's variable"));
            return null;
        }

        return new EventFieldDeclaration(fieldNode, name!, datatype!, required, @as is not null, keepLanguage, description);
    }

    private static bool IsIri(RdfTerm term, string iri) =>
        term.Kind == RdfTermKind.Iri && term.Lexical.SequenceEqual(Encoding.UTF8.GetBytes(iri));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    internal static partial Regex FieldNamePattern();

    /// <summary>A node's properties, read with the cardinalities and kinds of §3.2.</summary>
    private sealed class Properties(List<(RdfTerm Predicate, RdfTerm Object)> triples)
    {
        internal string? Unknown(params string[] known)
        {
            foreach ((RdfTerm p, RdfTerm o) in triples)
            {
                string predicate = Encoding.UTF8.GetString(p.Lexical);

                if (predicate.StartsWith(Vocabulary.Varve, StringComparison.Ordinal) && Array.IndexOf(known, predicate) < 0)
                {
                    return "an unknown term " + predicate;
                }

                if (predicate == Vocabulary.TypeIri && !(IsIri(o, Vocabulary.EventTypeIri) || IsIri(o, Vocabulary.EventFieldIri)))
                {
                    return "a type or field node has no other rdf:type";
                }
            }

            return null;
        }

        internal string? String(string predicate, bool required, out string? value)
        {
            value = null;
            RdfTerm? term = One(predicate, out string? error);

            if (error is not null)
            {
                return error;
            }

            if (term is null)
            {
                return required ? "missing " + Short(predicate) : null;
            }

            if (term.Kind != RdfTermKind.Literal || !term.Language.IsEmpty || !(term.DatatypeIri.IsEmpty || term.DatatypeIri.SequenceEqual(Encoding.UTF8.GetBytes(Vocabulary.StringIri))))
            {
                return Short(predicate) + " is an xsd:string";
            }

            value = Encoding.UTF8.GetString(term.Lexical);
            return null;
        }

        internal string? Iri(string predicate, out RdfTerm? value)
        {
            value = One(predicate, out string? error);

            if (error is not null)
            {
                return error;
            }

            if (value is null)
            {
                return "missing " + Short(predicate);
            }

            return value.Kind == RdfTermKind.Iri ? null : Short(predicate) + " is an IRI";
        }

        internal string? Boolean(string predicate, bool required, out bool value)
        {
            value = false;
            RdfTerm? term = One(predicate, out string? error);

            if (error is not null)
            {
                return error;
            }

            if (term is null)
            {
                return required ? "missing " + Short(predicate) : null;
            }

            if (term.Kind == RdfTermKind.Literal && term.DatatypeIri.SequenceEqual(Encoding.UTF8.GetBytes(Vocabulary.BooleanIri)))
            {
                string lexical = Encoding.UTF8.GetString(term.Lexical);

                if (lexical is "true" or "1")
                {
                    value = true;
                    return null;
                }

                if (lexical is "false" or "0")
                {
                    return null;
                }
            }

            return Short(predicate) + " is an xsd:boolean";
        }

        internal string? Version(out int value)
        {
            value = 0;
            RdfTerm? term = One(Vocabulary.EventVersionIri, out string? error);

            if (error is not null)
            {
                return error;
            }

            if (term is null)
            {
                return "missing varve:eventVersion";
            }

            if (term.Kind == RdfTermKind.Literal
                && term.DatatypeIri.SequenceEqual(Encoding.UTF8.GetBytes(Vocabulary.IntegerIri))
                && int.TryParse(Encoding.UTF8.GetString(term.Lexical), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
                && value >= 1)
            {
                return null;
            }

            value = 0;
            return "varve:eventVersion is an xsd:integer of at least 1";
        }

        private RdfTerm? One(string predicate, out string? error)
        {
            error = null;
            RdfTerm? found = null;

            foreach ((RdfTerm p, RdfTerm o) in triples)
            {
                if (!IsIri(p, predicate))
                {
                    continue;
                }

                if (found is not null)
                {
                    error = Short(predicate) + " is given twice";
                    return null;
                }

                found = o;
            }

            return found;
        }

        private static string Short(string iri) =>
            iri.StartsWith(Vocabulary.Varve, StringComparison.Ordinal) ? "varve:" + iri[Vocabulary.Varve.Length..]
            : iri == Vocabulary.CommentIri ? "rdfs:comment" : iri;
    }
}

/// <summary>The reasons of docs/spec/events.md §7.1, as the problem's <c>reason</c> member spells them.</summary>
internal static class Reasons
{
    internal const string Graph = "graph";
    internal const string BlankNode = "blank-node";
    internal const string Term = "term";
    internal const string Select = "select";
    internal const string Field = "field";
    internal const string ClassTaken = "class-taken";
    internal const string NameTaken = "name-taken";
    internal const string Removed = "removed";
    internal const string Version = "version";
}

/// <summary>A total order of terms: kind, then the UTF-8 bytes of the lexical form, datatype and language.</summary>
internal sealed class TermOrder : IComparer<RdfTerm>
{
    internal static readonly TermOrder Instance = new();

    public int Compare(RdfTerm? x, RdfTerm? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int c = x.Kind.CompareTo(y.Kind);

        if (c != 0)
        {
            return c;
        }

        c = x.Lexical.SequenceCompareTo(y.Lexical);

        if (c != 0 || x.Kind != RdfTermKind.Literal)
        {
            return c;
        }

        c = x.DatatypeIri.SequenceCompareTo(y.DatatypeIri);
        return c != 0 ? c : x.Language.SequenceCompareTo(y.Language);
    }
}
