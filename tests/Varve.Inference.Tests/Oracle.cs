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
    internal static HashSet<Fact> Closure(Program program, IEnumerable<Fact> explicitFacts, AssertedLists? lists = null)
    {
        FactSet facts = new();
        foreach (Fact axiom in program.Axioms)
        {
            facts.Add(axiom);
        }

        List<Fact> statements = [];
        foreach (Fact fact in explicitFacts)
        {
            facts.Add(fact);
            if (program.List is { } list && AssertedLists.IsListStatement(fact, list))
            {
                statements.Add(fact);
            }
        }

        // The lists are read from the explicit statements, as every closure reads them (ADR 0197, amended 2026-11-24).
        // Or, given, another closure's reading: a list is then read where its cells' statements are among these facts.
        if (program.List is { } terms)
        {
            facts.Lists = lists ?? AssertedLists.Read(new HashSet<Fact>(statements), terms, beneath: null);
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
