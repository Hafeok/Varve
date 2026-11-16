// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// How many facts the closures sharing it may hold together (ADR 0195 point
/// 6): a binding's memory limit, in facts. A closure that would pass it
/// throws <see cref="ClosureLimitExceededException"/> before it adds the fact,
/// and is then in no state to serve: its owner drops it.
/// </summary>
[Contract(typeof(TheInferenceProjection.ClosureMemoryLimit), Role = "a binding's closures' share of memory, in facts")]
public sealed class FactBudget
{
    private long _used;

    /// <summary>A budget of <paramref name="limit"/> facts.</summary>
    public FactBudget(QuadCount limit) => Limit = limit;

    /// <summary>The most facts the closures may hold together.</summary>
    public QuadCount Limit { get; }

    /// <summary>The facts they hold now, explicit, axioms and derived alike.</summary>
    public QuadCount Used => new(Interlocked.Read(ref _used));

    internal void Take()
    {
        if (Interlocked.Increment(ref _used) > Limit.Value)
        {
            Interlocked.Decrement(ref _used);
            throw new ClosureLimitExceededException(Limit);
        }
    }

    internal void Give() => Interlocked.Decrement(ref _used);

    internal void GiveAll(QuadCount facts) => Interlocked.Add(ref _used, -facts.Value);
}

/// <summary>A closure would hold more facts than its <see cref="FactBudget"/> allows (ADR 0195 point 6).</summary>
public sealed class ClosureLimitExceededException : Exception
{
    /// <summary>The refusal at <paramref name="limit"/>.</summary>
    public ClosureLimitExceededException(QuadCount limit)
        : base($"The closure would hold more than {limit.Value} facts, its binding's limit (ADR 0195 point 6).") => Limit = limit;

    /// <summary>The limit it would have passed.</summary>
    public QuadCount Limit { get; }
}
