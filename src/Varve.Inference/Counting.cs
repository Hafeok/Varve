// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Inference.Model;

namespace Varve.Inference;

/// <summary>
/// Recursive counting, the third retraction candidate (ADR 0196; Motik,
/// Nenov, Piro and Horrocks, "Maintenance of Datalog materialisations
/// revisited", AIJ 2019, §5).
/// </summary>
/// <remarks>
/// <para>
/// Each fact has a <strong>round</strong>, the round of the semi-naive
/// evaluation that first derives it: 0 for an explicit fact or an axiom, and
/// otherwise one more than the latest premise of the instance that derives it
/// earliest. Each derived fact has a <strong>count</strong>: the instances
/// that derive it in its round, each a rule and a set of premises, counted
/// once. A fact is kept while its count is above zero, since every instance
/// it counts has premises of earlier rounds, which are proved before it: no
/// count is ever held up by a cycle.
/// </para>
/// <para>
/// <strong>Retraction</strong> visits facts in increasing round. A fact
/// whose count reaches zero is deleted, and every instance it was a premise
/// of that counted towards its head is uncounted. Then each deleted fact
/// that some instance over what is left still derives is put back in the
/// round that instance gives it, earliest first, as insertion does.
/// <strong>Insertion</strong> settles facts in increasing round, Dijkstra's
/// order over the rounds, so that a fact's round is final when it settles.
/// </para>
/// </remarks>
public sealed partial class Closure
{
    /// <summary>The round of a fact the closure holds under recursive counting, and the instances counted towards it there: for the tests' invariant.</summary>
    internal (int Round, int Count) Standing(in Fact fact) =>
        Facts.TryStanding(fact, out int round, out int count) ? (round, count) : (0, 0);

    /// <summary>The earliest round any instance over the facts held gives <paramref name="head"/>, and how many instances give it that round.</summary>
    internal (int Round, int Count) Earliest(in Fact head)
    {
        int best = int.MaxValue, count = 0;
        List<Fact[]> instances = [];
        foreach (Rule rule in Program.Rules)
        {
            instances.Clear();
            rule.Instances(head, Facts, instances);
            HashSet<InstanceKey> seen = [];
            foreach (Fact[] premises in instances)
            {
                if (!seen.Add(new InstanceKey(rule, head, premises)))
                {
                    continue;
                }

                int round = RoundAfter(premises);
                if (round < best)
                {
                    best = round;
                    count = 1;
                }
                else if (round == best)
                {
                    count++;
                }
            }
        }

        return (best, count);
    }

    /// <summary>A fact's round: 0 for one of the TBox closure beneath, which is given here.</summary>
    private int RoundOf(in Fact fact) => Facts.TryStanding(fact, out int round, out _) ? round : 0;

    private void CountingInsert(IEnumerable<Fact> givens)
    {
        using Working working = new(Facts.Budget);
        PriorityQueue<Fact, int> queue = new();
        foreach (Fact fact in givens)
        {
            if (!Facts.TryStanding(fact, out int round, out _) || round > 0)
            {
                working.Take();
                queue.Enqueue(fact, 0);
            }
        }

        Settle(queue, working);
    }

    private void CountingRetract(List<Fact> gone)
    {
        // Phase 1, in increasing round: delete what has no instance left in
        // its round, uncounting what it was a premise of.
        using Working working = new(Facts.Budget);
        PriorityQueue<Fact, int> queue = new();
        foreach (Fact fact in gone)
        {
            if (Facts.Contains(fact) && !IsGiven(fact))
            {
                working.Take();
                queue.Enqueue(fact, RoundOf(fact));
            }
        }

        List<Fact> deleted = [];
        while (queue.TryDequeue(out Fact fact, out _))
        {
            working.Give();
            if (!Facts.Contains(fact) || IsGiven(fact) || (Facts.TryStanding(fact, out int round, out int count) && round > 0 && count > 0))
            {
                continue;
            }

            foreach ((Fact head, Fact[] premises) in Derivations(fact))
            {
                if (head == fact || !Facts.Contains(head) || IsGiven(head) || RoundAfter(premises) != RoundOf(head))
                {
                    continue;
                }

                if (Facts.AddToCount(head, -1) == 0)
                {
                    working.Take();
                    queue.Enqueue(head, RoundOf(head));
                }
            }

            working.Take();
            Facts.Remove(fact);
            deleted.Add(fact);
        }

        // Phase 2: put back what is still derived, in the round it now has.
        foreach (Fact fact in deleted)
        {
            (int round, int count) = Earliest(fact);
            if (count > 0)
            {
                working.Take();
                queue.Enqueue(fact, round);
            }
        }

        Settle(queue, working);
    }

