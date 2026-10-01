// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Expressions;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>A group key: its expression, and the slot its value goes to.</summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed record GroupKeySpec(Expr Expression, int Slot);

/// <summary>An aggregate extracted from above a <c>Group</c> (ADR 0053), and the hidden slot its value goes to.</summary>
internal sealed record AggregateSpec(
    [property: HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))] AggregateFunction Function,
    [property: HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))] Expr? Argument,
    bool Distinct,
    string Separator,
    IExtensionAggregate? Custom,
    [property: HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))] int Slot);

/// <summary>
/// §18.5.1 <c>Group</c> and <c>Aggregation</c> (ADR 0053): hash grouping by
/// term equality of the key values, one accumulator per aggregate per group,
/// errors per the ADR's table, no spilling. The output binds the keys that
/// have variables and the aggregates' slots, and nothing else.
/// </summary>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class GroupOperator(Operator inner, GroupKeySpec[] keys, List<AggregateSpec> aggregates) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        GroupTable table = GroupTable.Create(exec, keys.Length);
        TermRef[] key = table.Probe;
        using (IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph))
        {
            while (solutions.MoveNext())
            {
                exec.Check();
                ulong[] row = solutions.Current;
                for (int i = 0; i < keys.Length; i++)
                {
                    Value value = keys[i].Expression.Eval(exec, row, graph);
                    key[i] = value.IsError ? TermRef.Unbound : Semantics.ToRef(exec, value);
                }

                Accumulator[] state = table.Find(exec, aggregates);
                for (int i = 0; i < state.Length; i++)
                {
                    state[i].Add(exec, row, graph);
                }
            }
        }

        if (table.Count == 0 && keys.Length == 0)
        {
            table.Find(exec, aggregates);
        }

        for (int g = 0; g < table.Count; g++)
        {
            TermRef[] found = table.Key(g);
            Accumulator[] state = table.State(g);
            ulong[] output = exec.NewRow();
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Slot >= 0 && found[i].IsBound)
                {
                    Rows.Set(output, exec.Width, keys[i].Slot, found[i]);
                }
            }

            for (int i = 0; i < state.Length; i++)
            {
                Value result = state[i].Result(exec);
                if (!result.IsError)
                {
                    Rows.Set(output, exec.Width, aggregates[i].Slot, Semantics.ToRef(exec, result));
                }
            }

            yield return output;
        }
    }

    /// <summary>
    /// The groups of one Open, in the order they were first met. A solution's
    /// key is worked out in <see cref="Probe"/>, which is copied only for a
    /// group not seen before: a solution of a known group costs no array.
    /// </summary>
    private sealed class GroupTable
    {
        private readonly Dictionary<TermRef[], Accumulator[]> _groups;
        private readonly List<TermRef[]> _keys = [];
        private readonly List<Accumulator[]> _states = [];

        private GroupTable(Exec exec, int width)
        {
            _groups = new Dictionary<TermRef[], Accumulator[]>(new KeyComparer(exec));
            Probe = new TermRef[width];
        }

        /// <summary>The key being looked up, written in place for each solution.</summary>
        internal TermRef[] Probe { get; }

        internal int Count => _keys.Count;

        [DesignDecision(typeof(AggregationByHashGroupingAndAccumulators.HashAggregationWithoutSpill), Scope = ExceptionScope.HotPath)]
        internal static GroupTable Create(Exec exec, int width) => new(exec, width);

        internal TermRef[] Key(int group) => _keys[group];

        internal Accumulator[] State(int group) => _states[group];

        /// <summary>The accumulators of <see cref="Probe"/>'s group, made when it is new.</summary>
        [DesignDecision(typeof(AggregationByHashGroupingAndAccumulators.HashAggregationWithoutSpill), Scope = ExceptionScope.HotPath)]
        internal Accumulator[] Find(Exec exec, List<AggregateSpec> aggregates)
        {
            if (_groups.TryGetValue(Probe, out Accumulator[]? state))
            {
                return state;
            }

            TermRef[] key = (TermRef[])Probe.Clone();
            state = new Accumulator[aggregates.Count];
            for (int i = 0; i < state.Length; i++)
            {
                state[i] = new Accumulator(exec, aggregates[i]);
            }

            _groups.Add(key, state);
            _keys.Add(key);
            _states.Add(state);
            return state;
        }
    }

    private sealed class KeyComparer(Exec exec) : IEqualityComparer<TermRef[]>
    {
        public bool Equals(TermRef[]? x, TermRef[]? y)
        {
            if (x is null || y is null || x.Length != y.Length)
            {
                return x is null && y is null;
            }

            for (int i = 0; i < x.Length; i++)
            {
                if (x[i].IsBound != y[i].IsBound || (x[i].IsBound && !exec.TermEquals(x[i], y[i])))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(TermRef[] obj)
        {
            HashCode hash = default;
            foreach (TermRef value in obj)
            {
                hash.Add(value.IsBound ? exec.TermHash(value) : 0);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>One aggregate's state for one group, per ADR 0053's table.</summary>
    private sealed class Accumulator
    {
        private readonly AggregateSpec _spec;
        private readonly HashSet<TermRef>? _distinct;
        private readonly HashSet<ulong[]>? _distinctRows;
        private readonly IAggregateAccumulator? _custom;
        private readonly List<byte> _text = [];
        private readonly byte[] _separator;
        private long _count;
        private XsdNumeric _sum = XsdNumeric.FromInteger(XsdInteger.Zero);
        private bool _failed;
        private Value _best = Value.Error;
        private bool _any;

        internal Accumulator(Exec exec, AggregateSpec spec)
        {
            _spec = spec;
            if (spec.Distinct)
            {
                if (spec.Argument is null)
                {
                    int[] all = new int[exec.Width];
                    for (int i = 0; i < all.Length; i++)
                    {
                        all[i] = i;
                    }

                    _distinctRows = new HashSet<ulong[]>(new RowKeyComparer(exec, all));
                }
                else
                {
                    _distinct = new HashSet<TermRef>(new TermRefComparer(exec));
                }
            }

            _custom = spec.Custom?.CreateAccumulator(spec.Distinct);
            _separator = Encoding.UTF8.GetBytes(spec.Separator);
        }

        internal void Add(Exec exec, ulong[] row, ActiveGraph graph)
        {
            if (_spec.Argument is null)
            {
                // COUNT(*): solutions, or distinct solutions.
                if (_distinctRows is null || FirstTime(_distinctRows, row))
                {
                    _count++;
                }

                return;
            }

            Value value = _spec.Argument.Eval(exec, row, graph);
            if (value.IsError)
            {
                // COUNT and SAMPLE skip an error; the others become one.
                if (_spec.Function is not (AggregateFunction.Count or AggregateFunction.Sample or AggregateFunction.Custom))
                {
                    _failed = true;
                }

                return;
            }

            if (_distinct is not null && !FirstTime(_distinct, Semantics.ToRef(exec, value)))
            {
                return;
            }

            switch (_spec.Function)
            {
                case AggregateFunction.Count:
                    _count++;
                    break;
                case AggregateFunction.Sum:
                case AggregateFunction.Avg:
                    _count++;
                    if (!_failed && (!Semantics.TryNumeric(exec, value, out XsdNumeric number) || !XsdNumeric.TryAdd(_sum, number, out _sum)))
                    {
                        _failed = true;
                    }

                    break;
                case AggregateFunction.Min:
                case AggregateFunction.Max:
                    if (!_any)
                    {
                        _best = value;
                        _any = true;
                    }
                    else
                    {
                        int c = Semantics.OrderCompare(exec, value, _best);
                        if (_spec.Function == AggregateFunction.Min ? c < 0 : c > 0)
                        {
                            _best = value;
                        }
                    }

                    break;
                case AggregateFunction.Sample:
                    if (!_any)
                    {
                        _best = value;
                        _any = true;
                    }

                    break;
                case AggregateFunction.GroupConcat:
                    RdfTerm term = Semantics.AsTerm(exec, value)!;
                    if (term.Kind != RdfTermKind.Literal)
                    {
                        _failed = true;
                        break;
                    }

                    if (_any)
                    {
                        Append(_separator);
                    }

                    Append(term.Lexical);
                    _any = true;
                    break;
                case AggregateFunction.Custom:
                    AddCustom(Semantics.AsTerm(exec, value)!);
                    break;
            }
        }

        /// <summary>Whether a DISTINCT aggregate meets this value, or COUNT(DISTINCT *) this solution, for the first time.</summary>
        [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
        private static bool FirstTime<T>(HashSet<T> seen, T item) => seen.Add(item);

        /// <summary>GROUP_CONCAT's text, which grows with the group.</summary>
        [DesignDecision(typeof(EvaluationHotPathScope.BlockingOperatorsHoldTheirInput), Scope = ExceptionScope.HotPath)]
        private void Append(ReadOnlySpan<byte> bytes) => _text.AddRange(bytes);

        [DesignDecision(typeof(EvaluationHotPathScope.ExtensionFunctionsAreTheCallersCode), Scope = ExceptionScope.HotPath)]
        private void AddCustom(RdfTerm term) => _custom!.Add(term);

        [DesignDecision(typeof(EvaluationHotPathScope.ExtensionFunctionsAreTheCallersCode), Scope = ExceptionScope.HotPath)]
        private Value CustomResult() => _custom!.TryGetResult(out RdfTerm? result) ? Value.Of(result) : Value.Error;

        /// <summary>GROUP_CONCAT's literal, made once per group.</summary>
        [DesignDecision(typeof(EvaluationHotPathScope.TermBuildingExpressionsAllocate), Scope = ExceptionScope.HotPath)]
        private Value Concatenated() => Value.Of(RdfTerm.Literal(_text.ToArray()));

        internal Value Result(Exec exec)
        {
            switch (_spec.Function)
            {
                case AggregateFunction.Count:
                    return Value.Of(XsdNumeric.FromInteger(new XsdInteger(_count)));
                case AggregateFunction.Sum:
                    return _failed ? Value.Error : Value.Of(_sum);
                case AggregateFunction.Avg:
                    if (_failed)
                    {
                        return Value.Error;
                    }

                    if (_count == 0)
                    {
                        return Value.Of(XsdNumeric.FromInteger(XsdInteger.Zero));
                    }

                    return XsdNumeric.TryDivide(_sum, XsdNumeric.FromInteger(new XsdInteger(_count)), out XsdNumeric average)
                        ? Value.Of(average)
                        : Value.Error;
                case AggregateFunction.Min:
                case AggregateFunction.Max:
                case AggregateFunction.Sample:
                    return _failed || !_any ? Value.Error : _best;
                case AggregateFunction.GroupConcat:
                    return _failed ? Value.Error : Concatenated();
                default:
                    return CustomResult();
            }
        }
    }
}
