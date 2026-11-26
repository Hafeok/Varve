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

    /// <summary>
    /// Whether <see cref="MatchesAt"/> finds every new match once a fact is
    /// added; a check that cannot is matched again in full after each change.
    /// </summary>
    internal virtual bool Anchored => false;

    /// <summary>Whether the check reads lists: matched again in full when a pass reads the closure's lists differently.</summary>
    internal virtual bool ReadsLists => false;

    /// <summary>Adds the premises of every match over <paramref name="facts"/> that uses <paramref name="anchor"/>; at least those.</summary>
    internal virtual void MatchesAt(in Fact anchor, FactSet facts, List<Fact[]> into) => Matches(facts, into);

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

    internal override bool Anchored => true;

    internal override void MatchesAt(in Fact anchor, FactSet facts, List<Fact[]> into)
    {
        bool fits = false;
        foreach (Atom atom in body)
        {
            fits |= PatternRule.Fits(atom, anchor);
        }

        if (!fits)
        {
            return;
        }

        _body.MatchesAt(anchor, facts, (bindings, premises) =>
        {
            if (condition is null || condition(bindings))
            {
                into.Add([.. premises]);
            }
        });
    }
}

/// <summary>
/// A check over the pairs of distinct members of a list an axiom names
/// (<c>eq-diff2</c>, <c>eq-diff3</c>, <c>prp-adp</c>, <c>cax-adc</c>): the
/// axiom's typing, its members fact, the cells, and what the pair matches.
/// </summary>
/// <remarks>
/// Since lists are read from asserted statements only (ADR 0197, amended
/// 2026-11-24), a pass that leaves the closure's reading of its lists as it
/// was finds the new matches from the facts it added (ADR 0195 point 6,
/// amended 2026-11-25): a typing or members fact matches its axiom; a cell's
/// statement, the axioms whose lists reach the cell; a fact a pair matches,
/// the other members of each list holding the member it names.
/// </remarks>
/// <param name="profile">The profile the check is of.</param>
/// <param name="name">The check's name.</param>
/// <param name="type"><c>rdf:type</c>.</param>
/// <param name="kind">The axiom's class: <c>owl:AllDifferent</c>, <c>owl:AllDisjointClasses</c>, <c>owl:AllDisjointProperties</c>.</param>
/// <param name="members">The axiom's property: <c>owl:members</c> or <c>owl:distinctMembers</c>.</param>
/// <param name="pair">The facts that make two members clash, the first named first.</param>
/// <param name="named">The members a fact a pair matches names: none for any other fact.</param>
/// <param name="pairAt">The facts that make a fact naming the first member clash with the second, that fact among them.</param>
internal sealed class MembersCheck(
    string profile,
    string name,
    TermHandle type,
    TermHandle kind,
    TermHandle members,
    Action<FactSet, TermHandle, TermHandle, List<Fact[]>> pair,
    Func<Fact, TermHandle[]> named,
    Action<FactSet, Fact, TermHandle, TermHandle, List<Fact[]>> pairAt) : Check(profile, name)
{
    internal override bool Anchored => true;

    internal override bool ReadsLists => true;

    internal override void Matches(FactSet facts, List<Fact[]> into)
    {
        foreach (Fact typing in facts.Match(TermHandle.None, type, kind))
        {
            MatchesOf(typing, facts, into);
        }
    }

    internal override void MatchesAt(in Fact anchor, FactSet facts, List<Fact[]> into)
    {
        if (facts.Lists is not { } lists)
        {
            return;
        }

        // The axioms the fact is part of, or whose lists reach a cell it makes: matched whole.
        HashSet<ulong> subjects = [];
        if (anchor.Predicate.Value == type.Value && anchor.Object.Value == kind.Value)
        {
            subjects.Add(anchor.Subject.Value);
        }

        if (anchor.Predicate.Value == members.Value)
        {
            subjects.Add(anchor.Subject.Value);
        }

        if (lists.IsCellStatement(anchor))
        {
            HashSet<ulong> heads = [];
            lists.HeadsReaching(anchor.Subject, heads);
            foreach (ulong head in heads)
            {
                foreach (Fact axiom in facts.Match(TermHandle.None, members, new TermHandle(head)))
                {
                    subjects.Add(axiom.Subject.Value);
                }
            }
        }

        foreach (ulong subject in subjects)
        {
            foreach (Fact typing in facts.Match(new TermHandle(subject), type, kind))
            {
                MatchesOf(typing, facts, into);
            }
        }

        // A fact a pair matches: paired with the other members of each list holding the member it names.
        List<Cell> path = [];
        List<Fact[]> found = [];
        foreach (TermHandle member in named(anchor))
        {
            foreach (TermHandle cell in lists.CellsHolding(member))
            {
                HashSet<ulong> heads = [];
                lists.HeadsReaching(cell, heads);
                foreach (ulong head in heads)
                {
                    foreach (Fact axiom in facts.Match(TermHandle.None, members, new TermHandle(head)))
                    {
                        foreach (Fact typing in facts.Match(axiom.Subject, type, kind))
                        {
                            if (!lists.TryPath(axiom.Object, path, facts))
                            {
                                continue;
                            }

                            foreach (Cell other in path)
                            {
                                if (other.Member.Value == member.Value)
                                {
                                    continue;
                                }

                                found.Clear();
                                pairAt(facts, anchor, member, other.Member, found);
                                Add(typing, axiom, path, found, into);
                            }
                        }
                    }
                }
            }
        }
    }

    // Every match of one typed axiom subject: each members axiom it has, over every pair of distinct members.
    private void MatchesOf(Fact typing, FactSet facts, List<Fact[]> into)
    {
        List<Fact[]> found = [];
        List<Cell> path = [];
        foreach (Fact axiom in facts.Match(typing.Subject, members, TermHandle.None))
        {
            if (facts.Lists is not { } lists || !lists.TryPath(axiom.Object, path, facts))
            {
                continue;
            }

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
                    Add(typing, axiom, path, found, into);
                }
            }
        }
    }

    private static void Add(Fact typing, Fact axiom, List<Cell> path, List<Fact[]> found, List<Fact[]> into)
    {
        foreach (Fact[] extra in found)
        {
            Fact[] premises = new Fact[2 + (2 * path.Count) + extra.Length];
            premises[0] = typing;
            premises[1] = axiom;
            for (int k = 0; k < path.Count; k++)
            {
                premises[2 + (2 * k)] = path[k].FirstFact;
                premises[3 + (2 * k)] = path[k].RestFact;
            }

            extra.CopyTo(premises, 2 + (2 * path.Count));
            into.Add(premises);
        }
    }
}
