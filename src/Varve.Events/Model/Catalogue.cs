// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Rdf;

namespace Varve.Events.Model;

/// <summary>One revision of an event type's schema: the version, and where the generated schema took this form.</summary>
[DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public readonly record struct SchemaRevision(int Version, SchemaRevisionId Revision);

/// <summary>
/// A revision's identifier (ADR 0145, amended by ADR 0167): a dataset
/// position, written as its bare integer, for a revision minted from the
/// dataset's catalogue graphs; a management position, written <c>m{p}</c>, for
/// one minted after the dataset's declarations moved to the management plane.
/// The two spaces never collide, and every revision of the dataset space sorts
/// before every revision of the management space, since a move is final.
/// </summary>
[DesignDecision(typeof(EventDeclarationsMoveToTheManagementPlane.RevisionsAfterTheMove), Scope = ExceptionScope.Boundary)]
public readonly record struct SchemaRevisionId : IComparable<SchemaRevisionId>
{
    private SchemaRevisionId(long position, bool management)
    {
        Position = position;
        IsManagement = management;
    }

    /// <summary>The position: a dataset position, or a management position when <see cref="IsManagement"/>.</summary>
    public long Position { get; }

    /// <summary>Whether the position is a management position: a revision minted after the move.</summary>
    public bool IsManagement { get; }

    /// <summary>A revision minted at a dataset position.</summary>
    public static SchemaRevisionId Dataset(FeedPosition position) => new(position.Value, management: false);

    /// <summary>A revision minted at a management position, after the move.</summary>
    public static SchemaRevisionId Management(long position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return new(position, management: true);
    }

    /// <summary>The identifier as a URL segment: <c>{p}</c>, or <c>m{p}</c> after the move.</summary>
    public override string ToString() =>
        (IsManagement ? "m" : string.Empty) + Position.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a URL segment: digits, or <c>m</c> then digits, with no sign, space or leading zero but a lone <c>0</c>.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static bool TryParse(string? text, out SchemaRevisionId revision)
    {
        revision = default;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        bool management = text[0] == 'm';
        ReadOnlySpan<char> digits = management ? text.AsSpan(1) : text.AsSpan();

        if (digits.Length == 0 || (digits.Length > 1 && digits[0] == '0')
            || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long position))
        {
            return false;
        }

        revision = new SchemaRevisionId(position, management);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SchemaRevisionId other) =>
        IsManagement != other.IsManagement ? (IsManagement ? 1 : -1) : Position.CompareTo(other.Position);

    /// <summary>Whether <paramref name="left"/> is minted before <paramref name="right"/>.</summary>
    public static bool operator <(SchemaRevisionId left, SchemaRevisionId right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is minted after <paramref name="right"/>.</summary>
    public static bool operator >(SchemaRevisionId left, SchemaRevisionId right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is minted no later than <paramref name="right"/>.</summary>
    public static bool operator <=(SchemaRevisionId left, SchemaRevisionId right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is minted no earlier than <paramref name="right"/>.</summary>
    public static bool operator >=(SchemaRevisionId left, SchemaRevisionId right) => left.CompareTo(right) >= 0;
}

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

    /// <summary>The same position with only the types <paramref name="predicate"/> keeps: a caller's view of the catalogue.</summary>
    public EventCatalogueSnapshot Where(Func<CataloguedType, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        List<CataloguedType> kept = [];

        foreach (CataloguedType type in Types)
        {
            if (predicate(type))
            {
                kept.Add(type);
            }
        }

        return new EventCatalogueSnapshot(Position, kept);
    }

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
