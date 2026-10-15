// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Json.Canonical;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Events.Model;

namespace Varve.Events;

/// <summary>
/// Renders the events of a commit (ADR 0143): for each event node, the
/// declaration's <c>SELECT</c> over the node's closure as of the commit, bound
/// to the node, mapped to JSON, and written as a canonical CloudEvent; an event
/// that cannot be rendered becomes the problem, in its place. A pure function
/// of the position, the declarations and the state at the position.
/// </summary>
[Contract(typeof(DomainEventsAsCloudevents.RenderedFromTheClosureAsOf), Role = "the rendering of a commit's events")]
public sealed class EventRenderer
{
    /// <summary>The problem type of an event that cannot be rendered.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string ProblemType = "https://w3id.org/varve/problems/event-not-rendered";

    /// <summary>The problem's title, as the protocol's problem catalogue states it.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string ProblemTitle = "An event could not be rendered";

    private readonly SparqlEvaluator _evaluator;

    /// <summary>A renderer; each declaration's <c>SELECT</c> runs over one event's closure, so the evaluator's defaults bound it.</summary>
    public EventRenderer() => _evaluator = new SparqlEvaluator();

    /// <summary>
    /// The events of <paramref name="commit"/> in order, rendered with the
    /// declarations of <paramref name="catalogue"/> (the catalogue as of the
    /// commit) over <paramref name="asOf"/> (the dataset as of the commit).
    /// </summary>
    public IReadOnlyList<RenderedEvent> Render(
        EventCommit commit, EventCatalogueSnapshot catalogue, IQuadSource asOf, EventEndpoints endpoints, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(asOf);
        ArgumentNullException.ThrowIfNull(endpoints);

        List<Occurrence> occurrences = Occurrences(commit, catalogue);
        List<RenderedEvent> events = new(occurrences.Count);

        foreach (Occurrence occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int ordinal = events.Count;
            events.Add(occurrence.Problem is { } problem
                ? Problem(commit, ordinal, occurrence.Node, occurrence.Types[0].Declaration.Name, problem, endpoints)
                : Render(commit, ordinal, occurrence.Node, occurrence.Types[0], asOf, endpoints, cancellationToken));
        }

        return events;
    }

    /// <summary>Whether a commit's assertions hold any event under a catalogue: the cheap test before an as-of read is opened.</summary>
    public static bool HasEvents(EventCommit commit, EventCatalogueSnapshot catalogue)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(catalogue);

