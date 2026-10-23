// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Events;
using Varve.Events.Model;
using Varve.Management;
using Varve.Protocol.Model;
using Varve.Rdf;

namespace Varve.Protocol;

/// <summary>
/// A dataset's event declarations in their two homes (ADR 0167): the catalogue
/// graphs <c>varve:events-catalogue/{enc(G)}</c> of the dataset, and the
/// dataset's graph of the system dataset, where each type node says
/// <c>varve:sourceGraph G</c> instead of the catalogue graph's name.
/// <list type="bullet">
/// <item><see cref="ToCatalogue"/> puts a dataset graph's event nodes in the
/// catalogue graphs' form: a type node and its triples in its source graph's
/// catalogue graph, without <c>varve:sourceGraph</c>; a field node in the
/// catalogue graph of the types that name it.</item>
/// <item><see cref="TryFromCatalogue"/> is the copy: the other way, and only
/// when the way back gives the same triples.</item>
/// <item><see cref="Changes"/> turns two states into the change form a
/// <see cref="EventCatalogueHistory"/> is fed and <see cref="EventDeclarationCheck"/> checks.</item>
/// </list>
/// The rest of a dataset's graph (its node, its grants, its bindings) is no
/// event declaration and is left out.
/// </summary>
public static class ManagedEventDeclarations
{
    private static readonly RdfTerm RdfType = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
    private static readonly RdfTerm EventType = Iri(ManagementVocabulary.Namespace + "EventType");
    private static readonly RdfTerm EventField = Iri(ManagementVocabulary.Namespace + "EventField");
    private static readonly RdfTerm EventFieldProperty = Iri(ManagementVocabulary.Namespace + "eventField");

    /// <summary>
    /// The event nodes of a dataset's graph, as assertions into the catalogue
    /// graphs, in a stable order. Each reason the graph cannot be put in that
    /// form is added to <paramref name="problems"/>: a type node without
    /// exactly one source graph, a field node named by no type, or by types in
    /// two catalogue graphs.
    /// </summary>
    public static List<FeedChange> ToCatalogue(IEnumerable<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> datasetGraph, ICollection<DeclarationProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(datasetGraph);
        ArgumentNullException.ThrowIfNull(problems);
        Dictionary<RdfTerm, List<(RdfTerm Predicate, RdfTerm Object)>> subjects = new(RdfTerm.Comparer);

        foreach ((RdfTerm subject, RdfTerm predicate, RdfTerm @object) in datasetGraph)
        {
            if (!subjects.TryGetValue(subject, out List<(RdfTerm, RdfTerm)>? pairs))
            {
                subjects[subject] = pairs = [];
            }

            pairs.Add((predicate, @object));
        }

        // Each type node's catalogue graph, from its one source graph.
        Dictionary<RdfTerm, RdfTerm> placed = new(RdfTerm.Comparer);
        List<RdfTerm> types = [];

        foreach ((RdfTerm subject, List<(RdfTerm Predicate, RdfTerm Object)> pairs) in subjects)
        {
            if (!pairs.Exists(p => p.Predicate.Equals(RdfType) && p.Object.Equals(EventType)))
            {
                continue;
            }

            types.Add(subject);
            List<RdfTerm> sources = pairs.FindAll(p => p.Predicate.Equals(ManagementVocabulary.SourceGraphProperty)).ConvertAll(p => p.Object);

            if (sources.Count != 1 || !TryCatalogueGraph(sources[0], out RdfTerm? graph))
            {
                problems.Add(new DeclarationProblem(subject, "graph", "a moved event type names exactly one varve:sourceGraph, an absolute IRI or varve:defaultGraph"));
                continue;
            }

            placed[subject] = graph;
        }

        // Each field node goes where the types that name it are.
        foreach ((RdfTerm subject, List<(RdfTerm Predicate, RdfTerm Object)> pairs) in subjects)
        {
            if (placed.ContainsKey(subject) || !pairs.Exists(p => p.Predicate.Equals(RdfType) && p.Object.Equals(EventField)))
            {
                continue;
            }

            RdfTerm? graph = null;
            bool split = false;

            foreach (RdfTerm type in types)
            {
                if (placed.TryGetValue(type, out RdfTerm? typeGraph) && subjects[type].Exists(p => p.Predicate.Equals(EventFieldProperty) && p.Object.Equals(subject)))
                {
                    split |= graph is not null && !graph.Equals(typeGraph);
                    graph ??= typeGraph;
                }
            }

            if (graph is null || split)
            {
                problems.Add(new DeclarationProblem(subject, "field", split
                    ? "a field node is named by event types of two source graphs; give each its own field node"
                    : "a field node is named by no event type of the dataset"));
                continue;
            }

            placed[subject] = graph;
        }

        List<FeedChange> changes = [];

        foreach ((RdfTerm subject, RdfTerm graph) in placed)
        {
            foreach ((RdfTerm predicate, RdfTerm @object) in subjects[subject])
            {
                if (!predicate.Equals(ManagementVocabulary.SourceGraphProperty))
                {
                    changes.Add(new FeedChange(FeedChangeKind.Assert, subject, predicate, @object, graph));
                }
            }
        }

        changes.Sort(Order);
        return changes;
    }

