// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Expressions;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>§18.5 <c>Join</c> (<c>sparql-evaluation.md</c> §6.2).</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class JoinOperator(Operator left, Operator right) : Operator
{
    private int[]? _keys;

    internal Operator Left { get; } = left;

    internal Operator Right { get; } = right;

    internal override bool Substitutable => Left.Substitutable && Right.Substitutable;

    internal override bool ScansBindGraph => Left.ScansBindGraph || Right.ScansBindGraph;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        if (Right is ServiceOperator service)
        {
            return service.Join(exec, Left.Open(exec, input, graph), graph);
        }

        return Right.Substitutable ? BindJoin(exec, input, graph) : HashJoin(exec, input, graph);
    }

    /// <summary>Each left solution is the incoming solution of the right: an index nested loop.</summary>
    private IEnumerator<ulong[]> BindJoin(Exec exec, ulong[] input, ActiveGraph graph)
    {
        using IEnumerator<ulong[]> lefts = Left.Open(exec, input, graph);
        while (lefts.MoveNext())
        {
            using IEnumerator<ulong[]> rights = Right.Open(exec, lefts.Current, graph);
            while (rights.MoveNext())
            {
                yield return rights.Current;
            }
        }
    }

    /// <summary>The right evaluated once, alone, into a table keyed on the variables both sides certainly bind.</summary>
    private IEnumerator<ulong[]> HashJoin(Exec exec, ulong[] input, ActiveGraph graph)
    {
        JoinTable table = JoinTable.Build(exec, Right.Open(exec, input, graph), _keys ??= Keys(Left, Right));
        using IEnumerator<ulong[]> lefts = Left.Open(exec, input, graph);
        while (lefts.MoveNext())
        {
            ulong[] left = lefts.Current;
            List<ulong[]>? candidates = table.Candidates(left);
            for (int i = 0; candidates is not null && i < candidates.Count; i++)
            {
                ulong[] right = candidates[i];
                if (Rows.Compatible(exec, left, right))
                {
                    exec.Check();
                    yield return Rows.Merge(exec.Width, left, right);
                }
            }
        }
    }

    /// <summary>The slots both sides certainly bind; worked out once per operator, on its first hash join.</summary>
    [DesignDecision(typeof(EvaluationHotPathScope.OperatorStateIsMadeOncePerExecution), Scope = ExceptionScope.HotPath)]
    internal static int[] Keys(Operator left, Operator right)
    {
        List<int> keys = [];
        foreach (int slot in left.Certain)
        {
            if (Array.IndexOf(right.Certain, slot) >= 0)
            {
                keys.Add(slot);
            }
        }

        return [.. keys];
    }
}

/// <summary>A materialised side of a join, hashed on key slots every row binds.</summary>
internal sealed class JoinTable
{
    private readonly Dictionary<ulong[], List<ulong[]>>? _buckets;

    private JoinTable(Exec exec, int[] keys)
    {
        if (keys.Length > 0)
        {
            _buckets = new Dictionary<ulong[], List<ulong[]>>(new RowKeyComparer(exec, keys));
        }
    }

    internal int Count => Rows.Count;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal List<ulong[]> Rows { get; } = [];

