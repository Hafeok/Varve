// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>The list vocabulary's handles, and the axiom predicate's.</summary>
internal readonly record struct ListTerms(TermHandle First, TermHandle Rest, TermHandle Nil, TermHandle Type);

/// <summary>One cell of a path through an <c>rdf:List</c>: its member, and the two facts that make it a cell.</summary>
internal readonly record struct Cell(TermHandle Member, Fact FirstFact, Fact RestFact);

/// <summary>
/// A rule whose premises include an <c>rdf:List</c> an axiom names (OWL 2
/// Profiles §4.3: <c>cls-int1</c>, <c>cls-uni</c>, <c>prp-spo2</c>, …).
/// </summary>
/// <remarks>
/// <para>
/// A list is read from the asserted statements alone (ADR 0197, amended
/// 2026-11-24; <see cref="AssertedLists"/>): the one chain of cells from the
/// axiom's object to <c>rdf:nil</c>, each cell a subject the asserted
/// statements give exactly one <c>rdf:first</c> and one <c>rdf:rest</c>. An
/// axiom whose object is no such list has no instance, and its cells are
/// reported unread. The reading does not change while the rules run, so the
/// rule is monotone in the facts it joins (#399).
/// </para>
/// <para>
/// Fired at a changed fact, the rule re-evaluates every axiom instance the
/// fact can be a premise of, narrowed to the fact where the rule can. Every
/// head it adds is supported by facts present, which is all insertion needs,
/// and it adds every head the fact is a premise of, which is all deletion
/// needs (ADR 0196 point 1).
/// </para>
/// </remarks>
internal abstract class ListRule : Rule
{
    protected ListRule(string name, TermHandle axiom, ListTerms list)
        : base(name)
    {
        Axiom = axiom;
        List = list;
    }

    /// <summary>The predicate of the axiom that names the list: <c>owl:intersectionOf</c>, <c>owl:propertyChainAxiom</c>.</summary>
    protected TermHandle Axiom { get; }

    protected ListTerms List { get; }

    internal sealed override void Fire(in Fact anchor, FactSet facts, List<Fact> into)
    {
        foreach (Fact axiom in Touched(anchor, facts))
        {
            foreach (List<Cell> path in Paths(axiom.Object, facts))
            {
                Evaluate(axiom, path, anchor, facts, into);
            }
        }
    }

    internal sealed override bool Proves(in Fact head, FactSet facts, Func<Fact, bool> accept, List<Fact>? premises)
    {
        foreach (Fact axiom in AxiomsFor(head, facts))
        {
            if (!accept(axiom))
            {
                continue;
            }

            foreach (List<Cell> path in Paths(axiom.Object, facts))
            {
                if (!Accepted(path, accept))
                {
                    continue;
                }

                List<Fact> found = [];
                if (Prove(head, axiom, path, facts, accept, found))
                {
                    if (premises is not null)
                    {
                        premises.Add(axiom);
                        foreach (Cell cell in path)
                        {
                            premises.Add(cell.FirstFact);
                            premises.Add(cell.RestFact);
                        }

                        premises.AddRange(found);
                    }

                    return true;
                }
            }
        }

        return false;
    }

    internal sealed override void AllPremises(in Fact head, FactSet facts, List<Fact> into)
    {
        foreach (Fact axiom in AxiomsFor(head, facts))
        {
            foreach (List<Cell> path in Paths(axiom.Object, facts))
            {
                List<Fact> extra = [];
                if (!Prove(head, axiom, path, facts, static _ => true, extra))
                {
                    continue;
                }

                into.Add(axiom);
                foreach (Cell cell in path)
                {
                    into.Add(cell.FirstFact);
                    into.Add(cell.RestFact);
                }

                Premises(head, axiom, path, facts, into);
            }
        }
    }

    internal sealed override void Instances(in Fact head, FactSet facts, List<Fact[]> into)
    {
        List<Fact[]> extras = [];
        foreach (Fact axiom in AxiomsFor(head, facts))
        {
            foreach (List<Cell> path in Paths(axiom.Object, facts))
            {
                extras.Clear();
                Extras(head, axiom, path, facts, extras);
                foreach (Fact[] extra in extras)
                {
                    into.Add(Premises(axiom, path, extra));
                }
            }
        }
    }

