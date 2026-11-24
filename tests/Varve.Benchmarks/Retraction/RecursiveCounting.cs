// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Varve.Inference;
using Varve.Inference.Model;

namespace Varve.Benchmarks.Candidates;

/// <summary>
/// Recursive counting, a retraction candidate ADR 0196 measured and did not
/// choose, kept here so that the measurement can be rerun (Motik,
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
/// <para>
/// Each closure's rounds and counts are kept here, beside its facts, keyed
/// by the fact: the engine's fact record carries none since DRed was chosen.
/// </para>
/// </remarks>
internal sealed class RecursiveCounting : Retraction
{
    private readonly ConditionalWeakTable<Closure, Dictionary<Fact, (int Round, int Count)>> _standings = new();

    /// <summary>The one instance; its state is per closure.</summary>
    internal static RecursiveCounting Instance { get; } = new();

    internal override void Insert(Closure closure, IEnumerable<Fact> facts) => CountingInsert(closure, facts);

    internal override void Retract(Closure closure, List<Fact> gone) => CountingRetract(closure, gone);

    private Dictionary<Fact, (int Round, int Count)> Of(Closure closure) => _standings.GetValue(closure, static _ => []);

    private bool TryStanding(Closure closure, in Fact fact, out int round, out int count)
    {
        if (closure.Facts.Holds(fact) && Of(closure).TryGetValue(fact, out (int Round, int Count) standing))
        {
            (round, count) = standing;
            return true;
        }

        round = count = 0;
        return closure.Facts.Holds(fact);
    }

    private void SetStanding(Closure closure, in Fact fact, int round, int count) => Of(closure)[fact] = (round, count);

    private int AddToCount(Closure closure, in Fact fact, int delta)
    {
        Dictionary<Fact, (int Round, int Count)> standings = Of(closure);
        standings.TryGetValue(fact, out (int Round, int Count) standing);
        standing.Count += delta;
        standings[fact] = standing;
        return standing.Count;
    }

    private void Remove(Closure closure, in Fact fact)
    {
        closure.Facts.Remove(fact);
        Of(closure).Remove(fact);
    }

    /// <summary>The round of a fact the closure holds under recursive counting, and the instances counted towards it there: for the tests' invariant.</summary>
    internal (int Round, int Count) Standing(Closure closure, in Fact fact) =>
        TryStanding(closure, fact, out int round, out int count) ? (round, count) : (0, 0);

