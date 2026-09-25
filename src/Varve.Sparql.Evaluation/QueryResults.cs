// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation;

/// <summary>What a query form answers.</summary>
public enum QueryResultKind : byte
{
    /// <summary>A <c>SELECT</c>: a sequence of solutions.</summary>
    Solutions,

    /// <summary>An <c>ASK</c>: a boolean.</summary>
    Boolean,

    /// <summary>A <c>CONSTRUCT</c> or <c>DESCRIBE</c>: triples.</summary>
    Triples,
}

/// <summary>
/// The answer to one query execution. Disposing it stops the evaluation and
/// releases every cursor it opened; it is the signal that the caller may
/// release the source (ADR 0052).
/// </summary>
public abstract class QueryResults : IDisposable
{
    private protected QueryResults()
    {
    }

    /// <summary>Which of the three this is.</summary>
    public abstract QueryResultKind Kind { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the evaluation's cursors.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>
/// The solutions of a <c>SELECT</c>, streamed: <see cref="MoveNext"/>, then per
/// column <see cref="TryGetHandle"/> — the source's own handle, no allocation —
/// or <see cref="TryGetTerm"/>, which externalises.
/// </summary>
public sealed class SolutionResults : QueryResults
{
    private readonly Exec _exec;
    private readonly IEnumerator<ulong[]> _solutions;
    private readonly int[] _slots;
    private ulong[]? _current;
    private bool _disposed;

    internal SolutionResults(Exec exec, IEnumerator<ulong[]> solutions, Variable[] variables, int[] slots)
    {
        _exec = exec;
        _solutions = solutions;
        Variables = variables;
        _slots = slots;
    }

    /// <inheritdoc />
    public override QueryResultKind Kind => QueryResultKind.Solutions;

    /// <summary>The projected variables, the columns, in the query's order.</summary>
    public IReadOnlyList<Variable> Variables { get; }

    /// <summary>Advances to the next solution; false at the end.</summary>
    /// <exception cref="OperationCanceledException">The evaluation's token was cancelled.</exception>
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _exec.Check();
        if (_solutions.MoveNext())
        {
            _current = _solutions.Current;
            return true;
        }

        _current = null;
        return false;
    }

    /// <summary>Whether a column is bound in the current solution.</summary>
    public bool IsBound(int column) => CurrentRow()[_slots[column]] != 0;

    /// <summary>
    /// The source's handle for a column: false when it is unbound, and when it
    /// holds a term the source does not have (a computed literal, a minted blank
    /// node, a constant the source has never seen).
    /// </summary>
    public bool TryGetHandle(int column, out TermHandle handle)
    {
        TermRef value = Rows.Get(CurrentRow(), _exec.Width, _slots[column]);
        if (!value.IsBound)
        {
            handle = default;
            return false;
        }

        return _exec.TryGetSourceHandle(value, out handle);
    }

    /// <summary>The term of a column, or false when it is unbound.</summary>
    public bool TryGetTerm(int column, [NotNullWhen(true)] out RdfTerm? term)
    {
        TermRef value = Rows.Get(CurrentRow(), _exec.Width, _slots[column]);
        term = value.IsBound ? _exec.Materialise(value) : null;
        return term is not null;
    }

    private ulong[] CurrentRow() => _current ?? throw new InvalidOperationException("No solution is current: call MoveNext, and read only while it returns true.");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            _solutions.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>The answer to an <c>ASK</c>.</summary>
public sealed class BooleanResult : QueryResults
{
    internal BooleanResult(bool value) => Value = value;

    /// <inheritdoc />
    public override QueryResultKind Kind => QueryResultKind.Boolean;

    /// <summary>Whether the pattern has a solution.</summary>
    public bool Value { get; }
}

/// <summary>The triples of a <c>CONSTRUCT</c> or <c>DESCRIBE</c>: a set, streamed.</summary>
public sealed class TripleResults : QueryResults
{
    private readonly IEnumerator<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> _triples;
    private bool _disposed;

    internal TripleResults(IEnumerator<(RdfTerm, RdfTerm, RdfTerm)> triples) => _triples = triples;

    /// <inheritdoc />
    public override QueryResultKind Kind => QueryResultKind.Triples;

    /// <summary>The current triple's subject.</summary>
    public RdfTerm Subject { get; private set; } = null!;

    /// <summary>The current triple's predicate.</summary>
    public RdfTerm Predicate { get; private set; } = null!;

    /// <summary>The current triple's object.</summary>
    public RdfTerm Object { get; private set; } = null!;

    /// <summary>Advances to the next triple; false at the end.</summary>
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_triples.MoveNext())
        {
            return false;
        }

        (Subject, Predicate, Object) = _triples.Current;
        return true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            _triples.Dispose();
        }

        base.Dispose(disposing);
    }
}