    internal sealed override void Derivations(in Fact anchor, FactSet facts, List<Instance> into)
    {
        List<(Fact Head, Fact[] Extras)> all = [];
        foreach (Fact axiom in Touched(anchor, facts))
        {
            foreach (List<Cell> path in Paths(axiom.Object, facts))
            {
                all.Clear();
                All(axiom, path, facts, all);
                foreach ((Fact head, Fact[] extra) in all)
                {
                    Fact[] premises = Premises(axiom, path, extra);
                    if (Array.IndexOf(premises, anchor) >= 0)
                    {
                        into.Add(new Instance(head, premises));
                    }
                }
            }
        }
    }

    private static Fact[] Premises(in Fact axiom, List<Cell> path, Fact[] extra)
    {
        Fact[] premises = new Fact[1 + (2 * path.Count) + extra.Length];
        premises[0] = axiom;
        for (int i = 0; i < path.Count; i++)
        {
            premises[1 + (2 * i)] = path[i].FirstFact;
            premises[2 + (2 * i)] = path[i].RestFact;
        }

        extra.CopyTo(premises, 1 + (2 * path.Count));
        return premises;
    }

    /// <summary>Adds the premises beyond the axiom and its cells of each instance of one axiom and one path that concludes <paramref name="head"/>, every one present, one array an instance.</summary>
    protected abstract void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into);

    /// <summary>Adds every instance of one axiom and one path whose premises are present: its head, and its premises beyond the axiom and its cells.</summary>
    protected abstract void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into);

    /// <summary>Adds every fact beyond the axiom and its cells that an instance of one axiom and one path concluding <paramref name="head"/> can have as a premise.</summary>
    protected abstract void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into);

    /// <summary>Adds the heads of the instances of one axiom and one path, narrowed to <paramref name="anchor"/> where the rule can.</summary>
    protected abstract void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into);

    /// <summary>Whether the instance of one axiom and one path concludes <paramref name="head"/> through facts <paramref name="accept"/> takes; their facts beyond the list's go to <paramref name="found"/>.</summary>
    protected abstract bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found);

    /// <summary>The axioms whose instances could conclude <paramref name="head"/>.</summary>
    protected abstract IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts);

    /// <summary>The axioms a fact other than the axiom and its list's cells is a premise of, through the member it names.</summary>
    protected abstract IEnumerable<TermHandle> MembersNamedBy(Fact fact);

    /// <summary>The subjects of the axioms a fact other than the axiom and its cells is a premise of directly.</summary>
    protected virtual IEnumerable<TermHandle> AxiomSubjectsNamedBy(Fact fact) => [];

    /// <summary>Every axiom fact of this rule with <paramref name="subject"/> as its subject.</summary>
    protected IEnumerable<Fact> AxiomsOf(TermHandle subject, FactSet facts) => facts.Match(subject, Axiom, TermHandle.None);

    private IEnumerable<Fact> Touched(Fact anchor, FactSet facts)
    {
        if (anchor.Predicate.Value == Axiom.Value)
        {
            yield return anchor;
            yield break;
        }

        HashSet<ulong> heads = [];
        if (facts.Lists is { } lists)
        {
            if (lists.IsCellStatement(anchor))
            {
                lists.HeadsReaching(anchor.Subject, heads);
            }

            foreach (TermHandle member in MembersNamedBy(anchor))
            {
                foreach (TermHandle cell in lists.CellsHolding(member))
                {
                    lists.HeadsReaching(cell, heads);
                }
            }
        }

        HashSet<Fact> seen = [];
        foreach (TermHandle subject in AxiomSubjectsNamedBy(anchor))
        {
            foreach (Fact axiom in AxiomsOf(subject, facts))
            {
                if (seen.Add(axiom))
                {
                    yield return axiom;
                }
            }
        }

        foreach (ulong head in heads)
        {
            foreach (Fact axiom in facts.Match(TermHandle.None, Axiom, new TermHandle(head)))
            {
                if (seen.Add(axiom))
                {
                    yield return axiom;
                }
            }
        }
    }

    /// <summary>The list from <paramref name="head"/> as the asserted statements make it: one path, or none (ADR 0197, amended 2026-11-24).</summary>
    protected static IEnumerable<List<Cell>> Paths(TermHandle head, FactSet facts)
    {
        List<Cell> path = [];
        if (facts.Lists is { } lists && lists.TryPath(head, path, facts))
        {
            yield return path;
        }
    }

    private static bool Accepted(List<Cell> path, Func<Fact, bool> accept)
    {
        foreach (Cell cell in path)
        {
            if (!accept(cell.FirstFact) || !accept(cell.RestFact))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a path holds a member.</summary>
    protected static bool Holds(List<Cell> path, TermHandle member)
    {
        foreach (Cell cell in path)
        {
            if (cell.Member.Value == member.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="fact"/> is in the facts and taken, added to <paramref name="found"/> if so.</summary>
    protected static bool Take(in Fact fact, FactSet facts, Func<Fact, bool> accept, List<Fact> found)
    {
        if (facts.Contains(fact) && accept(fact))
        {
            found.Add(fact);
            return true;
        }

        return false;
    }
}

/// <summary>cls-int1: an instance of every class of an intersection is an instance of it.</summary>
internal sealed class IntersectionMembership(TermHandle axiom, ListTerms list) : ListRule("cls-int1", axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        if (path.Count == 0)
        {
            return;
        }

        // The candidates: the anchor's subject when it is a typing into a
        // member, else every instance of the first member.
        IEnumerable<Fact> candidates = anchor.Predicate.Value == List.Type.Value && Holds(path, anchor.Object)
            ? [anchor]
            : facts.Match(TermHandle.None, List.Type, path[0].Member);
        foreach (Fact candidate in candidates)
        {
            bool all = true;
            foreach (Cell cell in path)
            {
                all &= facts.Contains(new Fact(candidate.Subject, List.Type, cell.Member));
            }

            if (all)
            {
                into.Add(new Fact(candidate.Subject, List.Type, axiom.Subject));
            }
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found)
    {
        if (path.Count == 0)
        {
            return false;
        }

        foreach (Cell cell in path)
        {
            if (!Take(new Fact(head.Subject, List.Type, cell.Member), facts, accept, found))
            {
                return false;
            }
        }

        return true;
    }

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into)
    {
        foreach (Cell cell in path)
        {
            into.Add(new Fact(head.Subject, List.Type, cell.Member));
        }
    }

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value == List.Type.Value ? AxiomsOf(head.Object, facts) : [];

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) =>
        fact.Predicate.Value == List.Type.Value ? [fact.Object] : [];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        if (path.Count > 0 && head.Predicate.Value == List.Type.Value && head.Object.Value == axiom.Subject.Value)
        {
            Typings(head.Subject, path, facts, into);
        }
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        if (path.Count == 0)
        {
            return;
        }

        List<Fact[]> found = [];
        foreach (Fact candidate in facts.Match(TermHandle.None, List.Type, path[0].Member))
        {
            found.Clear();
            Typings(candidate.Subject, path, facts, found);
            foreach (Fact[] extra in found)
            {
                into.Add((new Fact(candidate.Subject, List.Type, axiom.Subject), extra));
            }
        }
    }

    private void Typings(TermHandle subject, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        Fact[] typings = new Fact[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            typings[i] = new Fact(subject, List.Type, path[i].Member);
            if (!facts.Contains(typings[i]))
            {
                return;
            }
        }

        into.Add(typings);
    }
}

/// <summary>cls-int2: an instance of an intersection is an instance of each of its classes.</summary>
internal sealed class IntersectionParts(TermHandle axiom, ListTerms list) : ListRule("cls-int2", axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        IEnumerable<Fact> instances = anchor.Predicate.Value == List.Type.Value && anchor.Object.Value == axiom.Subject.Value
            ? [anchor]
            : facts.Match(TermHandle.None, List.Type, axiom.Subject);
        foreach (Fact instance in instances)
        {
            foreach (Cell cell in path)
            {
                into.Add(new Fact(instance.Subject, List.Type, cell.Member));
            }
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found) =>
        Holds(path, head.Object)
        && Take(new Fact(head.Subject, List.Type, axiom.Subject), facts, accept, found);

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into) =>
        into.Add(new Fact(head.Subject, List.Type, axiom.Subject));

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts)
    {
        if (head.Predicate.Value != List.Type.Value)
        {
            yield break;
        }

        // Any intersection whose list holds the class: found through the cells naming it.
        HashSet<Fact> seen = [];
        foreach (Fact instance in facts.Match(head.Subject, List.Type, TermHandle.None))
        {
            foreach (Fact axiom in AxiomsOf(instance.Object, facts))
            {
                if (seen.Add(axiom))
                {
                    yield return axiom;
                }
            }
        }
    }

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [];

    protected override IEnumerable<TermHandle> AxiomSubjectsNamedBy(Fact fact) =>
        fact.Predicate.Value == List.Type.Value ? [fact.Object] : [];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        Fact instance = new(head.Subject, List.Type, axiom.Subject);
        if (head.Predicate.Value == List.Type.Value && Holds(path, head.Object) && facts.Contains(instance))
        {
            into.Add([instance]);
        }
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        foreach (Fact instance in facts.Match(TermHandle.None, List.Type, axiom.Subject))
        {
            foreach (Cell cell in path)
            {
                into.Add((new Fact(instance.Subject, List.Type, cell.Member), [instance]));
            }
        }
    }
}