        foreach (TypeAssertion assertion in commit.Assertions)
        {
            foreach (CataloguedType type in catalogue.OfClass(assertion.Graph, assertion.Class))
            {
                if (!type.Declaration.Retired)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly record struct Occurrence(RdfTerm Node, List<CataloguedType> Types, string? Problem, int Order);

    /// <summary>The events of a commit, in §4.1's order: IRIs in code-point order, then blank nodes in the delta's order; the types of one node by short name.</summary>
    private static List<Occurrence> Occurrences(EventCommit commit, EventCatalogueSnapshot catalogue)
    {
        Dictionary<RdfTerm, (List<CataloguedType> Types, int Order)> byNode = new(RdfTerm.Comparer);
        int order = 0;

        foreach (TypeAssertion assertion in commit.Assertions)
        {
            foreach (CataloguedType type in catalogue.OfClass(assertion.Graph, assertion.Class))
            {
                if (type.Declaration.Retired)
                {
                    continue;
                }

                if (!byNode.TryGetValue(assertion.Node, out (List<CataloguedType> Types, int Order) entry))
                {
                    entry = ([], order++);
                    byNode[assertion.Node] = entry;
                }

                if (!entry.Types.Contains(type))
                {
                    entry.Types.Add(type);
                }
            }
        }

        List<Occurrence> occurrences = [];

        foreach ((RdfTerm node, (List<CataloguedType> types, int nodeOrder)) in byNode)
        {
            types.Sort(static (a, b) => string.CompareOrdinal(a.Declaration.Name, b.Declaration.Name));
            string? problem = node.Kind != RdfTermKind.Iri ? Problems.BlankEvent : types.Count > 1 ? Problems.Types : null;
            occurrences.Add(new Occurrence(node, types, problem, nodeOrder));
        }

        occurrences.Sort(static (a, b) =>
        {
            bool aIri = a.Node.Kind == RdfTermKind.Iri, bIri = b.Node.Kind == RdfTermKind.Iri;

            if (aIri != bIri)
            {
                return aIri ? -1 : 1;
            }

            return aIri ? a.Node.Lexical.SequenceCompareTo(b.Node.Lexical) : a.Order.CompareTo(b.Order);
        });

        return occurrences;
    }

    private RenderedEvent Render(
        EventCommit commit, int ordinal, RdfTerm node, CataloguedType type, IQuadSource asOf, EventEndpoints endpoints, CancellationToken cancellationToken)
    {
        EventTypeDeclaration declaration = type.Declaration;
        InMemoryDataset closure = Closure(asOf, node, declaration.SourceGraph);
        SelectQuery query = declaration.Compiled.BindEvent(node);
        Dictionary<string, RdfTerm> row = new(StringComparer.Ordinal);
        int rows = 0;

        using (QueryResults results = _evaluator.Evaluate(query, closure, cancellationToken))
        {
            SolutionResults solutions = (SolutionResults)results;
            IReadOnlyList<Variable> variables = solutions.Variables;

            while (solutions.MoveNext())
            {
                if (++rows > 1)
                {
                    break;
                }

                for (int i = 0; i < variables.Count; i++)
                {
                    if (solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term) && term is not null)
                    {
                        row[variables[i].Name] = term;
                    }
                }
            }
        }

        if (rows != 1)
        {
            return Problem(commit, ordinal, node, declaration.Name, rows == 0 ? Problems.NoRow : Problems.Rows, endpoints);
        }

        ArrayBufferWriter<byte> data = new();
        CanonicalJsonWriter dataWriter = new(data);
        dataWriter.StartObject();

        foreach (EventFieldDeclaration field in EventSchemas.Sorted(declaration.Fields))
        {
            if (!row.TryGetValue(field.Name, out RdfTerm? value))
            {
                if (field.Required)
                {
                    return Problem(commit, ordinal, node, declaration.Name, Problems.UnboundRequired, endpoints);
                }

                continue;
            }

            // A value that fails leaves the writer mid-member; the event is
            // abandoned with it, so nothing half-written escapes.
            dataWriter.WriteName(field.Name);

            if (ValueMapping.TryWrite(dataWriter, field, value) is { } reason)
            {
                return Problem(commit, ordinal, node, declaration.Name, reason, endpoints);
            }
        }

        dataWriter.EndObject();

        string? subject = null;

        if (declaration.Subject is not null && row.TryGetValue(declaration.Subject, out RdfTerm? subjectTerm))
        {
            if (subjectTerm.Kind is RdfTermKind.BlankNode or RdfTermKind.TripleTerm)
            {
                return Problem(commit, ordinal, node, declaration.Name, Problems.Datatype, endpoints);
            }

            subject = Encoding.UTF8.GetString(subjectTerm.Lexical);
        }

        SchemaRevision revision = type.Current;
        string id = Id(commit.Position, node);
        ArrayBufferWriter<byte> output = new();
        CanonicalJsonWriter writer = new(output);
        writer.StartObject();
        writer.WriteName("data"u8);
        writer.WriteCanonicalValue(data.WrittenSpan);
        writer.WriteName("datacontenttype"u8);
        writer.WriteString("application/json"u8);
        writer.WriteName("dataschema"u8);
        writer.WriteString(endpoints.Schema(declaration.Name, revision.Version, revision.Revision));
        writer.WriteName("id"u8);
        writer.WriteString(id);
        writer.WriteName("sequence"u8);
        writer.WriteString(Sequence(commit.Position, ordinal));
        writer.WriteName("source"u8);
        writer.WriteString(endpoints.Source);
        writer.WriteName("specversion"u8);
        writer.WriteString("1.0"u8);

        if (subject is not null)
        {
            writer.WriteName("subject"u8);
            writer.WriteString(subject);
        }

        writer.WriteName("time"u8);
        writer.WriteString(commit.Timestamp.ToString());
        writer.WriteName("type"u8);
        writer.WriteString(declaration.Name);
        writer.EndObject();
        return new RenderedEvent(commit.Position, ordinal, id, declaration.Name, isProblem: false, output.WrittenSpan.ToArray());
    }

    private static RenderedEvent Problem(EventCommit commit, int ordinal, RdfTerm node, string eventType, string reason, EventEndpoints endpoints)
    {
        string id = Id(commit.Position, node);
        ArrayBufferWriter<byte> output = new();
        CanonicalJsonWriter writer = new(output);
        writer.StartObject();
        writer.WriteName("data"u8);
        writer.StartObject();
        writer.WriteName("event"u8);
        writer.WriteString(node.Kind == RdfTermKind.Iri ? Encoding.UTF8.GetString(node.Lexical) : "_:");
        writer.WriteName("eventType"u8);
        writer.WriteString(eventType);
        writer.WriteName("reason"u8);
        writer.WriteString(reason);
        writer.WriteName("status"u8);
        writer.WriteNumber(422);
        writer.WriteName("title"u8);
        writer.WriteString(ProblemTitle);
        writer.WriteName("type"u8);
        writer.WriteString(ProblemType);
        writer.EndObject();
        writer.WriteName("datacontenttype"u8);
        writer.WriteString("application/problem+json"u8);
        writer.WriteName("id"u8);
        writer.WriteString(id);
        writer.WriteName("sequence"u8);
        writer.WriteString(Sequence(commit.Position, ordinal));
        writer.WriteName("source"u8);
        writer.WriteString(endpoints.Source);
        writer.WriteName("specversion"u8);
        writer.WriteString("1.0"u8);
        writer.WriteName("time"u8);
        writer.WriteString(commit.Timestamp.ToString());
        writer.WriteName("type"u8);
        writer.WriteString(ProblemType);
        writer.EndObject();
        return new RenderedEvent(commit.Position, ordinal, id, ProblemType, isProblem: true, output.WrittenSpan.ToArray());
    }

    private static string Id(FeedPosition position, RdfTerm node) =>
        position.Value.ToString(CultureInfo.InvariantCulture) + " " + (node.Kind == RdfTermKind.Iri ? Encoding.UTF8.GetString(node.Lexical) : "_:");

    /// <summary>The <c>sequence</c> extension: the position padded to 20 digits, a dot, the ordinal padded to 6, so string order is event order.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    internal static string Sequence(FeedPosition position, int ordinal) =>
        position.Value.ToString("D20", CultureInfo.InvariantCulture) + "." + ordinal.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>The closure of <paramref name="node"/> in <paramref name="graph"/>: its quads, and those of every blank node reached from them, in the default graph of a dataset of their own.</summary>
    internal static InMemoryDataset Closure(IQuadSource source, RdfTerm node, RdfTerm? graph)
    {
        InMemoryDatasetBuilder builder = new();
        GraphPattern pattern;

        if (graph is null)
        {
            pattern = GraphPattern.DefaultGraph;
        }
        else if (source.TryInternalise(graph, out TermHandle graphHandle))
        {
            pattern = GraphPattern.Named(graphHandle);
        }
        else
        {
            return builder.ToDataset();
        }

        Queue<TermHandle> pending = new();
        HashSet<TermHandle> visited = new(source.TermComparer);

        if (source.TryInternalise(node, out TermHandle start))
        {
            pending.Enqueue(start);
            visited.Add(start);
        }

        while (pending.Count > 0)
        {
            TermHandle subject = pending.Dequeue();
            using IQuadCursor cursor = source.Match(subject, TermHandle.None, TermHandle.None, pattern);

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;

                if (!source.TryExternalise(quad.Subject, out RdfTerm? s)
                    || !source.TryExternalise(quad.Predicate, out RdfTerm? p)
                    || !source.TryExternalise(quad.Object, out RdfTerm? o))
                {
                    continue;
                }

                builder.Add(s, p, o);

                if (o.Kind == RdfTermKind.BlankNode && visited.Add(quad.Object))
                {
                    pending.Enqueue(quad.Object);
                }
            }
        }

        return builder.ToDataset();
    }
}
