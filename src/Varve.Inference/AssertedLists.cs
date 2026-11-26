// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// The lists the asserted statements make, as the list rules and checks read
/// them (ADR 0197 point 10): a cell is a subject the asserted statements give
/// exactly one <c>rdf:first</c> and exactly one <c>rdf:rest</c>, and a list
/// is the one chain of cells from a head along their <c>rdf:rest</c> to
/// <c>rdf:nil</c>, no cell twice. An inferred <c>rdf:first</c> or
/// <c>rdf:rest</c> neither makes a list nor breaks one; every other subject
/// of an asserted one is unread, with its reason.
/// </summary>
/// <remarks>
/// Read from the explicit statements alone, the reading is fixed while the
/// rules run: a list rule is then an ordinary monotone rule over the derived
/// facts, with one instance per list, and a change to the reading is a change
/// to the closure's input, which rebuilds it (<see cref="Closure"/>). Its size
/// is the number of asserted list statements, and a list's length at most
/// the number of cells (#399).
/// </remarks>
[DesignDecision(typeof(Owl2RlAndRdfsAsRules.ListsReadFromAssertedStatements), Scope = ExceptionScope.Boundary)]
internal sealed class AssertedLists
{
    private readonly ListTerms _list;
    private readonly Dictionary<ulong, Cell> _read = [];
    private readonly Dictionary<ulong, List<TermHandle>> _pointedFrom = [];
    private readonly Dictionary<ulong, List<TermHandle>> _holding = [];
    private readonly List<UnreadList> _unread = [];

    private AssertedLists(ListTerms list) => _list = list;

    /// <summary>The cells the statements do not make lists of, each with its reason, ordered by handle.</summary>
    internal IReadOnlyList<UnreadList> Unread => _unread;

    /// <summary>
    /// The reading of <paramref name="statements"/>, the explicit
    /// <c>rdf:first</c> and <c>rdf:rest</c> statements of a closure and of
    /// the TBox closure beneath it. A cell <paramref name="beneath"/> reads,
    /// the TBox's, is read as it reads it, whatever the data graph asserts
    /// of it: the TBox closure's conclusions from its lists hold under every
    /// data graph (ADR 0195 point 3), so a data graph can neither break nor
    /// change a TBox list, and a statement of its own that differs is
    /// reported (<see cref="UnreadListReason.DiffersFromTBox"/>).
    /// </summary>
    internal static AssertedLists Read(IEnumerable<Fact> statements, ListTerms list, AssertedLists? beneath)
    {
        AssertedLists lists = new(list);
        Dictionary<ulong, (List<Fact> Firsts, List<Fact> Rests)> bySubject = [];
        foreach (Fact statement in statements)
        {
            if (!bySubject.TryGetValue(statement.Subject.Value, out (List<Fact> Firsts, List<Fact> Rests) found))
            {
                found = ([], []);
                bySubject[statement.Subject.Value] = found;
            }

            (statement.Predicate.Value == list.First.Value ? found.Firsts : found.Rests).Add(statement);
        }

        Dictionary<ulong, Cell> candidates = [];
        Dictionary<ulong, UnreadListReason> unread = [];
        HashSet<ulong> differing = [];
        if (beneath is not null)
        {
            foreach ((ulong subject, Cell cell) in beneath._read)
            {
                lists._read[subject] = cell;
            }
        }

        foreach ((ulong subject, (List<Fact> firsts, List<Fact> rests)) in bySubject)
        {
            if (lists._read.TryGetValue(subject, out Cell tbox))
            {
                // The TBox's cell: read as the TBox reads it; a statement of the data graph's beside it is reported.
                foreach (Fact statement in firsts)
                {
                    if (!statement.Equals(tbox.FirstFact))
                    {
                        differing.Add(subject);
                    }
                }

                foreach (Fact statement in rests)
                {
                    if (!statement.Equals(tbox.RestFact))
                    {
                        differing.Add(subject);
                    }
                }

                continue;
            }

            if (firsts.Count > 1)
            {
                unread[subject] = UnreadListReason.SeveralFirsts;
            }
            else if (rests.Count > 1)
            {
                unread[subject] = UnreadListReason.SeveralRests;
            }
            else if (firsts.Count == 0)
            {
                unread[subject] = UnreadListReason.NoFirst;
            }
            else if (rests.Count == 0)
            {
                unread[subject] = UnreadListReason.NoRest;
            }
            else if (subject == list.Nil.Value)
            {
                // rdf:nil ends every list and is never a cell of one.
                unread[subject] = UnreadListReason.Broken;
            }
            else
            {
                candidates[subject] = new Cell(firsts[0].Object, firsts[0], rests[0]);
            }
        }

        // Each candidate is read when its chain reaches rdf:nil through candidates alone, no cell twice.
        HashSet<ulong> visiting = [];
        List<ulong> chain = [];
        foreach (ulong start in candidates.Keys)
        {
            if (lists._read.ContainsKey(start) || unread.ContainsKey(start))
            {
                continue;
            }

            chain.Clear();
            visiting.Clear();
            ulong at = start;
            UnreadListReason? outcome;
            while (true)
            {
                if (at == list.Nil.Value || lists._read.ContainsKey(at))
                {
                    outcome = null;
                    break;
                }

                if (unread.TryGetValue(at, out UnreadListReason below))
                {
                    outcome = below == UnreadListReason.Cycle ? UnreadListReason.Cycle : UnreadListReason.Broken;
                    break;
                }

                if (!candidates.TryGetValue(at, out Cell cell))
                {
                    outcome = UnreadListReason.Broken;
                    break;
                }

                if (!visiting.Add(at))
                {
                    outcome = UnreadListReason.Cycle;
                    break;
                }

                chain.Add(at);
                at = cell.RestFact.Object.Value;
            }

            foreach (ulong subject in chain)
            {
                if (outcome is { } reason)
                {
                    unread[subject] = reason;
                }
                else
                {
                    lists._read[subject] = candidates[subject];
                }
            }
        }

        foreach ((ulong subject, Cell cell) in lists._read)
        {
            Add(lists._pointedFrom, cell.RestFact.Object.Value, new TermHandle(subject));
            Add(lists._holding, cell.Member.Value, new TermHandle(subject));
        }

        foreach (ulong subject in differing)
        {
            unread[subject] = UnreadListReason.DiffersFromTBox;
        }

        List<ulong> ordered = [.. unread.Keys];
        ordered.Sort();
        foreach (ulong subject in ordered)
        {
            lists._unread.Add(new UnreadList(new TermHandle(subject), unread[subject]));
        }

        return lists;
    }