/// <summary>cls-uni: an instance of any class of a union is an instance of it.</summary>
internal sealed class UnionMembership(TermHandle axiom, ListTerms list) : ListRule("cls-uni", axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        foreach (Cell cell in path)
        {
            IEnumerable<Fact> instances = anchor.Predicate.Value == List.Type.Value && anchor.Object.Value == cell.Member.Value
                ? [anchor]
                : facts.Match(TermHandle.None, List.Type, cell.Member);
            foreach (Fact instance in instances)
            {
                into.Add(new Fact(instance.Subject, List.Type, axiom.Subject));
            }
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found)
    {
        foreach (Cell cell in path)
        {
            if (Take(new Fact(head.Subject, List.Type, cell.Member), facts, accept, found))
            {
                return true;
            }
        }

        return false;
    }

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into)
    {
        foreach (Cell cell in path)
        {
            Fact typing = new(head.Subject, List.Type, cell.Member);
            if (facts.Contains(typing))
            {
                into.Add(typing);
            }
        }
    }

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value == List.Type.Value ? AxiomsOf(head.Object, facts) : [];

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) =>
        fact.Predicate.Value == List.Type.Value ? [fact.Object] : [];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        if (head.Predicate.Value != List.Type.Value || head.Object.Value != axiom.Subject.Value)
        {
            return;
        }

        foreach (Cell cell in path)
        {
            Fact typing = new(head.Subject, List.Type, cell.Member);
            if (facts.Contains(typing))
            {
                into.Add([typing]);
            }
        }
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        foreach (Cell cell in path)
        {
            foreach (Fact instance in facts.Match(TermHandle.None, List.Type, cell.Member))
            {
                into.Add((new Fact(instance.Subject, List.Type, axiom.Subject), [instance]));
            }
        }
    }
}

