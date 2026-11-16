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
/// The minimal premise sets of a cross-graph closure's facts (ADR 0190 point
/// 6): for each fact, the sets of data graphs its derivations draw on, none a
/// proper subset of another. A data graph is an index the owner assigns; the
/// empty set is a fact drawn from the TBox and the axioms alone.
/// </summary>
/// <remarks>
/// <para>
/// The closure over the data graphs' merge is computed first, by
/// <see cref="Closure"/>. The sets are then computed over it as pairs of a
/// fact and a set: an explicit fact has the set of each graph that holds
/// it, an axiom and a fact of the TBox closure beneath the empty set, and a
/// rule's instance the union of its premises' sets, one taken from each.
/// A pair is kept only when no kept pair of the same fact has a subset of
/// its set, and a pair kept drops those it absorbs: the positive Boolean
/// provenance semiring over graphs. The sets are subsets of the data
/// graphs, so the computation ends.
/// </para>
/// <para>
/// A premise used twice in one instance takes the same set both times: a
/// union of two of its sets is never smaller than either.
/// </para>
/// </remarks>
[Contract(typeof(TheInferenceProjection.PlacedByPremiseSets), Role = "each fact's minimal premise sets, over a cross-graph closure")]
public sealed class PremiseSets
{
    private const int Empty = 0;

    private readonly Closure _closure;
    private readonly List<int[]> _sets = [[]];
    private readonly List<PremiseSet?> _views = [];
    private readonly Dictionary<SetKey, int> _ids = [];

    // A fact's sets: a set's number, or the bitwise complement of an entry in _antichains when it has more than one.
    private readonly Dictionary<Fact, int> _labels = [];
    private readonly List<List<int>?> _antichains = [];
    private readonly Stack<int> _freeAntichains = [];

    /// <summary>
    /// The minimal premise sets of every fact of <paramref name="closure"/>,
    /// the closure of the data graphs' merge, each explicit fact held by the
    /// graphs <paramref name="explicitIn"/> names for it.
    /// </summary>
    public PremiseSets(Closure closure, IEnumerable<(Fact Fact, DataGraphIndex Graph)> explicitIn)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentNullException.ThrowIfNull(explicitIn);
        _closure = closure;
        _ids[new SetKey([])] = Empty;

        Queue<(Fact Fact, int Set)> work = new();
        foreach (Fact axiom in closure.Program.Axioms)
        {
            Offer(axiom, Empty, work);
        }

        foreach ((Fact fact, DataGraphIndex graph) in explicitIn)
        {
            if (graph.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(explicitIn), graph.Value, "A data graph's index is not negative.");
            }

