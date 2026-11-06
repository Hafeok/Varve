// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// One rule of a profile, answering the two questions every pass asks of it
/// (ADR 0196 point 1): what it concludes from a changed fact, and whether a
/// fact still has a derivation by it.
/// </summary>
internal abstract class Rule
{
    protected Rule(string name) => Name = name;

    /// <summary>The rule's name as its profile gives it: <c>rdfs9</c>, <c>cax-sco</c>.</summary>
    internal string Name { get; }

    /// <summary>
    /// Adds to <paramref name="into"/> the head of every instance whose
    /// premises are all in <paramref name="facts"/>, one of them
    /// <paramref name="anchor"/>: the join anchored at a changed fact.
    /// </summary>
    internal abstract void Fire(in Fact anchor, FactSet facts, List<Fact> into);

    /// <summary>Whether some instance concluding <paramref name="head"/> has every premise in <paramref name="facts"/>.</summary>
    internal bool Supports(in Fact head, FactSet facts) => Proves(head, facts, static _ => true, premises: null);

    /// <summary>
    /// Whether some instance concluding <paramref name="head"/> has every
    /// premise in <paramref name="facts"/> and accepted by
    /// <paramref name="accept"/>; its premises are added to
    /// <paramref name="premises"/> when one is given. The backward step of
    /// Backward/Forward and of explain (ADRs 0196, 0199).
    /// </summary>
    internal abstract bool Proves(in Fact head, FactSet facts, Func<Fact, bool> accept, List<Fact>? premises);

    /// <summary>
    /// Adds every premise of every instance concluding <paramref name="head"/>
    /// whose premises are all in <paramref name="facts"/>: the facts the
    /// backward step of Backward/Forward checks (ADR 0196).
    /// </summary>
    internal abstract void AllPremises(in Fact head, FactSet facts, List<Fact> into);

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>A position of an atom: a variable by index, or a constant handle.</summary>
internal readonly record struct Slot(int Variable, TermHandle Constant)
{
    internal bool IsVariable => Variable >= 0;

    internal static Slot Var(int index) => new(index, TermHandle.None);

    internal static Slot Const(TermHandle handle) => new(-1, handle);
}

/// <summary>A triple pattern of a rule's body or head.</summary>
internal readonly record struct Atom(Slot S, Slot P, Slot O);

/// <summary>
/// A rule whose body and head are triple patterns: joined from the anchor's
/// atom outwards, each further atom matched through the narrowest index its
/// bound positions allow.
/// </summary>
internal sealed class PatternRule : Rule
{
    private readonly Atom[] _body;
    private readonly Atom[] _head;
    private readonly int _variables;

    internal PatternRule(string name, Atom[] body, Atom[] head)
        : base(name)
    {
        _body = body;
        _head = head;
        int max = -1;
        foreach (Atom atom in body)
        {
            max = Math.Max(max, Math.Max(atom.S.Variable, Math.Max(atom.P.Variable, atom.O.Variable)));
        }

        _variables = max + 1;
    }

    internal override void Fire(in Fact anchor, FactSet facts, List<Fact> into)
    {
        TermHandle[] bindings = new TermHandle[_variables];
        for (int i = 0; i < _body.Length; i++)
        {
            Array.Clear(bindings);
            if (Unify(_body[i], anchor, bindings))
            {
                Join(facts, bindings, skip: i, next: 0, into, stopAtFirst: false);
            }
        }
    }

    internal override bool Proves(in Fact head, FactSet facts, Func<Fact, bool> accept, List<Fact>? premises)
    {
        TermHandle[] bindings = new TermHandle[_variables];
        Fact[] chosen = new Fact[_body.Length];
        foreach (Atom atom in _head)
        {
            Array.Clear(bindings);
            if (Unify(atom, head, bindings) && Search(facts, bindings, 0, accept, chosen))
            {
                premises?.AddRange(chosen);
                return true;
            }
        }

        return false;
    }

    internal override void AllPremises(in Fact head, FactSet facts, List<Fact> into)
    {
        TermHandle[] bindings = new TermHandle[_variables];
        Fact[] chosen = new Fact[_body.Length];
        foreach (Atom atom in _head)
        {
            Array.Clear(bindings);
            if (Unify(atom, head, bindings))
            {
                Every(facts, bindings, 0, chosen, into);
            }
        }
    }

