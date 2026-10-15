// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Rdf;

namespace Varve.Events.Model;

/// <summary>One revision of an event type's schema: the version, and the position at which the generated schema took this form.</summary>
[DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public readonly record struct SchemaRevision(int Version, FeedPosition Revision);

/// <summary>An event type as the catalogue holds it at one position: its declaration, the revision in force, and every revision before.</summary>
public sealed class CataloguedType
{
    internal CataloguedType(EventTypeDeclaration declaration, RdfTerm catalogueGraph, IReadOnlyList<SchemaRevision> revisions)
    {
        Declaration = declaration;
        CatalogueGraph = catalogueGraph;
        Revisions = revisions;
    }

    /// <summary>The declaration in force.</summary>
    public EventTypeDeclaration Declaration { get; }

    /// <summary>The catalogue graph that declares it, governed by its source graph.</summary>
    public RdfTerm CatalogueGraph { get; }

    /// <summary>Every revision of every version up to this position, oldest first; the last is in force.</summary>
    public IReadOnlyList<SchemaRevision> Revisions { get; }

    /// <summary>The revision in force.</summary>
    public SchemaRevision Current => Revisions[^1];
}

/// <summary>The catalogue at one position: the types declared then, with their revisions (docs/spec/events.md §6.2).</summary>
public sealed class EventCatalogueSnapshot
{
    private readonly Dictionary<string, CataloguedType> _byName;
    private readonly Dictionary<(RdfTerm? Graph, RdfTerm Class), List<CataloguedType>> _byClass;

    internal EventCatalogueSnapshot(FeedPosition position, IReadOnlyList<CataloguedType> types)
    {
        Position = position;
        Types = types;
        _byName = new(StringComparer.Ordinal);
        _byClass = new(GraphClassComparer.Instance);

        foreach (CataloguedType type in types)
        {
            _byName.TryAdd(type.Declaration.Name, type);
            (RdfTerm?, RdfTerm) key = (type.Declaration.SourceGraph, type.Declaration.Class);

            if (!_byClass.TryGetValue(key, out List<CataloguedType>? list))
            {
                _byClass[key] = list = [];
            }

            list.Add(type);
        }
    }

    /// <summary>The position the snapshot describes.</summary>
    public FeedPosition Position { get; }

    /// <summary>The types declared at the position, in short-name order.</summary>
    public IReadOnlyList<CataloguedType> Types { get; }

    /// <summary>The type of a short name, when one is declared.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool TryGet(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CataloguedType? type) =>
        _byName.TryGetValue(name, out type);

    internal IReadOnlyList<CataloguedType> OfClass(RdfTerm? graph, RdfTerm @class) =>
        _byClass.TryGetValue((graph, @class), out List<CataloguedType>? list) ? list : [];

    private sealed class GraphClassComparer : IEqualityComparer<(RdfTerm? Graph, RdfTerm Class)>
    {
        internal static readonly GraphClassComparer Instance = new();

        public bool Equals((RdfTerm? Graph, RdfTerm Class) x, (RdfTerm? Graph, RdfTerm Class) y) =>
            (x.Graph is null ? y.Graph is null : y.Graph is not null && RdfTerm.Comparer.Equals(x.Graph, y.Graph))
            && RdfTerm.Comparer.Equals(x.Class, y.Class);

        public int GetHashCode((RdfTerm? Graph, RdfTerm Class) obj) =>
            HashCode.Combine(obj.Graph is null ? 0 : RdfTerm.Comparer.GetHashCode(obj.Graph), RdfTerm.Comparer.GetHashCode(obj.Class));
    }
}