    [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
    internal static JoinTable Build(Exec exec, IEnumerator<ulong[]> source, int[] keys)
    {
        JoinTable table = new(exec, keys);
        using (source)
        {
            while (source.MoveNext())
            {
                exec.Check();
                table.Rows.Add(source.Current);
                if (table._buckets is not null)
                {
                    if (!table._buckets.TryGetValue(source.Current, out List<ulong[]>? bucket))
                    {
                        bucket = [];
                        table._buckets.Add(source.Current, bucket);
                    }

                    bucket.Add(source.Current);
                }
            }
        }

        return table;
    }

    /// <summary>The rows that may join <paramref name="probe"/>, or null for none.</summary>
    [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
    internal List<ulong[]>? Candidates(ulong[] probe)
    {
        if (_buckets is null)
        {
            return Rows;
        }

        return _buckets.TryGetValue(probe, out List<ulong[]>? bucket) ? bucket : null;
    }
}

/// <summary>Equality and hashing of solutions on some slots, under the source's term equality.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class RowKeyComparer(Exec exec, int[] slots) : IEqualityComparer<ulong[]>
{
    public bool Equals(ulong[]? x, ulong[]? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        foreach (int slot in slots)
        {
            TermRef a = Rows.Get(x, exec.Width, slot);
            TermRef b = Rows.Get(y, exec.Width, slot);
            if (a.IsBound != b.IsBound || (a.IsBound && !exec.TermEquals(a, b)))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(ulong[] obj)
    {
        HashCode hash = default;
        foreach (int slot in slots)
        {
            TermRef value = Rows.Get(obj, exec.Width, slot);
            hash.Add(value.IsBound ? exec.TermHash(value) : 0);
        }

        return hash.ToHashCode();
    }
}

/// <summary>§18.5 <c>LeftJoin</c> (§6.3).</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class LeftJoinOperator(Operator left, Operator right, Expr? condition) : Operator
{
    private int[]? _keys;

    internal override bool ScansBindGraph => left.ScansBindGraph;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph) =>
        right.Substitutable ? Nested(exec, input, graph) : Hashed(exec, input, graph);

    private IEnumerator<ulong[]> Nested(Exec exec, ulong[] input, ActiveGraph graph)
    {
        using IEnumerator<ulong[]> lefts = left.Open(exec, input, graph);
        while (lefts.MoveNext())
        {
            bool matched = false;
            using (IEnumerator<ulong[]> rights = right.Open(exec, lefts.Current, graph))
            {
                while (rights.MoveNext())
                {
                    if (Accept(exec, rights.Current, graph))
                    {
                        matched = true;
                        yield return rights.Current;
                    }
                }
            }

            if (!matched)
            {
                yield return lefts.Current;
            }
        }
    }

    private IEnumerator<ulong[]> Hashed(Exec exec, ulong[] input, ActiveGraph graph)
    {
        JoinTable table = JoinTable.Build(exec, right.Open(exec, input, graph), _keys ??= JoinOperator.Keys(left, right));
        using IEnumerator<ulong[]> lefts = left.Open(exec, input, graph);
        while (lefts.MoveNext())
        {
            ulong[] l = lefts.Current;
            bool matched = false;
            List<ulong[]>? candidates = table.Candidates(l);
            for (int i = 0; candidates is not null && i < candidates.Count; i++)
            {
                ulong[] r = candidates[i];
                if (!Rows.Compatible(exec, l, r))
                {
                    continue;
                }

                ulong[] merged = Rows.Merge(exec.Width, l, r);
                if (Accept(exec, merged, graph))
                {
                    matched = true;
                    yield return merged;
                }
            }

            if (!matched)
            {
                yield return l;
            }
        }
    }

    private bool Accept(Exec exec, ulong[] merged, ActiveGraph graph) =>
        condition is null || Semantics.Ebv(exec, condition.Eval(exec, merged, graph)) == true;
}

/// <summary>§18.5 <c>Filter</c> (§6.4): true passes, false and error do not.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class FilterOperator(Expr condition, Operator inner) : Operator
{
    internal override bool ScansBindGraph => inner.ScansBindGraph;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        using IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph);
        while (solutions.MoveNext())
        {
            exec.Check();
            if (Semantics.Ebv(exec, condition.Eval(exec, solutions.Current, graph)) == true)
            {
                yield return solutions.Current;
            }
        }
    }
}

/// <summary>§18.5 <c>Union</c> (§6.5).</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class UnionOperator(Operator left, Operator right) : Operator
{
    internal override bool Substitutable => left.Substitutable && right.Substitutable;

    internal override bool ScansBindGraph => left.ScansBindGraph && right.ScansBindGraph;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        using (IEnumerator<ulong[]> lefts = left.Open(exec, input, graph))
        {
            while (lefts.MoveNext())
            {
                yield return lefts.Current;
            }
        }

        using IEnumerator<ulong[]> rights = right.Open(exec, input, graph);
        while (rights.MoveNext())
        {
            yield return rights.Current;
        }
    }
}

/// <summary>§18.5 <c>Minus</c> (§6.6): removed only by a compatible solution sharing a bound variable.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class MinusOperator(Operator left, Operator right) : Operator
{
    /// <summary>MINUS compares each left solution with every right one: its table is not keyed.</summary>
    private readonly int[] _noKeys = [];

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        JoinTable removals = JoinTable.Build(exec, right.Open(exec, exec.NewRow(), graph), _noKeys);
        List<ulong[]> rows = removals.Rows;
        using IEnumerator<ulong[]> lefts = left.Open(exec, input, graph);
        while (lefts.MoveNext())
        {
            ulong[] l = lefts.Current;
            bool removed = false;
            for (int i = 0; i < rows.Count; i++)
            {
                ulong[] r = rows[i];
                if (Rows.ShareVariable(exec.Width, l, r) && Rows.Compatible(exec, l, r))
                {
                    removed = true;
                    break;
                }
            }

            if (!removed)
            {
                yield return l;
            }
        }
    }
}

