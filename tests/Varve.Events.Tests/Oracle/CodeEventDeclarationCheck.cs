// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Delta.Model;
using Varve.Rdf;
using Varve.Events.Model;

namespace Varve.Events.Tests.Oracle;

/// <summary>
/// The check of a commit that changes catalogue graphs as it stood before
/// shapes became the runtime validator (ADR 0175): every problem decided by
/// <see cref="EventDeclarations"/>' code, frozen here as the oracle the shapes
/// and the residual check are held to. Not shipped.
/// </summary>
internal static class CodeEventDeclarationCheck
{
    /// <summary>
    /// Checks the changes <paramref name="changes"/> (a commit's changes to
    /// graphs under the catalogue namespace) against <paramref name="proposed"/>,
    /// the state the commit would produce. Changes to other graphs are ignored.
    /// </summary>
    internal static DeclarationVerdict Check(IQuadSource proposed, IReadOnlyList<FeedChange> changes)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(changes);

        Dictionary<RdfTerm, (List<FeedChange> Asserted, List<FeedChange> Retracted)> touched = new(RdfTerm.Comparer);

        foreach (FeedChange change in changes)
        {
            if (change.Graph is not { } graph || !EventCatalogue.IsUnderNamespace(graph))
            {
                continue;
            }

            if (!touched.TryGetValue(graph, out (List<FeedChange> Asserted, List<FeedChange> Retracted) lists))
            {
                touched[graph] = lists = ([], []);
            }

            (change.Kind == FeedChangeKind.Assert ? lists.Asserted : lists.Retracted).Add(change);
        }

        if (touched.Count == 0)
        {
            return DeclarationVerdict.Accept;
        }

        List<EventTypeDeclaration> before = [], after = [];
        List<DeclarationProblem> beforeProblems = [], afterProblems = [];

        List<RdfTerm> touchedOrder = [.. touched.Keys];
        touchedOrder.Sort(TermOrder.Instance);

        foreach (RdfTerm graph in touchedOrder)
        {
            (List<FeedChange> asserted, List<FeedChange> retracted) = touched[graph];
            List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> now = EventDeclarations.Triples(proposed, graph);
            HashSet<(RdfTerm, RdfTerm, RdfTerm)> then = new(now, TripleComparer.Instance);

            foreach (FeedChange change in asserted)
            {
                then.Remove((change.Subject, change.Predicate, change.Object));
            }

            foreach (FeedChange change in retracted)
            {
                then.Add((change.Subject, change.Predicate, change.Object));
            }

            EventDeclarations.ReadGraph(graph, now, after, afterProblems, ProblemsDecidedBy.Code);
            EventDeclarations.ReadGraph(graph, [.. then], before, beforeProblems, ProblemsDecidedBy.Code);
        }

        if (afterProblems.Count > 0)
        {
            return DeclarationVerdict.Invalid(afterProblems[0]);
        }

        // The other catalogue graphs, unchanged by the commit, for names.
        HashSet<RdfTerm> touchedGraphs = new(touched.Keys, RdfTerm.Comparer);
        List<RdfTerm> others = [];

        foreach (RdfTerm graph in EventDeclarations.CatalogueGraphs(proposed))
        {
            if (!touchedGraphs.Contains(graph))
            {
                others.Add(graph);
            }
        }
        IReadOnlyList<EventTypeDeclaration> untouched = EventDeclarations.Read(proposed, others).Types;

        Dictionary<RdfTerm, EventTypeDeclaration> beforeByNode = new(RdfTerm.Comparer);

        foreach (EventTypeDeclaration declaration in before)
        {
            beforeByNode.TryAdd(declaration.Node, declaration);
        }

        Dictionary<RdfTerm, EventTypeDeclaration> afterByNode = new(RdfTerm.Comparer);

        foreach (EventTypeDeclaration declaration in after)
        {
            afterByNode.TryAdd(declaration.Node, declaration);
        }

        // §3.4: a declaration is never removed, and a short name never changes.
        before.Sort(static (a, b) => TermOrder.Instance.Compare(a.Node, b.Node));
        after.Sort(static (a, b) => TermOrder.Instance.Compare(a.Node, b.Node));

        foreach (EventTypeDeclaration old in before)
        {
            if (!afterByNode.TryGetValue(old.Node, out EventTypeDeclaration? now))
            {
                return DeclarationVerdict.Invalid(new(old.Node, Reasons.Removed,
                    "a declaration is never removed; retire it with varve:eventRetired true"));
            }

            if (!RdfTerm.Comparer.Equals(old.SourceGraph ?? Vocabulary.Type, now.SourceGraph ?? Vocabulary.Type) || old.Name != now.Name)
            {
                return DeclarationVerdict.Invalid(new(old.Node, Reasons.Removed,
                    "a type's short name and source graph do not change; retire it and declare another"));
            }
        }

        // A short name is one type node's in the dataset; the refusal names
        // only the declaration being written (ADR 0144, NameTakenLeaksNothing).
        HashSet<(string, RdfTerm)> existing = new(NameNodeComparer.Instance);

