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
/// A list is any path of cells from the axiom's object to <c>rdf:nil</c>,
/// each cell a subject with an <c>rdf:first</c> and an <c>rdf:rest</c>, no cell
/// twice. Every path is an instance, so adding a fact never takes an instance
/// away: the rule stays monotone however ill-formed a list is.
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
    private const int MaximumCells = 10_000;

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
        if (anchor.Predicate.Value == List.First.Value || anchor.Predicate.Value == List.Rest.Value)
        {
            HeadsReaching(anchor.Subject, facts, heads);
        }

        foreach (TermHandle member in MembersNamedBy(anchor))
        {
            foreach (Fact cell in facts.Match(TermHandle.None, List.First, member))
            {
                HeadsReaching(cell.Subject, facts, heads);
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

    /// <summary>The cells from which <paramref name="cell"/> is reached along <c>rdf:rest</c>, itself included.</summary>
    private void HeadsReaching(TermHandle cell, FactSet facts, HashSet<ulong> heads)
    {
        Stack<TermHandle> pending = new();
        pending.Push(cell);
        while (pending.Count > 0 && heads.Count < MaximumCells)
        {
            TermHandle next = pending.Pop();
            if (!heads.Add(next.Value))
            {
                continue;
            }

            foreach (Fact rest in facts.Match(TermHandle.None, List.Rest, next))
            {
                pending.Push(rest.Subject);
            }
        }
    }

    /// <summary>Every path of cells from <paramref name="head"/> to <c>rdf:nil</c>, no cell twice.</summary>
    protected IEnumerable<List<Cell>> Paths(TermHandle head, FactSet facts)
    {
        List<List<Cell>> paths = [];
        Walk(head, facts, [], [], paths);
        return paths;
    }

    private void Walk(TermHandle cell, FactSet facts, List<Cell> path, HashSet<ulong> visited, List<List<Cell>> paths)
    {
        if (cell.Value == List.Nil.Value)
        {
            paths.Add([.. path]);
            return;
        }

        if (path.Count >= MaximumCells || !visited.Add(cell.Value))
        {
            return;
        }

        foreach (Fact first in facts.Match(cell, List.First, TermHandle.None))
        {
            foreach (Fact rest in facts.Match(cell, List.Rest, TermHandle.None))
            {
                path.Add(new Cell(first.Object, first, rest));
                Walk(rest.Object, facts, path, visited, paths);
                path.RemoveAt(path.Count - 1);
            }
        }

        visited.Remove(cell.Value);
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

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value == List.Type.Value ? AxiomsOf(head.Object, facts) : [];

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) =>
        fact.Predicate.Value == List.Type.Value ? [fact.Object] : [];
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

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value == List.Type.Value ? AxiomsOf(head.Object, facts) : [];

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) =>
        fact.Predicate.Value == List.Type.Value ? [fact.Object] : [];
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

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value == List.Type.Value ? AxiomsOf(head.Object, facts) : [];

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [];
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

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) =>
        head.Predicate.Value != subClassOf.Value ? [] : AxiomsOf(union ? head.Object : head.Subject, facts);

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [];
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

    protected override IEnumerable<Fact> AxiomsFor(Fact head, FactSet facts) => AxiomsOf(head.Predicate, facts);

    protected override IEnumerable<TermHandle> MembersNamedBy(Fact fact) => [fact.Predicate];
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
}
