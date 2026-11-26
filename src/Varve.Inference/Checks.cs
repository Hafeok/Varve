// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// A rule whose head is <c>false</c> (ADR 0197 points 2, 3 and 5): a pattern
/// over the closure whose every match is a clash. A check concludes nothing,
/// so it never changes the closure and is evaluated over it.
/// </summary>
internal abstract class Check
{
    internal const string RuleNamespace = "https://w3id.org/varve/ns#rule/";

    protected Check(string profile, string name)
    {
        Name = name;
        Iri = RdfTerm.Iri(Encoding.UTF8.GetBytes(RuleNamespace + profile + "/" + name));
    }

    /// <summary>The check's name as its specification gives it: <c>cax-dw</c>.</summary>
    internal string Name { get; }

    /// <summary>The check's IRI, which a clash names.</summary>
    internal RdfTerm Iri { get; }

    /// <summary>Adds the premises of every match over <paramref name="facts"/>, one array a match.</summary>
    internal abstract void Matches(FactSet facts, List<Fact[]> into);

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>A check whose body is triple patterns, with a condition on its bindings where the rule has one.</summary>
internal sealed class PatternCheck(string profile, string name, Atom[] body, Func<TermHandle[], bool>? condition = null) : Check(profile, name)
{
    private readonly PatternRule _body = new(name, body, []);

    internal override void Matches(FactSet facts, List<Fact[]> into) =>
        _body.Matches(facts, (bindings, premises) =>
        {
            if (condition is null || condition(bindings))
            {
                into.Add([.. premises]);
            }
        });
}

/// <summary>
/// A check over the pairs of distinct members of a list an axiom names
/// (<c>eq-diff2</c>, <c>eq-diff3</c>, <c>prp-adp</c>, <c>cax-adc</c>): the
/// axiom's typing, its members fact, the cells, and what the pair matches.
/// </summary>
internal sealed class MembersCheck(string profile, string name, TermHandle type, TermHandle kind, TermHandle members, Action<FactSet, TermHandle, TermHandle, List<Fact[]>> pair) : Check(profile, name)
{
    internal override void Matches(FactSet facts, List<Fact[]> into)
    {
        List<Fact[]> found = [];
        foreach (Fact typing in facts.Match(TermHandle.None, type, kind))
        {
            foreach (Fact axiom in facts.Match(typing.Subject, members, TermHandle.None))
            {
                List<Fact> cells = [];
                foreach (List<Cell> path in Paths(axiom.Object, facts))
                {
                    for (int i = 0; i < path.Count; i++)
                    {
                        for (int j = i + 1; j < path.Count; j++)
                        {
                            if (path[i].Member.Value == path[j].Member.Value)
                            {
                                continue;
                            }

                            found.Clear();
                            pair(facts, path[i].Member, path[j].Member, found);
                            pair(facts, path[j].Member, path[i].Member, found);
                            foreach (Fact[] extra in found)
                            {
                                cells.Clear();
                                cells.Add(typing);
                                cells.Add(axiom);
                                foreach (Cell cell in path)
                                {
                                    cells.Add(cell.FirstFact);
                                    cells.Add(cell.RestFact);
                                }

                                cells.AddRange(extra);
                                into.Add([.. cells]);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>The list from <paramref name="head"/> as the list rules read it: from the asserted statements, one path or none (ADR 0197, amended 2026-11-24).</summary>
    private static List<List<Cell>> Paths(TermHandle head, FactSet facts)
    {
        List<Cell> path = [];
        return facts.Lists is { } lists && lists.TryPath(head, path, facts) ? [path] : [];
    }
}