/// <summary>§18.5 <c>Extend</c> (§6.7): an error leaves the variable unbound and keeps the solution.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class ExtendOperator(Operator inner, int slot, Expr expression) : Operator
{
    internal override bool ScansBindGraph => inner.ScansBindGraph;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        using IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph);
        while (solutions.MoveNext())
        {
            exec.Check();
            ulong[] row = solutions.Current;
            Value value = expression.Eval(exec, row, graph);
            if (value.IsError)
            {
                yield return row;
                continue;
            }

            TermRef bound = Semantics.ToRef(exec, value);
            if (row[slot] != 0)
            {
                // Only under substitution (EXISTS) can the slot already be bound.
                if (exec.TermEquals(Rows.Get(row, exec.Width, slot), bound))
                {
                    yield return row;
                }

                continue;
            }

            ulong[] extended = Rows.Copy(row);
            Rows.Set(extended, exec.Width, slot, bound);
            exec.SameSolution(row, extended);
            yield return extended;
        }
    }
}

/// <summary><c>VALUES</c> (§6.8): each row compatible with the incoming solution, merged with it.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class ValuesOperator(RdfTerm?[][] rows, int[] slots) : Operator
{
    private TermRef[][]? _resolved;
    private Exec? _for;

    internal override bool Substitutable => true;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        if (_for != exec || _resolved is null)
        {
            _resolved = Resolve(exec);
            _for = exec;
        }

        return Rows(exec, input, _resolved);
    }

    /// <summary>The rows' terms as slot values, looked up in this execution's source.</summary>
    [DesignDecision(typeof(EvaluationHotPathScope.OperatorStateIsMadeOncePerExecution), Scope = ExceptionScope.HotPath)]
    private TermRef[][] Resolve(Exec exec)
    {
        TermRef[][] resolved = new TermRef[rows.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            resolved[i] = new TermRef[slots.Length];
            for (int j = 0; j < slots.Length; j++)
            {
                resolved[i][j] = rows[i][j] is { } term ? exec.Intern(term) : TermRef.Unbound;
            }
        }

        return resolved;
    }

    private IEnumerator<ulong[]> Rows(Exec exec, ulong[] input, TermRef[][] resolved)
    {
        foreach (TermRef[] values in resolved)
        {
            ulong[]? row = null;
            bool compatible = true;
            for (int j = 0; j < slots.Length && compatible; j++)
            {
                if (!values[j].IsBound)
                {
                    continue;
                }

                if (input[slots[j]] != 0)
                {
                    compatible = exec.TermEquals(Execution.Rows.Get(input, exec.Width, slots[j]), values[j]);
                }
                else
                {
                    row ??= Execution.Rows.Copy(input);
                    Execution.Rows.Set(row, exec.Width, slots[j], values[j]);
                }
            }

            if (compatible)
            {
                yield return row ?? Execution.Rows.Copy(input);
            }
        }
    }
}

