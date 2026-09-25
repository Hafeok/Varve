// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Expressions;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>A group key: its expression, and the slot its value goes to.</summary>
internal sealed record GroupKeySpec(Expr Expression, int Slot);

/// <summary>An aggregate extracted from above a <c>Group</c> (ADR 0053), and the hidden slot its value goes to.</summary>
internal sealed record AggregateSpec(
    AggregateFunction Function,
    Expr? Argument,
    bool Distinct,
    string Separator,
    IExtensionAggregate? Custom,
    int Slot);

/// <summary>
/// §18.5.1 <c>Group</c> and <c>Aggregation</c> (ADR 0053): hash grouping by
/// term equality of the key values, one accumulator per aggregate per group,
/// errors per the ADR's table, no spilling. The output binds the keys that
/// have variables and the aggregates' slots, and nothing else.
/// </summary>
internal sealed class GroupOperator(Operator inner, GroupKeySpec[] keys, List<AggregateSpec> aggregates) : Operator
{
    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        Dictionary<TermRef[], Accumulator[]> groups = new(new KeyComparer(exec));
        List<(TermRef[] Key, Accumulator[] State)> order = [];
        using (IEnumerator<ulong[]> solutions = inner.Open(exec, input, graph))
        {
            while (solutions.MoveNext())
            {
                exec.Check();
                ulong[] row = solutions.Current;
                TermRef[] key = new TermRef[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    Value value = keys[i].Expression.Eval(exec, row, graph);
                    key[i] = value.IsError ? TermRef.Unbound : Semantics.ToRef(exec, value);
                }

                if (!groups.TryGetValue(key, out Accumulator[]? state))
                {
                    state = Create(exec);
                    groups.Add(key, state);
                    order.Add((key, state));
                }

                for (int i = 0; i < state.Length; i++)
                {
                    state[i].Add(exec, row, graph);
                }
            }
        }

        if (order.Count == 0 && keys.Length == 0)
        {
            order.Add(([], Create(exec)));
        }

        foreach ((TermRef[] key, Accumulator[] state) in order)
        {
            ulong[] output = exec.NewRow();
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Slot >= 0 && key[i].IsBound)
                {
                    Rows.Set(output, exec.Width, keys[i].Slot, key[i]);
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

    private Accumulator[] Create(Exec exec)
    {
        Accumulator[] state = new Accumulator[aggregates.Count];
        for (int i = 0; i < state.Length; i++)
        {
            state[i] = new Accumulator(exec, aggregates[i]);
        }

        return state;
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
        }

        internal void Add(Exec exec, ulong[] row, ActiveGraph graph)
        {
            if (_spec.Argument is null)
            {
                // COUNT(*): solutions, or distinct solutions.
                if (_distinctRows is null || _distinctRows.Add(row))
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

            if (_distinct is not null && !_distinct.Add(Semantics.ToRef(exec, value)))
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
                        _text.AddRange(Encoding.UTF8.GetBytes(_spec.Separator));
                    }

                    _text.AddRange(term.Lexical);
                    _any = true;
                    break;
                case AggregateFunction.Custom:
                    _custom!.Add(Semantics.AsTerm(exec, value)!);
                    break;
            }
        }

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
                    return _failed ? Value.Error : Value.Of(RdfTerm.Literal(_text.ToArray()));
                default:
                    return _custom!.TryGetResult(out RdfTerm? result) ? Value.Of(result) : Value.Error;
            }
        }
    }
}