/// <summary>cls-oo: each member of an enumeration is an instance of it.</summary>
internal sealed class Enumeration(TermHandle axiom, ListTerms list) : ListRule("cls-oo", axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        foreach (Cell cell in path)
        {
            into.Add(new Fact(cell.Member, List.Type, axiom.Subject));
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found) =>
        Holds(path, head.Subject);

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into)
    {
    }

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value == List.Type.Value ? AxiomsOf(head.Object, facts) : [];

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        if (head.Predicate.Value == List.Type.Value && head.Object.Value == axiom.Subject.Value && Holds(path, head.Subject))
        {
            into.Add([]);
        }
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        foreach (Cell cell in path)
        {
            into.Add((new Fact(cell.Member, List.Type, axiom.Subject), []));
        }
    }
}

/// <summary>
/// scm-int and scm-uni: an intersection is a subclass of each of its classes,
/// and each class of a union a subclass of it.
/// </summary>
internal sealed class ListSubClasses(string name, TermHandle axiom, ListTerms list, TermHandle subClassOf, bool union) : ListRule(name, axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        foreach (Cell cell in path)
        {
            into.Add(union ? new Fact(cell.Member, subClassOf, axiom.Subject) : new Fact(axiom.Subject, subClassOf, cell.Member));
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found)
    {
        return Holds(path, union ? head.Subject : head.Object);
    }

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into)
    {
    }

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value != subClassOf.Value ? [] : AxiomsOf(union ? head.Object : head.Subject, facts);

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        bool named = head.Predicate.Value == subClassOf.Value
            && (union ? head.Object.Value == axiom.Subject.Value : head.Subject.Value == axiom.Subject.Value);
        if (named && Holds(path, union ? head.Subject : head.Object))
        {
            into.Add([]);
        }
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        foreach (Cell cell in path)
        {
            into.Add((union ? new Fact(cell.Member, subClassOf, axiom.Subject) : new Fact(axiom.Subject, subClassOf, cell.Member), []));
        }
    }
}