    /// <summary>
    /// The copy (ADR 0167): the catalogue graphs' triples, given as assertions
    /// with their graphs, as nodes of a dataset's graph, each type node with
    /// <c>varve:sourceGraph</c>. False, with the reason, when a graph under the
    /// catalogue namespace is no catalogue graph, a node is a subject in two
    /// catalogue graphs, or the nodes do not translate back to the same
    /// triples; then the move cannot copy the catalogue one-to-one.
    /// </summary>
    public static bool TryFromCatalogue(
        IReadOnlyList<FeedChange> catalogue, out List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> nodes, out DeclarationProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        nodes = [];
        problem = null;
        Dictionary<RdfTerm, RdfTerm> graphOf = new(RdfTerm.Comparer);
        HashSet<(RdfTerm, RdfTerm, RdfTerm)> seen = new(TripleComparer.Instance);

        foreach (FeedChange change in catalogue)
        {
            if (change.Graph is not { } graph || !EventCatalogue.TryGetSourceGraph(graph, out RdfTerm? source))
            {
                problem = new DeclarationProblem(change.Graph ?? change.Subject, "graph", "the graph is under the catalogue namespace and catalogues no graph");
                return false;
            }

            if (graphOf.TryGetValue(change.Subject, out RdfTerm? other) && !other.Equals(graph))
            {
                problem = new DeclarationProblem(change.Subject, "graph", "the node is a subject in two catalogue graphs; a moved node has one source graph");
                return false;
            }

            graphOf[change.Subject] = graph;

            if (seen.Add((change.Subject, change.Predicate, change.Object)))
            {
                nodes.Add((change.Subject, change.Predicate, change.Object));
            }

            if (change.Predicate.Equals(RdfType) && change.Object.Equals(EventType))
            {
                RdfTerm stated = source ?? ManagementVocabulary.DefaultGraphTerm;

                if (seen.Add((change.Subject, ManagementVocabulary.SourceGraphProperty, stated)))
                {
                    nodes.Add((change.Subject, ManagementVocabulary.SourceGraphProperty, stated));
                }
            }
        }

        List<DeclarationProblem> problems = [];
        List<FeedChange> back = ToCatalogue(nodes, problems);
        HashSet<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)> original = new(QuadComparer.Instance);

        foreach (FeedChange change in catalogue)
        {
            original.Add((change.Subject, change.Predicate, change.Object, change.Graph!));
        }

        if (problems.Count > 0 || back.Count != original.Count || !back.TrueForAll(c => original.Contains((c.Subject, c.Predicate, c.Object, c.Graph!))))
        {
            problem = problems.Count > 0 ? problems[0] : new DeclarationProblem(catalogue[0].Subject, "graph", "the catalogue graphs do not translate one-to-one into the dataset's graph");
            nodes = [];
            return false;
        }

