// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;
using Varve.Sparql.Evaluation.Operators;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Expressions;

/// <summary>
/// A compiled expression: evaluated against one solution, in the active graph
/// (which only <c>EXISTS</c> reads).
/// </summary>
internal abstract class Expr
{
    internal abstract Value Eval(Exec exec, ulong[] row, in ActiveGraph graph);
}

/// <summary>A variable's value in the solution; unbound is an error (§17.2).</summary>
internal sealed class SlotExpr(int slot) : Expr
{
    internal int Slot { get; } = slot;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph) => Value.Of(Rows.Get(row, exec.Width, Slot));
}

/// <summary>
/// A constant, resolved once per execution (§5.3): interned through the
/// source, and when it is a number, its value parsed once.
/// </summary>
internal sealed class ConstExpr(RdfTerm term) : Expr
{
    private Value _value;
    private Exec? _for;

    internal RdfTerm Term { get; } = term;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        if (!ReferenceEquals(_for, exec))
        {
            Resolve(exec);
        }

        return _value;
    }

    private void Resolve(Exec exec)
    {
        TermRef reference = exec.Intern(Term);
        _value = Terms.TryNumeric(Term, out XsdNumeric number) ? Value.Of(reference, number) : Value.Of(reference);
        _for = exec;
    }
}

/// <summary><c>||</c> and <c>&amp;&amp;</c>, with §17.2's truth table for errors.</summary>
internal sealed class LogicExpr(bool and, Expr left, Expr right) : Expr
{
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        bool? l = Semantics.Ebv(exec, left.Eval(exec, row, graph));
        if (and && l == false)
        {
            return Value.Of(false);
        }

        if (!and && l == true)
        {
            return Value.Of(true);
        }

        bool? r = Semantics.Ebv(exec, right.Eval(exec, row, graph));
        if (and)
        {
            return r == false ? Value.Of(false) : l is null || r is null ? Value.Error : Value.Of(true);
        }

        return r == true ? Value.Of(true) : l is null || r is null ? Value.Error : Value.Of(false);
    }
}

/// <summary><c>!</c>: <c>fn:not</c> over the effective boolean value.</summary>
internal sealed class NotExpr(Expr operand) : Expr
{
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        bool? value = Semantics.Ebv(exec, operand.Eval(exec, row, graph));
        return value is null ? Value.Error : Value.Of(!value.Value);
    }
}

/// <summary>Unary <c>+</c> and <c>-</c> over numerics.</summary>
internal sealed class SignExpr(bool negate, Expr operand) : Expr
{
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        Value value = operand.Eval(exec, row, graph);
        if (!Semantics.TryNumeric(exec, value, out XsdNumeric number))
        {
            return Value.Error;
        }

        if (!negate)
        {
            return Value.Of(number);
        }

        return XsdNumeric.TryNegate(number, out XsdNumeric negated) ? Value.Of(negated) : Value.Error;
    }
}

/// <summary>The six comparisons of §17.3.</summary>
internal sealed class CompareExpr(BinaryOperator op, Expr left, Expr right) : Expr
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        Value l = left.Eval(exec, row, graph);
        Value r = right.Eval(exec, row, graph);
        if (op is BinaryOperator.Equal or BinaryOperator.NotEqual)
        {
            bool? equal = Semantics.Equal(exec, l, r);
            return equal is null ? Value.Error : Value.Of(equal.Value == (op == BinaryOperator.Equal));
        }

        if (!Semantics.TryCompare(exec, l, r, out PartialOrdering order))
        {
            return Value.Error;
        }

        return Value.Of(op switch
        {
            BinaryOperator.Less => order == PartialOrdering.Less,
            BinaryOperator.Greater => order == PartialOrdering.Greater,
            BinaryOperator.LessOrEqual => order is PartialOrdering.Less or PartialOrdering.Equal,
            _ => order is PartialOrdering.Greater or PartialOrdering.Equal,
        });
    }
}

/// <summary>The four arithmetic operators of §17.3, promoting per XPath.</summary>
internal sealed class ArithmeticExpr(BinaryOperator op, Expr left, Expr right) : Expr
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        if (!Semantics.TryNumeric(exec, left.Eval(exec, row, graph), out XsdNumeric a)
            || !Semantics.TryNumeric(exec, right.Eval(exec, row, graph), out XsdNumeric b))
        {
            return Value.Error;
        }

        bool ok;
        XsdNumeric result;
        switch (op)
        {
            case BinaryOperator.Add:
                ok = XsdNumeric.TryAdd(a, b, out result);
                break;
            case BinaryOperator.Subtract:
                ok = XsdNumeric.TrySubtract(a, b, out result);
                break;
            case BinaryOperator.Multiply:
                ok = XsdNumeric.TryMultiply(a, b, out result);
                break;
            default:
                ok = XsdNumeric.TryDivide(a, b, out result);
                break;
        }

        return ok ? Value.Of(result) : Value.Error;
    }
}

/// <summary><c>EXISTS</c> and <c>NOT EXISTS</c> (§17.4.1.4): the pattern evaluated given the solution (§6.13).</summary>
internal sealed class ExistsExpr(Operator pattern, bool negated) : Expr
{
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        using IEnumerator<ulong[]> solutions = pattern.Open(exec, row, graph);
        bool any = solutions.MoveNext();
        return Value.Of(any != negated);
    }
}

/// <summary>A call to an extension function (§17.6, ADR 0056).</summary>
internal sealed class ExtensionExpr(IExtensionFunction function, Expr[] arguments) : Expr
{
    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph)
    {
        RdfTerm[] terms = new RdfTerm[arguments.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            RdfTerm? term = Semantics.AsTerm(exec, arguments[i].Eval(exec, row, graph));
            if (term is null)
            {
                return Value.Error;
            }

            terms[i] = term;
        }

        return function.TryEvaluate(terms, out RdfTerm? result) ? Value.Of(result) : Value.Error;
    }
}

/// <summary>An expression that is always an error: an unknown function, per §17.2.1.</summary>
internal sealed class ErrorExpr : Expr
{
    internal static ErrorExpr Instance { get; } = new();

    internal override Value Eval(Exec exec, ulong[] row, in ActiveGraph graph) => Value.Error;
}
