// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Inference.Model;

namespace Varve.Inference.Tests;

/// <summary>
/// The closure from scratch by naive iteration: every rule fired at every
/// fact, until a round adds nothing. It shares the rules with the closure
/// under test and none of its passes, which is what ADR 0195 point 9 holds
/// them to.
/// </summary>
internal static class Oracle
{
    internal static HashSet<Fact> Closure(Program program, IEnumerable<Fact> explicitFacts)
    {
        FactSet facts = new();
        foreach (Fact axiom in program.Axioms)
        {
            facts.Add(axiom);
        }

        foreach (Fact fact in explicitFacts)
        {
            facts.Add(fact);
        }

        List<Fact> heads = [];
        bool grew = true;
        while (grew)
        {
            grew = false;
            List<Fact> round = [.. facts.All];
            foreach (Fact fact in round)
            {
                foreach (Rule rule in program.Rules)
                {
                    heads.Clear();
                    rule.Fire(fact, facts, heads);
                    foreach (Fact head in heads)
                    {
                        grew |= facts.Add(head);
                    }
                }
            }
        }

        return [.. facts.All];
    }
}
