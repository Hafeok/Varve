// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;

namespace Varve.Inference;

/// <summary>
/// The bytes the closures sharing it may hold together (ADR 0195 point 6, as
/// amended 2026-11-18): a binding's memory limit. Every structure a closure
/// grows asks it first: a fact at <see cref="ClosureCosts.PerFact"/>, the fact
/// store's arrays at their new size before they are allocated, and a build's
/// working sets per entry. A request that would pass the limit throws
/// <see cref="ClosureLimitExceededException"/> on the asking thread, before
/// the allocation it stands for; the closure is then in no state to serve,
/// and its owner drops it, which gives back everything it took.
/// </summary>
/// <remarks>
/// The refusal is the budget's, never an <see cref="OutOfMemoryException"/>
/// caught: the heap is never asked for what the budget has not granted, so a
/// limit the heap can hold is never outrun (#335).
/// </remarks>
[Contract(typeof(TheInferenceProjection.ClosureMemoryLimit), Role = "a binding's closures' share of memory, in bytes, asked before every allocation")]
public sealed class FactBudget
{
    private long _used;
    private long _peak;

    /// <summary>A budget of <paramref name="limit"/>, its closures' structures charged at <paramref name="costs"/>.</summary>
    public FactBudget(MemoryAmount limit, ClosureCosts costs)
        : this(limit, costs, pool: null)
    {
    }

    /// <summary>
    /// A budget of <paramref name="limit"/> that also takes everything it
    /// takes from <paramref name="pool"/>, a budget shared by every binding
    /// of a process: a request is refused when either would pass its limit,
    /// so the bindings together never ask the heap for more than the pool.
    /// </summary>
    public FactBudget(MemoryAmount limit, ClosureCosts costs, FactBudget? pool)
    {
        ArgumentNullException.ThrowIfNull(costs);
        Limit = limit;
        Costs = costs;
        Pool = pool;
    }

    /// <summary>The budget shared by every binding of the process, if there is one.</summary>
    public FactBudget? Pool { get; }

    /// <summary>The most the closures may hold together.</summary>
    public MemoryAmount Limit { get; }

    /// <summary>What each structure is charged.</summary>
    public ClosureCosts Costs { get; }

    /// <summary>What the closures hold now, by the budget's count.</summary>
    public MemoryAmount Used => new(Interlocked.Read(ref _used));

    /// <summary>The most they have held at once since the budget was made: what a build needs, by the budget's count.</summary>
    public MemoryAmount Peak => new(Interlocked.Read(ref _peak));

    /// <summary>
    /// Admission: refuses a build handed <paramref name="given"/> facts that
    /// the budget could not hold even before it derives anything, each held
    /// and explicit, before the build allocates for any of them.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">The given facts alone would pass the limit.</exception>
    public void Admit(Varve.Rdf.QuadCount given)
    {
        long wanted = Interlocked.Read(ref _used) + (given.Value * (Costs.PerFact.Value + Costs.PerExplicit.Value));
        if (wanted > Limit.Value)
        {
            throw new ClosureLimitExceededException(Limit, new MemoryAmount(wanted));
        }
    }

    /// <summary>Takes <paramref name="amount"/> before it is allocated, for a structure beside the closures that the binding holds while it builds them.</summary>
    /// <exception cref="ClosureLimitExceededException">The closures would hold more than <see cref="Limit"/>.</exception>
    public void TakeBeside(MemoryAmount amount) => Take(amount);

    /// <summary>Gives back what <see cref="TakeBeside"/> took.</summary>
    public void GiveBeside(MemoryAmount amount) => Give(amount);

    /// <summary>Takes <paramref name="amount"/> before it is allocated; past the limit, throws and takes nothing.</summary>
    /// <exception cref="ClosureLimitExceededException">The closures would hold more than <see cref="Limit"/>.</exception>
    internal void Take(MemoryAmount amount)
    {
        long bytes = amount.Value;
        long now = Interlocked.Add(ref _used, bytes);
        if (now > Limit.Value)
        {
            Interlocked.Add(ref _used, -bytes);
            throw new ClosureLimitExceededException(Limit, new MemoryAmount(now));
        }

        if (Pool is { } pool)
        {
            try
            {
                pool.Take(amount);
            }
            catch (ClosureLimitExceededException)
            {
                // The process's bindings together are at the pool's limit: refused as this binding's own would be.
                Interlocked.Add(ref _used, -bytes);
                throw new ClosureLimitExceededException(Limit, new MemoryAmount(now));
            }
        }

        long peak = Interlocked.Read(ref _peak);
        while (now > peak)
        {
            long seen = Interlocked.CompareExchange(ref _peak, now, peak);
            if (seen == peak)
            {
                break;
            }

            peak = seen;
        }
    }

    /// <summary>Gives back <paramref name="amount"/> taken before.</summary>
    internal void Give(MemoryAmount amount)
    {
        Interlocked.Add(ref _used, -amount.Value);
        Pool?.Give(amount);
    }
}

/// <summary>A closure's structures would pass its <see cref="FactBudget"/> (ADR 0195 point 6).</summary>
public sealed class ClosureLimitExceededException : Exception
{
    /// <summary>The refusal at <paramref name="limit"/>, a request having asked for <paramref name="wanted"/> in all.</summary>
    public ClosureLimitExceededException(MemoryAmount limit, MemoryAmount wanted)
        : base($"The closure would hold {wanted.Value} bytes, past its binding's limit of {limit.Value} (ADR 0195 point 6).")
    {
        Limit = limit;
        Wanted = wanted;
    }

    /// <summary>The limit it would have passed.</summary>
    public MemoryAmount Limit { get; }

    /// <summary>What the closures would have held, the refused request included: a lower bound on what they need.</summary>
    public MemoryAmount Wanted { get; }
}