    /// <summary>The earliest round any instance over the facts held gives <paramref name="head"/>, and how many instances give it that round.</summary>
    internal (int Round, int Count) Earliest(Closure closure, in Fact head)
    {
        int best = int.MaxValue, count = 0;
        List<Fact[]> instances = [];
        foreach (Rule rule in closure.Program.Rules)
        {
            instances.Clear();
            rule.Instances(head, closure.Facts, instances);
            HashSet<InstanceKey> seen = [];
            foreach (Fact[] premises in instances)
            {
                if (!seen.Add(new InstanceKey(rule, head, premises)))
                {
                    continue;
                }

                int round = RoundAfter(closure, premises);
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
    private int RoundOf(Closure closure, in Fact fact) => TryStanding(closure, fact, out int round, out _) ? round : 0;

    private void CountingInsert(Closure closure, IEnumerable<Fact> givens)
    {
        using Closure.Working working = new(closure.Facts.Budget);
        PriorityQueue<Fact, int> queue = new();
        foreach (Fact fact in givens)
        {
            if (!TryStanding(closure, fact, out int round, out _) || round > 0)
            {
                working.Take();
                queue.Enqueue(fact, 0);
            }
        }

        Settle(closure, queue, working);
    }

    private void CountingRetract(Closure closure, List<Fact> gone)
    {
        // Phase 1, in increasing round: delete what has no instance left in
        // its round, uncounting what it was a premise of.
        using Closure.Working working = new(closure.Facts.Budget);
        PriorityQueue<Fact, int> queue = new();
        foreach (Fact fact in gone)
        {
            if (closure.Facts.Contains(fact) && !closure.IsGiven(fact))
            {
                working.Take();
                queue.Enqueue(fact, RoundOf(closure, fact));
            }
        }

        List<Fact> deleted = [];
        while (queue.TryDequeue(out Fact fact, out _))
        {
            working.Give();
            if (!closure.Facts.Contains(fact) || closure.IsGiven(fact) || (TryStanding(closure, fact, out int round, out int count) && round > 0 && count > 0))
            {
                continue;
            }

            foreach ((Fact head, Fact[] premises) in Derivations(closure, fact))
            {
                if (head == fact || !closure.Facts.Contains(head) || closure.IsGiven(head) || RoundAfter(closure, premises) != RoundOf(closure, head))
                {
                    continue;
                }

                if (AddToCount(closure, head, -1) == 0)
                {
                    working.Take();
                    queue.Enqueue(head, RoundOf(closure, head));
                }
            }

            working.Take();
            Remove(closure, fact);
            deleted.Add(fact);
        }

        // Phase 2: put back what is still derived, in the round it now has.
        foreach (Fact fact in deleted)
        {
            (int round, int count) = Earliest(closure, fact);
            if (count > 0)
            {
                working.Take();
                queue.Enqueue(fact, round);
            }
        }

        Settle(closure, queue, working);
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
    private void Settle(Closure closure, PriorityQueue<Fact, int> queue, Closure.Working working)
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
            bool present = closure.Facts.Contains(fact);
            if (present && RoundOf(closure, fact) <= round)
            {
                continue;
            }

            int? before = present ? RoundOf(closure, fact) : null;
            if (!present)
            {
                closure.Facts.Add(fact);
            }

            // The round first: an instance with the fact among its own premises reads it.
            SetStanding(closure, fact, round, 0);
            SetStanding(closure, fact, round, closure.IsGiven(fact) ? 0
                : !present && !seeded.Contains(fact) && Settled(found, fact, working, out (int Round, int Count) earliest) && earliest.Round == round ? earliest.Count
                : Earliest(closure, fact).Count);
            foreach ((Fact head, Fact[] premises) in Derivations(closure, fact))
            {
                int now = RoundAfter(closure, premises);
                bool again = before is not null && RoundAfter(closure, premises, fact, before.Value) == now;
                if (!closure.Facts.Contains(head))
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

                if (now < RoundOf(closure, head))
                {
                    working.Take();
                    queue.Enqueue(head, now);
                    continue;
                }

                // The instance reaches its head's round only now: count it.
                if (now == RoundOf(closure, head) && !closure.IsGiven(head) && head != fact && !again)
                {
                    AddToCount(closure, head, 1);
                }
            }
        }
    }

    /// <summary>Takes a settled fact's frontier entry out, giving its charge back.</summary>
    private static bool Settled(Dictionary<Fact, (int Round, int Count)> found, in Fact fact, Closure.Working working, out (int Round, int Count) earliest)
    {
        if (!found.Remove(fact, out earliest))
        {
            return false;
        }

        working.Give();
        return true;
    }

    /// <summary>Every instance with <paramref name="anchor"/> among its premises, each rule's premise set once.</summary>
    private static List<(Fact Head, Fact[] Premises)> Derivations(Closure closure, in Fact anchor)
    {
        List<(Fact, Fact[])> all = [];
        List<Instance> instances = [];
        foreach (Rule rule in closure.Program.Rules)
        {
            instances.Clear();
            rule.Derivations(anchor, closure.Facts, instances);
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
    private int RoundAfter(Closure closure, Fact[] premises)
    {
        int latest = -1;
        foreach (Fact premise in premises)
        {
            latest = Math.Max(latest, RoundOf(closure, premise));
        }

        return latest + 1;
    }

    /// <summary>The round an instance gave its head while <paramref name="changed"/> was in round <paramref name="was"/>.</summary>
    private int RoundAfter(Closure closure, Fact[] premises, in Fact changed, int was)
    {
        int latest = -1;
        foreach (Fact premise in premises)
        {
            latest = Math.Max(latest, premise == changed ? was : RoundOf(closure, premise));
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
