// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Rdf;
using Varve.Events.Model;

namespace Varve.Events;

/// <summary>
/// The catalogue graphs' history, replayed from the commits that touch them
/// (ADR 0144, point 5): it answers the declarations, versions and revisions in
/// force at any position without an as-of read. A host feeds it every
/// commit's changes to graphs under the catalogue namespace, in position
/// order, and keeps it current as commits arrive.
/// </summary>
[Contract(typeof(TheEventSchemaRegistry.RevisionsAreImmutable), Role = "the replayed history the revisions are read from")]
public sealed class EventCatalogueHistory
{
    private readonly Dictionary<RdfTerm, HashSet<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)>> _graphs = new(RdfTerm.Comparer);
    private readonly Dictionary<RdfTerm, List<EventTypeDeclaration>> _declarations = new(RdfTerm.Comparer);
    private readonly Dictionary<string, List<(SchemaRevision Revision, byte[] Schema)>> _revisions = new(StringComparer.Ordinal);
    private readonly List<EventCatalogueSnapshot> _snapshots = [new EventCatalogueSnapshot(new FeedPosition(0), [])];
    private readonly object _gate = new();

    /// <summary>A history of the dataset's catalogue graphs, replayed from position 1: its revisions are dataset positions.</summary>
    public EventCatalogueHistory()
    {
    }

    private EventCatalogueHistory(EventCatalogueSnapshot last)
    {
        IsManagement = true;
        _snapshots = [new EventCatalogueSnapshot(new FeedPosition(0), last.Types)];

        foreach (CataloguedType type in last.Types)
        {
            List<(SchemaRevision Revision, byte[] Schema)> list = new(type.Revisions.Count);

            for (int i = 0; i < type.Revisions.Count; i++)
            {
                // Only the last revision's bytes are compared with what follows.
                list.Add((type.Revisions[i], i == type.Revisions.Count - 1 ? EventSchemas.Generate(type.Declaration, id: null) : []));
            }

            _revisions[type.Declaration.Name] = list;
        }
    }

    /// <summary>
    /// The history after a dataset's declarations moved to the management plane
    /// (ADR 0167), seeded with the revisions of <paramref name="last"/>, the
    /// catalogue at the move. It is fed management positions, from the system
    /// dataset's commits in the catalogue graphs' change form, and the
    /// revisions it mints are management positions (<c>m{p}</c>). A type whose
    /// generated schema has the same bytes as at the move keeps its revision,
    /// so its <c>dataschema</c> does not change. Its first snapshot, at
    /// position 0, is <paramref name="last"/>'s types.
    /// </summary>
    public static EventCatalogueHistory ContinueFrom(EventCatalogueSnapshot last)
    {
        ArgumentNullException.ThrowIfNull(last);
        return new EventCatalogueHistory(last);
    }

    /// <summary>Whether the history's positions are management positions: a history continued from a move.</summary>
    public bool IsManagement { get; }

    /// <summary>The last position applied.</summary>
    public FeedPosition Position { get; private set; }

    /// <summary>The catalogue at the last position applied.</summary>
    public EventCatalogueSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _snapshots[^1];
            }
        }
    }

    /// <summary>
    /// Applies the commit at <paramref name="position"/>: its changes to graphs
    /// under the catalogue namespace (others are ignored). Positions arrive in
    /// order; one at or before <see cref="Position"/> is ignored, so a host can
    /// replay a range twice.
    /// </summary>
    public void Apply(FeedPosition position, IEnumerable<FeedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        lock (_gate)
        {
            if (position <= Position)
            {
                return;
            }

            HashSet<RdfTerm> touched = new(RdfTerm.Comparer);

            foreach (FeedChange change in changes)
            {
                if (change.Graph is not { } graph || !EventCatalogue.IsUnderNamespace(graph))
                {
                    continue;
                }

                if (!_graphs.TryGetValue(graph, out HashSet<(RdfTerm, RdfTerm, RdfTerm)>? triples))
                {
                    _graphs[graph] = triples = new(TripleComparer.Instance);
                }

                (RdfTerm, RdfTerm, RdfTerm) triple = (change.Subject, change.Predicate, change.Object);
                _ = change.Kind == FeedChangeKind.Assert ? triples.Add(triple) : triples.Remove(triple);
                touched.Add(graph);
            }

            Position = position;

            if (touched.Count == 0)
            {
                return;
            }

            foreach (RdfTerm graph in touched)
            {
                List<EventTypeDeclaration> types = [];
                EventDeclarations.ReadGraph(graph, [.. _graphs[graph]], types, []);
                _declarations[graph] = types;
            }

            List<CataloguedType> catalogued = [];

            foreach ((RdfTerm graph, List<EventTypeDeclaration> types) in _declarations)
            {
                foreach (EventTypeDeclaration declaration in types)
                {
                    catalogued.Add(new CataloguedType(declaration, graph, Revise(position, declaration)));
                }
            }

            catalogued.Sort(static (a, b) =>
            {
                int byName = string.CompareOrdinal(a.Declaration.Name, b.Declaration.Name);
                return byName != 0 ? byName : TermOrder.Instance.Compare(a.Declaration.Node, b.Declaration.Node);
            });

            _snapshots.Add(new EventCatalogueSnapshot(position, catalogued));
        }
    }

    /// <summary>The catalogue at which <paramref name="revision"/> was minted, when it is of this history's space.</summary>
    public EventCatalogueSnapshot At(SchemaRevisionId revision) => At(new FeedPosition(revision.Position));

    /// <summary>The catalogue at <paramref name="position"/>: the last snapshot at or before it.</summary>
    public EventCatalogueSnapshot At(FeedPosition position)
    {
        lock (_gate)
        {
            int low = 0, high = _snapshots.Count - 1;

            while (low < high)
            {
                int middle = (low + high + 1) / 2;

                if (_snapshots[middle].Position <= position)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return _snapshots[low];
        }
    }

    /// <summary>The revisions of a type up to a position, as <see cref="Revise"/> recorded them.</summary>
    private SchemaRevision[] Revise(FeedPosition position, EventTypeDeclaration declaration)
    {
        if (!_revisions.TryGetValue(declaration.Name, out List<(SchemaRevision Revision, byte[] Schema)>? list))
        {
            _revisions[declaration.Name] = list = [];
        }

        byte[] schema = EventSchemas.Generate(declaration, id: null);

        if (list.Count == 0 || list[^1].Revision.Version != declaration.Version || !list[^1].Schema.AsSpan().SequenceEqual(schema))
        {
            // A revision is fixed once recorded: a later position appends.
            SchemaRevisionId minted = IsManagement ? SchemaRevisionId.Management(position.Value) : SchemaRevisionId.Dataset(position);

            if (list.Count == 0 || list[^1].Revision.Revision < minted)
            {
                list.Add((new SchemaRevision(declaration.Version, minted), schema));
            }
        }

        SchemaRevision[] revisions = new SchemaRevision[list.Count];

        for (int i = 0; i < list.Count; i++)
        {
            revisions[i] = list[i].Revision;
        }

        return revisions;
    }

    private sealed class TripleComparer : IEqualityComparer<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)>
    {
        internal static readonly TripleComparer Instance = new();

        public bool Equals((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) x, (RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) y) =>
            RdfTerm.Comparer.Equals(x.Subject, y.Subject) && RdfTerm.Comparer.Equals(x.Predicate, y.Predicate) && RdfTerm.Comparer.Equals(x.Object, y.Object);

        public int GetHashCode((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Subject), RdfTerm.Comparer.GetHashCode(obj.Predicate), RdfTerm.Comparer.GetHashCode(obj.Object));
    }
}