    private static void Add(Dictionary<ulong, List<TermHandle>> map, ulong key, TermHandle value)
    {
        if (!map.TryGetValue(key, out List<TermHandle>? values))
        {
            values = [];
            map[key] = values;
        }

        values.Add(value);
    }

    /// <summary>Whether <paramref name="statement"/> is an <c>rdf:first</c> or <c>rdf:rest</c> statement, the kind this reads.</summary>
    internal static bool IsListStatement(in Fact statement, ListTerms list) =>
        statement.Predicate.Value == list.First.Value || statement.Predicate.Value == list.Rest.Value;

    /// <summary>
    /// The list from <paramref name="head"/>, its cells in order, into
    /// <paramref name="path"/>: false when the head is neither <c>rdf:nil</c>
    /// nor a read cell, or a cell's statements are not yet among
    /// <paramref name="facts"/>, as while a rebuild inserts them again: an
    /// instance is only ever one whose premises are all held.
    /// </summary>
    internal bool TryPath(TermHandle head, List<Cell> path, FactSet facts)
    {
        path.Clear();
        TermHandle at = head;
        while (at.Value != _list.Nil.Value)
        {
            if (!_read.TryGetValue(at.Value, out Cell cell) || !facts.Contains(cell.FirstFact) || !facts.Contains(cell.RestFact))
            {
                path.Clear();
                return false;
            }

            path.Add(cell);
            at = cell.RestFact.Object;
        }

        return true;
    }

    /// <summary>Whether <paramref name="fact"/> is one of the two statements that make a read cell.</summary>
    internal bool IsCellStatement(in Fact fact) =>
        _read.TryGetValue(fact.Subject.Value, out Cell cell) && (cell.FirstFact.Equals(fact) || cell.RestFact.Equals(fact));

    /// <summary>The read cells whose <c>rdf:first</c> is <paramref name="member"/>.</summary>
    internal IReadOnlyList<TermHandle> CellsHolding(TermHandle member) =>
        _holding.TryGetValue(member.Value, out List<TermHandle>? cells) ? cells : [];

    /// <summary>The heads of the lists <paramref name="cell"/> is a cell of: itself, and every read cell whose chain reaches it.</summary>
    internal void HeadsReaching(TermHandle cell, HashSet<ulong> into)
    {
        Stack<TermHandle> pending = new();
        pending.Push(cell);
        while (pending.Count > 0)
        {
            TermHandle next = pending.Pop();
            if (!into.Add(next.Value))
            {
                continue;
            }

            if (_pointedFrom.TryGetValue(next.Value, out List<TermHandle>? before))
            {
                foreach (TermHandle earlier in before)
                {
                    pending.Push(earlier);
                }
            }
        }
    }

    /// <summary>Whether <paramref name="other"/> reads the same cells from the same statements: the list rules' input unchanged.</summary>
    internal bool SameReading(AssertedLists other)
    {
        if (other._read.Count != _read.Count)
        {
            return false;
        }

        foreach ((ulong subject, Cell cell) in _read)
        {
            if (!other._read.TryGetValue(subject, out Cell theirs) || !theirs.Equals(cell))
            {
                return false;
            }
        }

        return true;
    }
}