    private void Every(FactSet facts, TermHandle[] bindings, int next, Fact[] chosen, List<Fact> into)
    {
        if (next == _body.Length)
        {
            into.AddRange(chosen);
            return;
        }

        Atom pattern = _body[next];
        foreach (Fact fact in facts.Match(Resolve(pattern.S, bindings), Resolve(pattern.P, bindings), Resolve(pattern.O, bindings)))
        {
            TermHandle[] inner = (TermHandle[])bindings.Clone();
            if (Unify(pattern, fact, inner))
            {
                chosen[next] = fact;
                Every(facts, inner, next + 1, chosen, into);
            }
        }
    }

    private bool Search(FactSet facts, TermHandle[] bindings, int next, Func<Fact, bool> accept, Fact[] chosen)
    {
        if (next == _body.Length)
        {
            return true;
        }

        Atom pattern = _body[next];
        foreach (Fact fact in facts.Match(Resolve(pattern.S, bindings), Resolve(pattern.P, bindings), Resolve(pattern.O, bindings)))
        {
            TermHandle[] inner = (TermHandle[])bindings.Clone();
            if (Unify(pattern, fact, inner) && accept(fact))
            {
                chosen[next] = fact;
                if (Search(facts, inner, next + 1, accept, chosen))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool Join(FactSet facts, TermHandle[] bindings, int skip, int next, List<Fact>? into, bool stopAtFirst)
    {
        if (next == skip)
        {
            next++;
        }

        if (next == _body.Length)
        {
            if (into is not null)
            {
                foreach (Atom atom in _head)
                {
                    into.Add(new Fact(Resolve(atom.S, bindings), Resolve(atom.P, bindings), Resolve(atom.O, bindings)));
                }
            }

            return true;
        }

        Atom pattern = _body[next];
        TermHandle s = Resolve(pattern.S, bindings), p = Resolve(pattern.P, bindings), o = Resolve(pattern.O, bindings);
        bool found = false;
        foreach (Fact fact in facts.Match(s, p, o))
        {
            TermHandle[] inner = (TermHandle[])bindings.Clone();
            if (Unify(pattern, fact, inner) && Join(facts, inner, skip, next + 1, into, stopAtFirst))
            {
                found = true;
                if (stopAtFirst)
                {
                    return true;
                }
            }
        }

        return found;
    }

    private static TermHandle Resolve(Slot slot, TermHandle[] bindings) => slot.IsVariable ? bindings[slot.Variable] : slot.Constant;

    private static bool Unify(Atom atom, in Fact fact, TermHandle[] bindings) =>
        Bind(atom.S, fact.Subject, bindings) && Bind(atom.P, fact.Predicate, bindings) && Bind(atom.O, fact.Object, bindings);

    private static bool Bind(Slot slot, TermHandle value, TermHandle[] bindings)
    {
        if (!slot.IsVariable)
        {
            return slot.Constant.Value == value.Value;
        }

        TermHandle bound = bindings[slot.Variable];
        if (bound.IsNone)
        {
            bindings[slot.Variable] = value;
            return true;
        }

        return bound.Value == value.Value;
    }
}

/// <summary>
/// rdfD1 and GrdfD1 over the literal itself (ADR 0197 point 2): a literal of a
/// recognised datatype in object position is an instance of its datatype. The
/// fact concluded has a literal subject, held for the joins, never served.
/// </summary>
internal sealed class LiteralTyping : Rule
{
    private readonly IInferenceTerms _terms;
    private readonly TermHandle _type;
    private readonly HashSet<ulong> _recognised;

    internal LiteralTyping(IInferenceTerms terms, TermHandle type, HashSet<ulong> recognised)
        : base("rdfD1")
    {
        _terms = terms;
        _type = type;
        _recognised = recognised;
    }

    internal override void Fire(in Fact anchor, FactSet facts, List<Fact> into)
    {
        if (Typing(anchor.Object, out TermHandle datatype))
        {
            into.Add(new Fact(anchor.Object, _type, datatype));
        }
    }

    internal override bool Proves(in Fact head, FactSet facts, Func<Fact, bool> accept, List<Fact>? premises)
    {
        if (head.Predicate.Value != _type.Value || !Typing(head.Subject, out TermHandle datatype) || datatype.Value != head.Object.Value)
        {
            return false;
        }

        foreach (Fact premise in facts.Match(TermHandle.None, TermHandle.None, head.Subject))
        {
            if (accept(premise))
            {
                premises?.Add(premise);
                return true;
            }
        }

        return false;
    }

    internal override void AllPremises(in Fact head, FactSet facts, List<Fact> into)
    {
        if (head.Predicate.Value != _type.Value || !Typing(head.Subject, out TermHandle datatype) || datatype.Value != head.Object.Value)
        {
            return;
        }

        into.AddRange(facts.Match(TermHandle.None, TermHandle.None, head.Subject));
    }

    private bool Typing(TermHandle literal, out TermHandle datatype) =>
        _terms.IsLiteral(literal) && _terms.TryDatatype(literal, out datatype) && _recognised.Contains(datatype.Value)
        || Fail(out datatype);

    private static bool Fail(out TermHandle datatype)
    {
        datatype = TermHandle.None;
        return false;
    }
}

/// <summary>
/// The axiomatic triples of an <c>rdf:_n</c> (RDF 1.1 Semantics §8.1, §9.1),
/// for each one that occurs in a fact (ADR 0197 point 2).
/// </summary>
internal sealed class ContainerMembershipAxioms : Rule
{
    private readonly IInferenceTerms _terms;
    private readonly (TermHandle P, TermHandle O)[] _axioms;

    internal ContainerMembershipAxioms(IInferenceTerms terms, (TermHandle P, TermHandle O)[] axioms)
        : base("rdf_n")
    {
        _terms = terms;
        _axioms = axioms;
    }

    internal override void Fire(in Fact anchor, FactSet facts, List<Fact> into)
    {
        Emit(anchor.Subject, into);
        if (anchor.Predicate.Value != anchor.Subject.Value)
        {
            Emit(anchor.Predicate, into);
        }

        if (anchor.Object.Value != anchor.Subject.Value && anchor.Object.Value != anchor.Predicate.Value)
        {
            Emit(anchor.Object, into);
        }
    }

    internal override bool Proves(in Fact head, FactSet facts, Func<Fact, bool> accept, List<Fact>? premises)
    {
        TermHandle term = head.Subject;
        if (!_terms.IsContainerMembership(term) || !IsAxiom(head))
        {
            return false;
        }

        // Some fact names it: any fact, an axiom of its own included, since
        // the rule fires on each.
        return Names(facts.Match(term, TermHandle.None, TermHandle.None), accept, premises)
            || Names(facts.Match(TermHandle.None, term, TermHandle.None), accept, premises)
            || Names(facts.Match(TermHandle.None, TermHandle.None, term), accept, premises);
    }

    internal override void AllPremises(in Fact head, FactSet facts, List<Fact> into)
    {
        TermHandle term = head.Subject;
        if (!_terms.IsContainerMembership(term) || !IsAxiom(head))
        {
            return;
        }

        into.AddRange(facts.Match(term, TermHandle.None, TermHandle.None));
        into.AddRange(facts.Match(TermHandle.None, term, TermHandle.None));
        into.AddRange(facts.Match(TermHandle.None, TermHandle.None, term));
    }

    private static bool Names(IEnumerable<Fact> facts, Func<Fact, bool> accept, List<Fact>? premises)
    {
        foreach (Fact fact in facts)
        {
            if (accept(fact))
            {
                premises?.Add(fact);
                return true;
            }
        }

        return false;
    }

    private bool IsAxiom(in Fact fact)
    {
        foreach ((TermHandle p, TermHandle o) in _axioms)
        {
            if (fact.Predicate.Value == p.Value && fact.Object.Value == o.Value)
            {
                return true;
            }
        }

        return false;
    }

    private void Emit(TermHandle term, List<Fact> into)
    {
        if (!_terms.IsContainerMembership(term))
        {
            return;
        }

        foreach ((TermHandle p, TermHandle o) in _axioms)
        {
            into.Add(new Fact(term, p, o));
        }
    }
}