    /// <summary>
    /// Settles facts in increasing round: a fact dequeued at a round earlier
    /// than its own, or absent, takes it and its count, and offers each
    /// instance it is a premise of to that instance's head.
    /// </summary>
    /// <remarks>
    /// A fact not yet held is counted forwards: an instance is found once,
    /// when the last of its premises settles, and an absent head keeps the
    /// earliest round found for it and how many instances gave it that round.
    /// A fact held already whose round drops is counted again from its
    /// instances, backwards; an instance found again whose round did not
    /// change is not counted twice. A fact seeded into the queue is counted
    /// backwards as well: instances of it may predate the pass.
    /// </remarks>
    private void Settle(PriorityQueue<Fact, int> queue, Working working)
    {
        // A fact seeded from outside, a retraction's put back, may have instances
        // found before this pass: it is counted backwards, never forwards.
        HashSet<Fact> seeded = [];
        foreach ((Fact fact, int _) in queue.UnorderedItems)
        {
            working.Take();
            seeded.Add(fact);
        }

        Dictionary<Fact, (int Round, int Count)> found = [];
        while (queue.TryDequeue(out Fact fact, out int round))
        {
            working.Give();
            bool present = Facts.Contains(fact);
            if (present && RoundOf(fact) <= round)
            {
                continue;
            }

            int? before = present ? RoundOf(fact) : null;
            if (!present)
            {
                Facts.Add(fact);
            }

            // The round first: an instance with the fact among its own premises reads it.
            Facts.SetStanding(fact, round, 0);
            Facts.SetStanding(fact, round, IsGiven(fact) ? 0
                : !present && !seeded.Contains(fact) && Settled(found, fact, working, out (int Round, int Count) earliest) && earliest.Round == round ? earliest.Count
                : Earliest(fact).Count);
            foreach ((Fact head, Fact[] premises) in Derivations(fact))
            {
                int now = RoundAfter(premises);
                bool again = before is not null && RoundAfter(premises, fact, before.Value) == now;
                if (!Facts.Contains(head))
                {
                    // Queued once per round it improves to, not once per instance found:
                    // a head of many instances would otherwise fill the queue with them.
                    bool known = found.TryGetValue(head, out (int Round, int Count) prior);
                    if (!again)
                    {
                        if (!known)
                        {
                            working.Take();
                        }

                        found[head] = known && prior.Round <= now
                            ? (prior.Round, prior.Count + (prior.Round == now ? 1 : 0))
                            : (now, 1);
                    }

                    if (!known || now < prior.Round || again)
                    {
                        working.Take();
                        queue.Enqueue(head, now);
                    }

                    continue;
                }

                if (now < RoundOf(head))
                {
                    working.Take();
                    queue.Enqueue(head, now);
                    continue;
                }

                // The instance reaches its head's round only now: count it.
                if (now == RoundOf(head) && !IsGiven(head) && head != fact && !again)
                {
                    Facts.AddToCount(head, 1);
                }
            }
        }
    }

    /// <summary>Takes a settled fact's frontier entry out, giving its charge back.</summary>
    private static bool Settled(Dictionary<Fact, (int Round, int Count)> found, in Fact fact, Working working, out (int Round, int Count) earliest)
    {
        if (!found.Remove(fact, out earliest))
        {
            return false;
        }

        working.Give();
        return true;
    }