            Offer(fact, Intern([graph.Value]), work);
        }

        Propagate(work);
    }

    /// <summary>The facts with sets: every fact of the closure the data graphs' merge draws on, the TBox closure beneath excluded.</summary>
    public QuadCount Count => new(_labels.Count);

    /// <summary>
    /// A fact's minimal premise sets; a single empty set for a fact of the TBox closure beneath or an axiom,
    /// and none for a fact the closure does not hold.
    /// </summary>
    public IReadOnlyList<PremiseSet> Of(Fact fact)
    {
        if (_closure.Beneath is { } tbox && tbox.Contains(fact))
        {
            return [View(Empty)];
        }

        if (!_labels.TryGetValue(fact, out int label))
        {
            return [];
        }

        if (label >= 0)
        {
            return [View(label)];
        }

        List<PremiseSet> sets = [];
        foreach (int set in _antichains[~label]!)
        {
            sets.Add(View(set));
        }

        return sets;
    }

    /// <summary>
    /// The distinct premise sets of two or more graphs that some fact has as
    /// a minimal set: the reserved graphs a cross-graph binding places rows
    /// in (ADR 0190 point 6), reported on readiness as <c>premiseSets</c>.
    /// </summary>
    public IReadOnlyCollection<PremiseSet> CrossGraph()
    {
        HashSet<int> used = [];
        foreach (int label in _labels.Values)
        {
            if (label >= 0)
            {
                used.Add(label);
                continue;
            }

            foreach (int set in _antichains[~label]!)
            {
                used.Add(set);
            }
        }

        List<PremiseSet> sets = [];
        foreach (int set in used)
        {
            if (_sets[set].Length >= 2)
            {
                sets.Add(View(set));
            }
        }

        return sets;
    }

    private void Propagate(Queue<(Fact Fact, int Set)> work)
    {
        List<Instance> instances = [];
        List<int> products = [];
        while (work.TryDequeue(out (Fact Fact, int Set) item))
        {
            if (!Has(item.Fact, item.Set))
            {
                // Absorbed since it was offered: what it would give, the set that absorbed it gives with less.
                continue;
            }

            foreach (Rule rule in _closure.Program.Rules)
            {
                instances.Clear();
                rule.Derivations(item.Fact, _closure.Facts, instances);
                foreach (Instance instance in instances)
                {
                    if (IsBeneath(instance.Head))
                    {
                        continue;
                    }

                    products.Clear();
                    Product(instance.Premises, 0, item.Fact, _sets[item.Set], products);
                    foreach (int set in products)
                    {
                        Offer(instance.Head, set, work);
                    }
                }
            }
        }
    }

    /// <summary>Every union of one set from each premise, <paramref name="anchor"/> taking <paramref name="sofar"/>'s part already.</summary>
    private void Product(Fact[] premises, int at, in Fact anchor, int[] sofar, List<int> into)
    {
        if (at == premises.Length)
        {
            into.Add(Intern(sofar));
            return;
        }

        Fact premise = premises[at];
        if (premise == anchor || IsBeneath(premise))
        {
            Product(premises, at + 1, anchor, sofar, into);
            return;
        }

        if (!_labels.TryGetValue(premise, out int label))
        {
            // A premise with no set yet: the instance gives nothing until it has one, and is found again then.
            return;
        }

        if (label >= 0)
        {
            Product(premises, at + 1, anchor, Union(sofar, _sets[label]), into);
            return;
        }

        foreach (int set in _antichains[~label]!.ToArray())
        {
            Product(premises, at + 1, anchor, Union(sofar, _sets[set]), into);
        }
    }

    private PremiseSet View(int set)
    {
        while (_views.Count <= set)
        {
            _views.Add(null);
        }

        return _views[set] ??= new PremiseSet(_sets[set]);
    }

    private bool IsBeneath(in Fact fact) => _closure.Beneath is { } tbox && tbox.Contains(fact);

    private bool Has(in Fact fact, int set)
    {
        if (!_labels.TryGetValue(fact, out int label))
        {
            return false;
        }

        return label >= 0 ? label == set : _antichains[~label]!.Contains(set);
    }

    /// <summary>Keeps (fact, set) unless a set the fact has is a subset of it, dropping the sets it absorbs; queues it when kept.</summary>
    private void Offer(in Fact fact, int set, Queue<(Fact Fact, int Set)> work)
    {
        if (IsBeneath(fact))
        {
            return;
        }

        if (!_labels.TryGetValue(fact, out int label))
        {
            _labels[fact] = set;
            work.Enqueue((fact, set));
            return;
        }

        int[] offered = _sets[set];
        if (label >= 0)
        {
            if (Subset(_sets[label], offered))
            {
                return;
            }

            if (Subset(offered, _sets[label]))
            {
                _labels[fact] = set;
            }
            else
            {
                int slot = _freeAntichains.Count > 0 ? _freeAntichains.Pop() : Grow();
                _antichains[slot] = [label, set];
                _labels[fact] = ~slot;
            }

            work.Enqueue((fact, set));
            return;
        }

        List<int> antichain = _antichains[~label]!;
        foreach (int held in antichain)
        {
            if (Subset(_sets[held], offered))
            {
                return;
            }
        }

        antichain.RemoveAll(held => Subset(offered, _sets[held]));
        antichain.Add(set);
        if (antichain.Count == 1)
        {
            _antichains[~label] = null;
            _freeAntichains.Push(~label);
            _labels[fact] = set;
        }

        work.Enqueue((fact, set));
    }

    private int Grow()
    {
        _antichains.Add(null);
        return _antichains.Count - 1;
    }

    private int Intern(int[] set)
    {
        SetKey key = new(set);
        if (!_ids.TryGetValue(key, out int id))
        {
            id = _sets.Count;
            _sets.Add(set);
            _ids[key] = id;
        }

        return id;
    }

    private static int[] Union(int[] a, int[] b)
    {
        if (b.Length == 0 || ReferenceEquals(a, b))
        {
            return a;
        }

        if (a.Length == 0)
        {
            return b;
        }

        List<int> union = new(a.Length + b.Length);
        int i = 0, j = 0;
        while (i < a.Length || j < b.Length)
        {
            if (j == b.Length || (i < a.Length && a[i] < b[j]))
            {
                union.Add(a[i++]);
            }
            else if (i == a.Length || b[j] < a[i])
            {
                union.Add(b[j++]);
            }
            else
            {
                union.Add(a[i++]);
                j++;
            }
        }

        return [.. union];
    }

    /// <summary>Whether sorted <paramref name="small"/> is a subset of sorted <paramref name="large"/>.</summary>
    private static bool Subset(int[] small, int[] large)
    {
        int j = 0;
        foreach (int item in small)
        {
            while (j < large.Length && large[j] < item)
            {
                j++;
            }

            if (j == large.Length || large[j] != item)
            {
                return false;
            }

            j++;
        }

        return true;
    }

    /// <summary>A set of graph indices as a dictionary key, by its members.</summary>
    private readonly struct SetKey(int[] members) : IEquatable<SetKey>
    {
        private readonly int[] _members = members;

        public bool Equals(SetKey other) => _members.AsSpan().SequenceEqual(other._members);

        public override bool Equals(object? obj) => obj is SetKey other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = default;
            foreach (int member in _members)
            {
                hash.Add(member);
            }

            return hash.ToHashCode();
        }
    }
}