        foreach (EventTypeDeclaration declaration in before)
        {
            existing.Add((declaration.Name, declaration.Node));
        }

        foreach (EventTypeDeclaration declaration in untouched)
        {
            existing.Add((declaration.Name, declaration.Node));
        }

        SortedDictionary<string, List<EventTypeDeclaration>> byName = new(StringComparer.Ordinal);

        foreach (IReadOnlyList<EventTypeDeclaration> set in (IReadOnlyList<EventTypeDeclaration>[])[after, untouched])
        {
            foreach (EventTypeDeclaration declaration in set)
            {
                if (!byName.TryGetValue(declaration.Name, out List<EventTypeDeclaration>? list))
                {
                    byName[declaration.Name] = list = [];
                }

                list.Add(declaration);
            }
        }

        foreach (List<EventTypeDeclaration> holders in byName.Values)
        {
            if (holders.Count < 2)
            {
                continue;
            }

            holders.Sort(static (a, b) => TermOrder.Instance.Compare(a.Node, b.Node));
            List<EventTypeDeclaration> newcomers = [];

            foreach (EventTypeDeclaration holder in holders)
            {
                if (!existing.Contains((holder.Name, holder.Node)))
                {
                    newcomers.Add(holder);
                }
            }

            EventTypeDeclaration blamed = newcomers.Count > 0 && newcomers.Count < holders.Count ? newcomers[0] : holders[1];
            return DeclarationVerdict.Invalid(new(blamed.Node, Reasons.NameTaken, "the short name is taken"));
        }

        foreach (EventTypeDeclaration now in after)
        {
            if (!beforeByNode.TryGetValue(now.Node, out EventTypeDeclaration? old))
            {
                continue;
            }

            if (now.Version < old.Version)
            {
                return DeclarationVerdict.Invalid(new(now.Node, Reasons.Version, "a version never decreases"));
            }

            if (now.Version == old.Version)
            {
                IReadOnlyList<FieldChange> incompatible = Incompatibilities(old, now);

                if (incompatible.Count > 0)
                {
                    return DeclarationVerdict.Incompatible(now.Node, incompatible);
                }
            }
        }

        return DeclarationVerdict.Accept;
    }

    /// <summary>
    /// The changes from <paramref name="old"/> to <paramref name="now"/> that
    /// let the new schema accept an instance the old one refuses (§7.2), in
    /// field-name order.
    /// </summary>
    internal static IReadOnlyList<FieldChange> Incompatibilities(EventTypeDeclaration old, EventTypeDeclaration now)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(now);
        Dictionary<string, EventFieldDeclaration> newFields = new(StringComparer.Ordinal);
        Dictionary<string, EventFieldDeclaration> oldFields = new(StringComparer.Ordinal);
        SortedSet<string> names = new(StringComparer.Ordinal);

        foreach (EventFieldDeclaration field in now.Fields)
        {
            newFields[field.Name] = field;
            names.Add(field.Name);
        }

        foreach (EventFieldDeclaration field in old.Fields)
        {
            oldFields[field.Name] = field;
            names.Add(field.Name);
        }
        List<FieldChange> changes = [];

        foreach (string name in names)
        {
            bool inOld = oldFields.TryGetValue(name, out EventFieldDeclaration? before);
            bool inNew = newFields.TryGetValue(name, out EventFieldDeclaration? after);

            if (inOld && !inNew)
            {
                changes.Add(new(name, "removed"));
            }
            else if (!inOld)
            {
                if (after!.Required)
                {
                    changes.Add(new(name, "required"));
                }
            }
            else if (ValueMapping.KindOf(before!) != ValueMapping.KindOf(after!) || before!.AsString != after!.AsString || before.KeepLanguage != after.KeepLanguage)
            {
                changes.Add(new(name, "type"));
            }
            else if (!before.Required && after.Required)
            {
                changes.Add(new(name, "required"));
            }
            else if (before.Required && !after.Required)
            {
                changes.Add(new(name, "optional"));
            }
        }

        return changes;
    }

    private sealed class TripleComparer : IEqualityComparer<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)>
    {
        internal static readonly TripleComparer Instance = new();

        public bool Equals((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) x, (RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) y) =>
            RdfTerm.Comparer.Equals(x.Subject, y.Subject) && RdfTerm.Comparer.Equals(x.Predicate, y.Predicate) && RdfTerm.Comparer.Equals(x.Object, y.Object);

        public int GetHashCode((RdfTerm Subject, RdfTerm Predicate, RdfTerm Object) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Subject), RdfTerm.Comparer.GetHashCode(obj.Predicate), RdfTerm.Comparer.GetHashCode(obj.Object));
    }

    private sealed class NameNodeComparer : IEqualityComparer<(string Name, RdfTerm Node)>
    {
        internal static readonly NameNodeComparer Instance = new();

        public bool Equals((string Name, RdfTerm Node) x, (string Name, RdfTerm Node) y) =>
            x.Name == y.Name && RdfTerm.Comparer.Equals(x.Node, y.Node);

        public int GetHashCode((string Name, RdfTerm Node) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Name), RdfTerm.Comparer.GetHashCode(obj.Node));
    }
}