    /// <summary>
    /// A pass's working sets' charge (ADR 0195 point 6): each entry of the frontier, the queue and the seeded set
    /// taken from the budget before it is stored, given back when it goes,
    /// and what is left given back when the pass ends, however it ends.
    /// </summary>
    private sealed class Working(FactBudget? budget) : IDisposable
    {
        private long _held;

        /// <exception cref="ClosureLimitExceededException">The entry would pass the budget.</exception>
        internal void Take()
        {
            if (budget is not null)
            {
                budget.Take(budget.Costs.PerWorkingEntry);
                _held += budget.Costs.PerWorkingEntry.Value;
            }
        }

        internal void Give()
        {
            if (budget is not null && _held > 0)
            {
                budget.Give(budget.Costs.PerWorkingEntry);
                _held -= budget.Costs.PerWorkingEntry.Value;
            }
        }

        public void Dispose()
        {
            budget?.Give(new MemoryAmount(_held));
            _held = 0;
        }
    }

    /// <summary>Every instance with <paramref name="anchor"/> among its premises, each rule's premise set once.</summary>
    private List<(Fact Head, Fact[] Premises)> Derivations(in Fact anchor)
    {
        List<(Fact, Fact[])> all = [];
        List<Instance> instances = [];
        foreach (Rule rule in Program.Rules)
        {
            instances.Clear();
            rule.Derivations(anchor, Facts, instances);
            HashSet<InstanceKey> seen = [];
            foreach (Instance instance in instances)
            {
                if (seen.Add(new InstanceKey(rule, instance.Head, instance.Premises)))
                {
                    all.Add((instance.Head, instance.Premises));
                }
            }
        }

        return all;
    }

    /// <summary>The round an instance gives its head: one after its latest premise.</summary>
    private int RoundAfter(Fact[] premises)
    {
        int latest = -1;
        foreach (Fact premise in premises)
        {
            latest = Math.Max(latest, RoundOf(premise));
        }

        return latest + 1;
    }

    /// <summary>The round an instance gave its head while <paramref name="changed"/> was in round <paramref name="was"/>.</summary>
    private int RoundAfter(Fact[] premises, in Fact changed, int was)
    {
        int latest = -1;
        foreach (Fact premise in premises)
        {
            latest = Math.Max(latest, premise == changed ? was : RoundOf(premise));
        }

        return latest + 1;
    }

    /// <summary>An instance as counting counts it: its rule, its head, and its premises as a set.</summary>
    private readonly struct InstanceKey : IEquatable<InstanceKey>
    {
        private readonly Rule _rule;
        private readonly Fact _head;
        private readonly Fact[] _premises;
        private readonly int _hash;

        internal InstanceKey(Rule rule, Fact head, Fact[] premises)
        {
            _rule = rule;
            _head = head;
            SortedSet<Fact> set = new(Comparer<Fact>.Create(Compare));
            set.UnionWith(premises);
            _premises = [.. set];
            HashCode hash = default;
            hash.Add(rule);
            hash.Add(head);
            foreach (Fact premise in _premises)
            {
                hash.Add(premise);
            }

            _hash = hash.ToHashCode();
        }

        public bool Equals(InstanceKey other) =>
            ReferenceEquals(_rule, other._rule) && _head == other._head && _premises.AsSpan().SequenceEqual(other._premises);

        public override bool Equals(object? obj) => obj is InstanceKey other && Equals(other);

        public override int GetHashCode() => _hash;

        private static int Compare(Fact x, Fact y)
        {
            int c = x.Subject.Value.CompareTo(y.Subject.Value);
            if (c == 0)
            {
                c = x.Predicate.Value.CompareTo(y.Predicate.Value);
            }

            return c != 0 ? c : x.Object.Value.CompareTo(y.Object.Value);
        }
    }
}