        nodes.Sort(static (a, b) => Order(new FeedChange(FeedChangeKind.Assert, a.Subject, a.Predicate, a.Object, null), new FeedChange(FeedChangeKind.Assert, b.Subject, b.Predicate, b.Object, null)));
        return true;
    }

    /// <summary>The changes from the catalogue-form state <paramref name="before"/> to <paramref name="after"/>: retractions, then assertions.</summary>
    public static List<FeedChange> Changes(IReadOnlyList<FeedChange> before, IReadOnlyList<FeedChange> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        HashSet<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)> then = new(QuadComparer.Instance);
        HashSet<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)> now = new(QuadComparer.Instance);

        foreach (FeedChange change in before)
        {
            then.Add((change.Subject, change.Predicate, change.Object, change.Graph!));
        }

        foreach (FeedChange change in after)
        {
            now.Add((change.Subject, change.Predicate, change.Object, change.Graph!));
        }

        List<FeedChange> changes = [];

        foreach (FeedChange change in before)
        {
            if (!now.Contains((change.Subject, change.Predicate, change.Object, change.Graph!)))
            {
                changes.Add(change with { Kind = FeedChangeKind.Retract });
            }
        }

        foreach (FeedChange change in after)
        {
            if (!then.Contains((change.Subject, change.Predicate, change.Object, change.Graph!)))
            {
                changes.Add(change with { Kind = FeedChangeKind.Assert });
            }
        }

        return changes;
    }

    private static bool TryCatalogueGraph(RdfTerm source, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RdfTerm? graph)
    {
        graph = null;

        if (source.Equals(ManagementVocabulary.DefaultGraphTerm))
        {
            graph = EventCatalogue.GraphOf(null);
            return true;
        }

        if (source.Kind != RdfTermKind.Iri || ManagementVocabulary.TryGetDataset(source, out _) || !Varve.Iri.IriRef.IsAbsolute(source.Lexical))
        {
            return false;
        }

        graph = EventCatalogue.GraphOf(source);
        return true;
    }

    private static int Order(FeedChange a, FeedChange b)
    {
        int c = Compare(a.Graph, b.Graph);
        c = c != 0 ? c : Compare(a.Subject, b.Subject);
        c = c != 0 ? c : Compare(a.Predicate, b.Predicate);
        return c != 0 ? c : Compare(a.Object, b.Object);
    }

    // A total order of terms: by kind, lexical form, datatype and language.
    private static int Compare(RdfTerm? x, RdfTerm? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        int c = x.Kind.CompareTo(y.Kind);
        c = c != 0 ? c : x.Lexical.SequenceCompareTo(y.Lexical);
        c = c != 0 || x.Kind != RdfTermKind.Literal ? c : x.DatatypeIri.SequenceCompareTo(y.DatatypeIri);
        c = c != 0 || x.Kind != RdfTermKind.Literal ? c : x.Language.SequenceCompareTo(y.Language);

        if (c == 0 && x.Kind == RdfTermKind.TripleTerm)
        {
            c = Compare(x.Subject, y.Subject);
            c = c != 0 ? c : Compare(x.Predicate, y.Predicate);
            c = c != 0 ? c : Compare(x.Object, y.Object);
        }

        return c;
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    private sealed class TripleComparer : IEqualityComparer<(RdfTerm, RdfTerm, RdfTerm)>
    {
        internal static readonly TripleComparer Instance = new();

        public bool Equals((RdfTerm, RdfTerm, RdfTerm) x, (RdfTerm, RdfTerm, RdfTerm) y) =>
            RdfTerm.Comparer.Equals(x.Item1, y.Item1) && RdfTerm.Comparer.Equals(x.Item2, y.Item2) && RdfTerm.Comparer.Equals(x.Item3, y.Item3);

        public int GetHashCode((RdfTerm, RdfTerm, RdfTerm) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Item1), RdfTerm.Comparer.GetHashCode(obj.Item2), RdfTerm.Comparer.GetHashCode(obj.Item3));
    }

    private sealed class QuadComparer : IEqualityComparer<(RdfTerm, RdfTerm, RdfTerm, RdfTerm)>
    {
        internal static readonly QuadComparer Instance = new();

        public bool Equals((RdfTerm, RdfTerm, RdfTerm, RdfTerm) x, (RdfTerm, RdfTerm, RdfTerm, RdfTerm) y) =>
            RdfTerm.Comparer.Equals(x.Item1, y.Item1) && RdfTerm.Comparer.Equals(x.Item2, y.Item2)
            && RdfTerm.Comparer.Equals(x.Item3, y.Item3) && RdfTerm.Comparer.Equals(x.Item4, y.Item4);

        public int GetHashCode((RdfTerm, RdfTerm, RdfTerm, RdfTerm) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Item1), RdfTerm.Comparer.GetHashCode(obj.Item2), RdfTerm.Comparer.GetHashCode(obj.Item3), RdfTerm.Comparer.GetHashCode(obj.Item4));
    }
}

/// <summary>
/// The move of a dataset's event declarations, as a host drives it without a
/// request (ADR 0167 point 3): an instance that writes both the dataset and
/// the system dataset finishes a move it finds <c>varve:Moving</c> when it
/// converges, with no caller's token.
/// </summary>
public static class EventDeclarationMove
{
    /// <summary>Finishes the move of <paramref name="dataset"/>'s event declarations; true when they are moved after it.</summary>
    public static Task<bool> ResumeAsync(ProtocolOptions options, IManagementPlane plane, DatasetName dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plane);
        return Endpoints.EventMoves.ResumeAsync(options, plane, dataset, cancellationToken);
    }
}
