// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Inference;
using Varve.Inference.Model;

namespace Varve.Benchmarks.Candidates;

/// <summary>
/// Backward/Forward (Motik, Nenov, Piro and Horrocks, AAAI 2015), a
/// retraction candidate ADR 0196 measured and did not choose, kept here so
/// that the measurement can be rerun: a fact that loses a premise is kept
/// when a well-founded proof of it remains, searched backwards; one with none
/// is deleted, and the facts it was a premise of are checked in turn. The
/// proof search is the engine's, which explain uses (ADR 0199).
/// </summary>
internal sealed class BackwardForward : Retraction
{
    /// <summary>The one instance: it keeps no state.</summary>
    internal static BackwardForward Instance { get; } = new();

    internal override void Retract(Closure closure, List<Fact> gone)
    {
        Queue<Fact> pending = new(gone);
        List<Fact> heads = [];
        ProofSearch search = new(closure);
        while (pending.TryDequeue(out Fact fact))
        {
            if (!closure.Facts.Contains(fact) || closure.IsGiven(fact))
            {
                continue;
            }

            if (search.Proved(fact))
            {
                continue;
            }

            heads.Clear();
            foreach (Rule rule in closure.Program.Rules)
            {
                rule.Fire(fact, closure.Facts, heads);
            }

            closure.Facts.Remove(fact);
            foreach (Fact head in heads)
            {
                if (closure.Facts.Contains(head) && !closure.IsGiven(head))
                {
                    pending.Enqueue(head);
                }
            }
        }
    }
}
