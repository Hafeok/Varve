// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Inference.Model;

namespace Varve.Inference;

/// <summary>
/// The provability check of Backward/Forward (Motik, Nenov, Piro and
/// Horrocks, AAAI 2015), and the order explain reads a proof in (ADRs 0196,
/// 0199).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Backward:</strong> a fact is checked once. Checking it checks the
/// premises of every instance that concludes it, through the facts the
/// closure holds. <strong>Forward:</strong> a fact is proved when it is
/// explicit or an axiom, or when an instance concluding it has every premise
/// proved; a proof propagates forward to the checked facts it supports. A
/// fact checked and not proved has no well-founded proof, whatever cycle it
/// sits on, and the search never cuts one.
/// </para>
/// <para>
/// Each fact is checked and proved at most once per search, so it is
/// polynomial in the facts it reaches. A proved fact stays proved while the
/// closure only loses facts that are not, which is all a retraction deletes.
/// </para>
/// </remarks>
internal sealed class ProofSearch(Closure closure)
{
    private readonly HashSet<Fact> _checked = [];
    private readonly Dictionary<Fact, int> _proved = [];
    private readonly List<Fact> _heads = [];
    private readonly List<Fact> _premises = [];

    /// <summary>Whether a fact has a well-founded proof through the facts the closure holds.</summary>
    internal bool Proved(in Fact fact)
    {
        Check(fact);
        return _proved.ContainsKey(fact);
    }

    /// <summary>The order a fact was proved in: every premise of the proof explain reads was proved before it.</summary>
    internal bool TryOrder(in Fact fact, out int order) => _proved.TryGetValue(fact, out order);

    private void Check(Fact fact)
    {
        Stack<Fact> pending = new();
        pending.Push(fact);
        while (pending.Count > 0)
        {
            Fact next = pending.Pop();
            if (!_checked.Add(next) || !closure.Facts.Contains(next))
            {
                continue;
            }

            if (closure.IsGiven(next))
            {
                Prove(next);
                continue;
            }

            // Every premise of every instance concluding it is checked in turn.
            _premises.Clear();
            foreach (Rule rule in closure.Program.Rules)
            {
                rule.AllPremises(next, closure.Facts, _premises);
            }

            foreach (Fact premise in _premises)
            {
                if (!_checked.Contains(premise))
                {
                    pending.Push(premise);
                }
            }

            if (!_proved.ContainsKey(next) && Supported(next))
            {
                Prove(next);
            }
        }
    }

    /// <summary>Whether an instance concludes the fact from proved premises alone.</summary>
    private bool Supported(in Fact fact)
    {
        foreach (Rule rule in closure.Program.Rules)
        {
            if (rule.Proves(fact, closure.Facts, _proved.ContainsKey, premises: null))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Marks a fact proved, and propagates forward to the checked facts it now supports.</summary>
    private void Prove(Fact fact)
    {
        Queue<Fact> proved = new();
        if (_proved.TryAdd(fact, _proved.Count))
        {
            proved.Enqueue(fact);
        }

        while (proved.TryDequeue(out Fact next))
        {
            _heads.Clear();
            foreach (Rule rule in closure.Program.Rules)
            {
                rule.Fire(next, closure.Facts, _heads);
            }

            List<Fact> heads = [.. _heads];
            foreach (Fact head in heads)
            {
                if (_checked.Contains(head) && !_proved.ContainsKey(head) && closure.Facts.Contains(head) && Supported(head))
                {
                    _proved.Add(head, _proved.Count);
                    proved.Enqueue(head);
                }
            }
        }
    }
}