/// <summary>prp-spo2: a chain of the properties of a property chain implies the property.</summary>
internal sealed class PropertyChain(TermHandle axiom, ListTerms list) : ListRule("prp-spo2", axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        if (path.Count == 0)
        {
            return;
        }

        foreach (Fact start in facts.Match(TermHandle.None, path[0].Member, TermHandle.None))
        {
            HashSet<ulong> reached = [start.Object.Value];
            for (int i = 1; i < path.Count && reached.Count > 0; i++)
            {
                HashSet<ulong> next = [];
                foreach (ulong node in reached)
                {
                    foreach (Fact step in facts.Match(new TermHandle(node), path[i].Member, TermHandle.None))
                    {
                        next.Add(step.Object.Value);
                    }
                }

                reached = next;
            }

            foreach (ulong end in reached)
            {
                into.Add(new Fact(start.Subject, axiom.Subject, new TermHandle(end)));
            }
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found) =>
        path.Count > 0 && Chain(head.Subject, 0, head.Object, path, facts, accept, found);

    private static bool Chain(TermHandle from, int index, TermHandle to, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found)
    {
        foreach (Fact step in facts.Match(from, path[index].Member, TermHandle.None))
        {
            if (!accept(step))
            {
                continue;
            }

            bool last = index == path.Count - 1;
            if (last ? step.Object.Value == to.Value : Chain(step.Object, index + 1, to, path, facts, accept, found))
            {
                found.Add(step);
                return true;
            }
        }

        return false;
    }

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into)
    {
        // Every step on some chain from the head's subject to its object:
        // reached forwards from the one and backwards from the other.
        int n = path.Count;
        HashSet<ulong>[] forward = new HashSet<ulong>[n + 1];
        forward[0] = [head.Subject.Value];
        for (int i = 0; i < n; i++)
        {
            forward[i + 1] = [];
            foreach (ulong node in forward[i])
            {
                foreach (Fact step in facts.Match(new TermHandle(node), path[i].Member, TermHandle.None))
                {
                    forward[i + 1].Add(step.Object.Value);
                }
            }
        }

        HashSet<ulong> backward = [head.Object.Value];
        for (int i = n - 1; i >= 0; i--)
        {
            HashSet<ulong> before = [];
            foreach (ulong node in backward)
            {
                foreach (Fact step in facts.Match(TermHandle.None, path[i].Member, new TermHandle(node)))
                {
                    if (forward[i].Contains(step.Subject.Value))
                    {
                        into.Add(step);
                        before.Add(step.Subject.Value);
                    }
                }
            }

            backward = before;
        }
    }

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) => AxiomsOf(head.Predicate, facts);

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [fact.Predicate];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        if (path.Count == 0 || head.Predicate.Value != axiom.Subject.Value)
        {
            return;
        }

        TermHandle end = head.Object;
        Chains(head.Subject, 0, path, facts, new Fact[path.Count], steps =>
        {
            if (steps[^1].Object.Value == end.Value)
            {
                into.Add([.. steps]);
            }
        });
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        if (path.Count == 0)
        {
            return;
        }

        TermHandle property = axiom.Subject;
        HashSet<ulong> starts = [];
        foreach (Fact first in facts.Match(TermHandle.None, path[0].Member, TermHandle.None))
        {
            if (starts.Add(first.Subject.Value))
            {
                Chains(first.Subject, 0, path, facts, new Fact[path.Count], steps =>
                    into.Add((new Fact(steps[0].Subject, property, steps[^1].Object), [.. steps])));
            }
        }
    }

    /// <summary>Every chain of steps along the path's properties from <paramref name="from"/>, one step a property.</summary>
    private static void Chains(TermHandle from, int index, List<Cell> path, FactSet facts, Fact[] steps, Action<Fact[]> found)
    {
        foreach (Fact step in facts.Match(from, path[index].Member, TermHandle.None))
        {
            steps[index] = step;
            if (index == path.Count - 1)
            {
                found(steps);
            }
            else
            {
                Chains(step.Object, index + 1, path, facts, steps, found);
            }
        }
    }
}

/// <summary>prp-key: two instances of a class that agree on every property of its key are the same.</summary>
internal sealed class Key(TermHandle axiom, ListTerms list, TermHandle sameAs) : ListRule("prp-key", axiom, list)
{
    protected override void Evaluate(in Fact axiom, List<Cell> path, in Fact anchor, FactSet facts, List<Fact> into)
    {
        if (path.Count == 0)
        {
            return;
        }

        List<Fact> instances = [.. facts.Match(TermHandle.None, List.Type, axiom.Subject)];
        foreach (Fact x in instances)
        {
            foreach (Fact y in instances)
            {
                if (Agree(x.Subject, y.Subject, path, facts, static _ => true, null))
                {
                    into.Add(new Fact(x.Subject, sameAs, y.Subject));
                }
            }
        }
    }