/// <summary>
/// §18.5 <c>Graph</c> (§6.11). The pattern is evaluated with the graph
/// variable unbound — §18.6 joins the graph's name on afterwards — so a
/// <c>FILTER(BOUND(?g))</c> inside sees it unbound, and a <c>?g</c> the
/// pattern itself mentions is its own binding until that join.
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class GraphOperator : Operator
{
    private readonly Operator _inner;
    private readonly RdfTerm? _name;
    private readonly int _slot;
    private readonly bool _mentioned;
    private Exec? _for;
    private TermHandle _named;
    private bool _isNamed;

    internal GraphOperator(Operator inner, RdfTerm? name, int slot, bool mentioned)
    {
        _inner = inner;
        _name = name;
        _slot = slot;
        _mentioned = mentioned;
    }

    internal override bool Substitutable => _inner.Substitutable;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        if (_name is not null)
        {
            if (_for != exec)
            {
                _isNamed = ResolveName(exec, _name, out _named);
                _for = exec;
            }

            return _isNamed ? _inner.Open(exec, input, ActiveGraph.Named(_named)) : Solutions.Empty;
        }

        if (input[_slot] != 0)
        {
            if (!exec.TryGetSourceHandle(Rows.Get(input, exec.Width, _slot), out TermHandle bound) || !exec.IsNamedGraph(bound))
            {
                return Solutions.Empty;
            }

            ulong[] seed = Rows.Copy(input);
            Rows.Clear(seed, exec.Width, _slot);
            return JoinName(exec, seed, ActiveGraph.Named(bound), Rows.Get(input, exec.Width, _slot));
        }

        // The scans bind the graph's name themselves only when nothing inside
        // can see the variable before they do.
        return !_mentioned && _inner.ScansBindGraph
            ? _inner.Open(exec, input, ActiveGraph.ForSlot(_slot))
            : EachGraph(exec, input);
    }

    /// <summary>Whether the GRAPH's IRI names one of the dataset's graphs, and its handle; once per execution.</summary>
    [DesignDecision(typeof(EvaluationHotPathScope.OperatorStateIsMadeOncePerExecution), Scope = ExceptionScope.HotPath)]
    private static bool ResolveName(Exec exec, RdfTerm name, out TermHandle named) =>
        exec.Source.TryInternalise(name, out named) && exec.IsNamedGraph(named);

    /// <summary>The pattern once per named graph, the graph's name joined on afterwards.</summary>
    private IEnumerator<ulong[]> EachGraph(Exec exec, ulong[] input)
    {
        List<TermHandle> graphs = exec.NamedGraphs();
        for (int i = 0; i < graphs.Count; i++)
        {
            TermHandle named = graphs[i];
            using IEnumerator<ulong[]> solutions = JoinName(exec, input, ActiveGraph.Named(named), exec.FromSource(named));
            while (solutions.MoveNext())
            {
                yield return solutions.Current;
            }
        }
    }

    /// <summary>The pattern in one graph, the graph's name joined on.</summary>
    private IEnumerator<ulong[]> JoinName(Exec exec, ulong[] input, ActiveGraph graph, TermRef name)
    {
        using (IEnumerator<ulong[]> solutions = _inner.Open(exec, input, graph))
        {
            while (solutions.MoveNext())
            {
                ulong[] row = solutions.Current;
                if (row[_slot] != 0)
                {
                    if (exec.TermEquals(Rows.Get(row, exec.Width, _slot), name))
                    {
                        yield return row;
                    }

                    continue;
                }

                ulong[] named = Rows.Copy(row);
                Rows.Set(named, exec.Width, _slot, name);
                yield return named;
            }
        }
    }
}

/// <summary>
/// §18.5 <c>Project</c> (§6.1, §4.4): the incoming solution restricted to the
/// projected variables on the way in, and the output restricted on the way out,
/// because a sub-<c>SELECT</c>'s other variables are not the outer query's.
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class ProjectOperator(Operator inner, int[] slots) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        using IEnumerator<ulong[]> solutions = inner.Open(exec, Restrict(exec, input), graph);
        while (solutions.MoveNext())
        {
            exec.Check();
            yield return Restrict(exec, solutions.Current);
        }
    }

    private ulong[] Restrict(Exec exec, ulong[] row)
    {
        ulong[] projected = exec.NewRow();
        foreach (int slot in slots)
        {
            if (row[slot] != 0)
            {
                Rows.Set(projected, exec.Width, slot, Rows.Get(row, exec.Width, slot));
            }
        }

        return projected;
    }
}

/// <summary>§18.5 <c>Distinct</c>; <c>Reduced</c> is evaluated the same, as §15.3.2 permits.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class DistinctOperator(Operator inner) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        HashSet<ulong[]> seen = NewSeen(exec);
        using IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph);
        while (solutions.MoveNext())
        {
            exec.Check();
            if (FirstTime(seen, solutions.Current))
            {
                yield return solutions.Current;
            }
        }
    }

    /// <summary>The set of the solutions yielded so far, keyed on every slot.</summary>
    [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
    internal static HashSet<ulong[]> NewSeen(Exec exec)
    {
        int[] all = new int[exec.Width];
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = i;
        }

        return new HashSet<ulong[]>(new RowKeyComparer(exec, all));
    }

    [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
    private static bool FirstTime(HashSet<ulong[]> seen, ulong[] row) => seen.Add(row);
}

