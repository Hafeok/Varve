// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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

    // Under a cross-graph merge, each data graph's reading with the TBox's, by the graph's index; the TBox's own under -1.
    private readonly SortedDictionary<int, AssertedLists>? _parts;

    private AssertedLists(ListTerms list) => _list = list;

    private AssertedLists(ListTerms list, SortedDictionary<int, AssertedLists> parts)
    {
        _list = list;
        _parts = parts;
        HashSet<UnreadList> seen = [];
        foreach (AssertedLists part in parts.Values)
        {
            foreach (UnreadList unread in part._unread)
            {
                if (seen.Add(unread))
                {
                    _unread.Add(unread);
                }
            }
        }

        _unread.Sort((a, b) => a.Cell.Value != b.Cell.Value ? a.Cell.Value.CompareTo(b.Cell.Value) : a.Reason.CompareTo(b.Reason));
    }

    /// <summary>
    /// The reading of a cross-graph merge's lists (ADR 0197, amended
    /// 2026-11-26): each data graph's lists from its own statements and the
    /// TBox's, never across graphs, so one graph's statement never makes
    /// another graph's list unread. A cell its graph does not complete, or a
    /// chain that goes on only in another graph, is unread there as
    /// <see cref="UnreadListReason.SpansGraphs"/>. Two graphs that read one
    /// head differently each give it a list.
    /// </summary>
    internal static AssertedLists PerGraph(IReadOnlyDictionary<int, HashSet<Fact>> byGraph, ListTerms list, AssertedLists? beneath)
    {
        Dictionary<ulong, int> graphsNaming = [];
        foreach (HashSet<Fact> statements in byGraph.Values)
        {
            HashSet<ulong> subjects = [];
            foreach (Fact statement in statements)
            {
                if (subjects.Add(statement.Subject.Value))
                {
                    graphsNaming[statement.Subject.Value] = graphsNaming.GetValueOrDefault(statement.Subject.Value) + 1;
                }
            }
        }

        SortedDictionary<int, AssertedLists> parts = [];
        if (beneath is not null)
        {
            parts[-1] = beneath;
        }

        foreach ((int graph, HashSet<Fact> statements) in byGraph)
        {
            if (statements.Count == 0)
            {
                continue;
            }

            HashSet<ulong> own = [];
            foreach (Fact statement in statements)
            {
                own.Add(statement.Subject.Value);
            }

            parts[graph] = Read(statements, list, beneath, subject => graphsNaming.GetValueOrDefault(subject) > (own.Contains(subject) ? 1 : 0));
        }

        return new AssertedLists(list, parts);
    }

    /// <summary>The cells one data graph's statements leave unread, with the TBox's beneath: none for a reading not per graph.</summary>
    internal IReadOnlyList<UnreadList> UnreadIn(int graph)
    {
        if (_parts is null || !_parts.TryGetValue(graph, out AssertedLists? part))
        {
            return [];
        }

        if (!_parts.TryGetValue(-1, out AssertedLists? tbox) || tbox._unread.Count == 0)
        {
            return part._unread;
        }

        HashSet<UnreadList> under = [.. tbox._unread];
        List<UnreadList> own = [];
        foreach (UnreadList unread in part._unread)
        {
            if (!under.Contains(unread))
            {
                own.Add(unread);
            }
        }

        return own;
    }

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
    internal static AssertedLists Read(IEnumerable<Fact> statements, ListTerms list, AssertedLists? beneath) => Read(statements, list, beneath, static _ => false);

    // elsewhere: whether another data graph asserts an rdf:first or rdf:rest of a subject.
    private static AssertedLists Read(IEnumerable<Fact> statements, ListTerms list, AssertedLists? beneath, Func<ulong, bool> elsewhere)
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
                unread[subject] = elsewhere(subject) ? UnreadListReason.SpansGraphs : UnreadListReason.NoFirst;
            }
            else if (rests.Count == 0)
            {
                unread[subject] = elsewhere(subject) ? UnreadListReason.SpansGraphs : UnreadListReason.NoRest;
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
                    outcome = below is UnreadListReason.Cycle or UnreadListReason.SpansGraphs ? below : UnreadListReason.Broken;
                    break;
                }

                if (!candidates.TryGetValue(at, out Cell cell))
                {
                    outcome = elsewhere(at) ? UnreadListReason.SpansGraphs : UnreadListReason.Broken;
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
    internal IEnumerable<List<Cell>> Paths(TermHandle head, FactSet facts)
    {
        if (_parts is null)
        {
            List<Cell> path = [];
            if (TryPath(head, path, facts))
            {
                yield return path;
            }

            yield break;
        }

        // Each graph's reading of the head, once each: graphs that read it alike give it one list.
        List<List<Cell>> given = [];
        foreach (AssertedLists part in _parts.Values)
        {
            List<Cell> path = [];
            if (part.TryPath(head, path, facts) && !given.Exists(other => Same(other, path)))
            {
                given.Add(path);
                yield return path;
            }
        }
    }

    private bool TryPath(TermHandle head, List<Cell> path, FactSet facts)
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
    internal bool IsCellStatement(in Fact fact)
    {
        if (_parts is not null)
        {
            foreach (AssertedLists part in _parts.Values)
            {
                if (part.IsCellStatement(fact))
                {
                    return true;
                }
            }

            return false;
        }

        return _read.TryGetValue(fact.Subject.Value, out Cell cell) && (cell.FirstFact.Equals(fact) || cell.RestFact.Equals(fact));
    }

    /// <summary>The read cells whose <c>rdf:first</c> is <paramref name="member"/>.</summary>
    internal IReadOnlyList<TermHandle> CellsHolding(TermHandle member)
    {
        if (_parts is not null)
        {
            HashSet<TermHandle> cells = [];
            foreach (AssertedLists part in _parts.Values)
            {
                cells.UnionWith(part.CellsHolding(member));
            }

            return [.. cells];
        }

        return _holding.TryGetValue(member.Value, out List<TermHandle>? held) ? held : [];
    }

    /// <summary>The heads of the lists <paramref name="cell"/> is a cell of: itself, and every read cell whose chain reaches it.</summary>
    internal void HeadsReaching(TermHandle cell, HashSet<ulong> into)
    {
        if (_parts is not null)
        {
            foreach (AssertedLists part in _parts.Values)
            {
                HashSet<ulong> heads = [];
                part.HeadsReaching(cell, heads);
                into.UnionWith(heads);
            }

            return;
        }

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

    /// <summary>
    /// The heads of every list whose reading differs between this reading and
    /// <paramref name="other"/>: each cell read in one and not the other, or
    /// read differently, and every cell whose chain reaches it, in either.
    /// </summary>
    internal HashSet<ulong> HeadsChangedFrom(AssertedLists other)
    {
        if (_parts is not null || other._parts is not null)
        {
            // Graph by graph: a graph read on one side only is compared with what it reads with no
            // statements of its own, the TBox's lists, or none.
            AssertedLists none = Part(this, -1) ?? Part(other, -1) ?? new(_list);
            HashSet<ulong> changed = [];
            HashSet<int> graphs = [];
            foreach (int graph in Parts(this))
            {
                graphs.Add(graph);
            }

            foreach (int graph in Parts(other))
            {
                graphs.Add(graph);
            }

            foreach (int graph in graphs)
            {
                AssertedLists mine = Part(this, graph) ?? none, theirs = Part(other, graph) ?? none;
                changed.UnionWith(mine.HeadsChangedFrom(theirs));
            }

            return changed;
        }

        HashSet<ulong> heads = [];
        foreach ((ulong subject, Cell cell) in _read)
        {
            if (!other._read.TryGetValue(subject, out Cell theirs) || !theirs.Equals(cell))
            {
                HeadsReaching(new TermHandle(subject), heads);
                other.HeadsReaching(new TermHandle(subject), heads);
            }
        }

        foreach (ulong subject in other._read.Keys)
        {
            if (!_read.ContainsKey(subject))
            {
                HeadsReaching(new TermHandle(subject), heads);
                other.HeadsReaching(new TermHandle(subject), heads);
            }
        }

        return heads;
    }

    /// <summary>Whether <paramref name="other"/> reads the same cells from the same statements: the list rules' input unchanged.</summary>
    internal bool SameReading(AssertedLists other)
    {
        if (_parts is not null || other._parts is not null)
        {
            if (_parts is null || other._parts is null || _parts.Count != other._parts.Count)
            {
                return false;
            }

            foreach ((int graph, AssertedLists part) in _parts)
            {
                if (!other._parts.TryGetValue(graph, out AssertedLists? theirs) || !part.SameReading(theirs))
                {
                    return false;
                }
            }

            return true;
        }

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

    /// <summary>Whether the lists are read per graph, a cross-graph merge's (ADR 0197, amended 2026-11-26).</summary>
    internal bool IsPerGraph => _parts is not null;

    /// <summary>
    /// The graphs whose reading holds every one of <paramref name="cells"/>
    /// as a cell statement: those that read the list an instance over those
    /// cells took, -1 for the TBox's. Under a cross-graph merge, an
    /// instance's cells are drawn through one of these graphs, whatever other
    /// graphs assert the same statements.
    /// </summary>
    internal void ReadersOf(List<Fact> cells, List<int> into)
    {
        into.Clear();
        if (_parts is null)
        {
            return;
        }

        foreach ((int graph, AssertedLists part) in _parts)
        {
            if (cells.TrueForAll(cell => part.IsCellStatement(cell)))
            {
                into.Add(graph);
            }
        }
    }

    /// <summary>The data graphs read, by index: none for a reading not per graph.</summary>
    internal IEnumerable<int> Graphs
    {
        get
        {
            foreach (int graph in Parts(this))
            {
                if (graph >= 0)
                {
                    yield return graph;
                }
            }
        }
    }

    private static IReadOnlyCollection<int> Parts(AssertedLists lists) => lists._parts is { } parts ? parts.Keys : Array.Empty<int>();

    private static bool Same(List<Cell> a, List<Cell> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static AssertedLists? Part(AssertedLists lists, int graph) =>
        lists._parts is not null && lists._parts.TryGetValue(graph, out AssertedLists? part) ? part : null;

    /// <summary>The cells of the read lists of one graph whose heads are among <paramref name="heads"/>, both statements of each: what an instance over them has as premises.</summary>
    internal IEnumerable<Fact> CellStatementsOf(int graph, IReadOnlySet<ulong> heads)
    {
        if (Part(this, graph) is not { } part)
        {
            yield break;
        }

        HashSet<ulong> reached = [];
        foreach ((ulong subject, Cell cell) in part._read)
        {
            reached.Clear();
            part.HeadsReaching(new TermHandle(subject), reached);
            if (reached.Overlaps(heads))
            {
                yield return cell.FirstFact;
                yield return cell.RestFact;
            }
        }
    }
}