    protected override bool Prove(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact> found) =>
        path.Count > 0
        && Take(new Fact(head.Subject, List.Type, axiom.Subject), facts, accept, found)
        && Take(new Fact(head.Object, List.Type, axiom.Subject), facts, accept, found)
        && Agree(head.Subject, head.Object, path, facts, accept, found);

    private static bool Agree(TermHandle x, TermHandle y, List<Cell> path, FactSet facts, Func<Fact, bool> accept, List<Fact>? found)
    {
        foreach (Cell cell in path)
        {
            bool agreed = false;
            foreach (Fact xz in facts.Match(x, cell.Member, TermHandle.None))
            {
                Fact yz = new(y, cell.Member, xz.Object);
                if (accept(xz) && facts.Contains(yz) && accept(yz))
                {
                    found?.Add(xz);
                    found?.Add(yz);
                    agreed = true;
                    break;
                }
            }

            if (!agreed)
            {
                return false;
            }
        }

        return true;
    }

    protected override void Premises(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact> into)
    {
        into.Add(new Fact(head.Subject, List.Type, axiom.Subject));
        into.Add(new Fact(head.Object, List.Type, axiom.Subject));
        foreach (Cell cell in path)
        {
            foreach (Fact xz in facts.Match(head.Subject, cell.Member, TermHandle.None))
            {
                Fact yz = new(head.Object, cell.Member, xz.Object);
                if (facts.Contains(yz))
                {
                    into.Add(xz);
                    into.Add(yz);
                }
            }
        }
    }

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts)
    {
        if (head.Predicate.Value != sameAs.Value)
        {
            yield break;
        }

        HashSet<Fact> seen = [];
        foreach (Fact typing in facts.Match(head.Subject, List.Type, TermHandle.None))
        {
            foreach (Fact axiom in AxiomsOf(typing.Object, facts))
            {
                if (seen.Add(axiom))
                {
                    yield return axiom;
                }
            }
        }
    }

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [fact.Predicate];

    protected override IEnumerable<TermHandle> AxiomSubjectsNamedBy(Fact fact) =>
        fact.Predicate.Value == List.Type.Value ? [fact.Object] : [];

    protected override void Extras(in Fact head, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        if (head.Predicate.Value == sameAs.Value)
        {
            Witnesses(head.Subject, head.Object, axiom, path, facts, into);
        }
    }

    protected override void All(in Fact axiom, List<Cell> path, FactSet facts, List<(Fact Head, Fact[] Extras)> into)
    {
        List<Fact> instances = [.. facts.Match(TermHandle.None, List.Type, axiom.Subject)];
        List<Fact[]> found = [];
        foreach (Fact x in instances)
        {
            foreach (Fact y in instances)
            {
                found.Clear();
                Witnesses(x.Subject, y.Subject, axiom, path, facts, found);
                foreach (Fact[] extra in found)
                {
                    into.Add((new Fact(x.Subject, sameAs, y.Subject), extra));
                }
            }
        }
    }

    /// <summary>Each instance's premises for x and y: their typings, then a pair of agreeing values for every key property, every choice of pair.</summary>
    private void Witnesses(TermHandle x, TermHandle y, in Fact axiom, List<Cell> path, FactSet facts, List<Fact[]> into)
    {
        Fact xType = new(x, List.Type, axiom.Subject), yType = new(y, List.Type, axiom.Subject);
        if (path.Count == 0 || !facts.Contains(xType) || !facts.Contains(yType))
        {
            return;
        }

        Fact[] premises = new Fact[2 + (2 * path.Count)];
        premises[0] = xType;
        premises[1] = yType;
        Choose(0);

        void Choose(int index)
        {
            if (index == path.Count)
            {
                into.Add([.. premises]);
                return;
            }

            foreach (Fact xz in facts.Match(x, path[index].Member, TermHandle.None))
            {
                Fact yz = new(y, path[index].Member, xz.Object);
                if (facts.Contains(yz))
                {
                    premises[2 + (2 * index)] = xz;
                    premises[3 + (2 * index)] = yz;
                    Choose(index + 1);
                }
            }
        }
    }
}