/// <summary>§18.5 <c>Slice</c>: the input is not read past the limit.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class SliceOperator(Operator inner, long offset, long? limit) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        if (limit == 0)
        {
            yield break;
        }

        long skipped = 0, taken = 0;
        using IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph);
        while (solutions.MoveNext())
        {
            if (skipped < offset)
            {
                skipped++;
                continue;
            }

            yield return solutions.Current;
            if (limit is { } max && ++taken >= max)
            {
                yield break;
            }
        }
    }
}

/// <summary>§18.5 <c>OrderBy</c> (§6.1): a stable sort under §7.5's order, each key evaluated once per solution.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class OrderByOperator(Operator inner, Expr[] keys, bool[] descending) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        SortTable table = SortTable.Create(exec, keys.Length);
        using (IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph))
        {
            while (solutions.MoveNext())
            {
                exec.Check();
                ulong[] row = solutions.Current;
                table.Hold(row);
                for (int i = 0; i < keys.Length; i++)
                {
                    table.Hold(SortKey(exec, keys[i].Eval(exec, row, graph)));
                }
            }
        }

        int[] order = table.Sort(descending);
        for (int i = 0; i < order.Length; i++)
        {
            yield return table.Row(order[i]);
        }
    }

    /// <summary>
    /// The solutions an ORDER BY holds, and their keys in one list, a row's
    /// keys side by side, so a solution costs its keys and no array of them.
    /// </summary>
    private sealed class SortTable : IComparer<int>
    {
        private readonly Exec _exec;
        private readonly int _width;
        private readonly List<ulong[]> _rows = [];
        private readonly List<Value> _keys = [];
        private bool[] _descending = [];

        private SortTable(Exec exec, int width)
        {
            _exec = exec;
            _width = width;
        }

        [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
        internal static SortTable Create(Exec exec, int width) => new(exec, width);

        [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
        internal void Hold(ulong[] row) => _rows.Add(row);

        [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
        internal void Hold(Value key) => _keys.Add(key);

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal ulong[] Row(int index) => _rows[index];

        /// <summary>The held rows' indices in order: stable, since a tie falls back to the index.</summary>
        [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
        internal int[] Sort(bool[] descending)
        {
            int[] order = new int[_rows.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            _descending = descending;
            Array.Sort(order, this);
            return order;
        }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public int Compare(int x, int y)
        {
            for (int k = 0; k < _width; k++)
            {
                int c = Semantics.OrderCompare(_exec, _keys[(x * _width) + k], _keys[(y * _width) + k]);
                if (c != 0)
                {
                    return _descending[k] ? -c : c;
                }
            }

            return x.CompareTo(y);
        }
    }

    /// <summary>
    /// A key made ready for the n log n comparisons of the sort, once per row:
    /// an inline integer or boolean becomes its value (the accessor arm), and
    /// any other term of the source is externalised once and held as a local
    /// term, whose numeric parse the local table caches. Compared as they come,
    /// every comparison externalised both terms to rank them — ten gigabytes
    /// for a million rows, found by ADR 0050's benchmark.
    /// </summary>
    private static Value SortKey(Exec exec, Value value)
    {
        if (value.Kind != ValueKind.Ref || value.Ref.IsLocal || value.HasNumber)
        {
            return value;
        }

        if (exec.Options.ValueAccess == ValueAccess.InlineAccessor
            && exec.Source.TryGetInlineValue(new TermHandle(value.Ref.Raw), out InlineValue inline))
        {
            switch (inline.Kind)
            {
                case InlineValueKind.Integer:
                    return Value.Of(XsdNumeric.FromInteger(new XsdInteger(inline.Integer)));
                case InlineValueKind.Boolean:
                    return Value.Of(inline.Boolean);
                default:
                    break;
            }
        }

        return HoldAsLocal(exec, value.Ref);
    }

    /// <summary>A key of the source's held as a local term, so the sort externalises it once.</summary>
    [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
    private static Value HoldAsLocal(Exec exec, TermRef key) =>
        Value.Of(new TermRef(exec.Locals.Intern(exec.Materialise(key)), true));
}

/// <summary>Rows already in hand: the answer of a <c>SERVICE</c>, or a test's fixed input.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class TableOperator(List<ulong[]> rows) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            ulong[] row = rows[i];
            if (Rows.Compatible(exec, input, row))
            {
                yield return Rows.Merge(exec.Width, input, row);
            }
        }
    }
}
