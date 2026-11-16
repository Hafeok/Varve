// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Derivation;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// The closure of one graph's explicit facts under a profile's rules,
/// maintained as facts are asserted and retracted (ADRs 0195–0197): the
/// explicit facts, the profile's axioms, and every fact the rules conclude
/// from them, to the least fixpoint.
/// </summary>
/// <remarks>
/// Over a source's handles, which compare by value (ADR 0195 point 8): exact
/// while erasure mode is off. Not safe for concurrent use; a projection owns
/// one and publishes what it reads of it.
/// </remarks>
[Contract(typeof(TheInferenceProjection.IncrementalEqualsFromScratch), Role = "a graph's closure, maintained by insertion and retraction")]
public sealed partial class Closure
{
    private readonly HashSet<Fact> _explicit = [];
    private readonly HashSet<Fact> _axioms = [];

    /// <summary>The empty graph's closure under <paramref name="profile"/>: its axioms and what they entail.</summary>
    public Closure(InferenceProfile profile, IInferenceTerms terms)
        : this(Program.Of(profile, terms), Retraction.RecursiveCounting)
    {
    }

    /// <summary>
    /// The empty graph's closure under <paramref name="profile"/>, its facts
    /// taken from <paramref name="budget"/>, which the closures of one binding
    /// share (ADR 0195 point 6).
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">The axioms alone would pass the budget.</exception>
    public Closure(InferenceProfile profile, IInferenceTerms terms, FactBudget budget)
        : this(Program.Of(profile, terms), Retraction.RecursiveCounting, budget)
    {
    }

    /// <summary>
    /// A data graph's closure over <paramref name="tbox"/>, the TBox's: its
    /// facts are this closure's too, given, never held twice nor counted
    /// against the budget again (ADR 0195 point 3). The TBox closure does
    /// not change while this one lives; a TBox change rebuilds both.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">The axioms alone would pass the budget.</exception>
    public Closure(InferenceProfile profile, IInferenceTerms terms, FactBudget budget, Closure tbox)
        : this(Program.Of(profile, terms), Retraction.RecursiveCounting, budget, tbox)
    {
    }

    internal Closure(Program program, Retraction retraction, FactBudget? budget = null, Closure? tbox = null)
    {
        Program = program;
        Strategy = retraction;
        Beneath = tbox;
        Facts = new FactSet { Base = tbox?.Facts };
        Facts.Budget = budget;
        foreach (Fact axiom in program.Axioms)
        {
            _axioms.Add(axiom);
        }

        Insert(program.Axioms);
    }

    internal Retraction Strategy { get; }

    /// <summary>The TBox closure beneath a data graph's, if it has one.</summary>
    internal Closure? Beneath { get; }

    /// <summary>Whether the closure holds a fact.</summary>
    public bool Contains(Fact fact) => Facts.Contains(fact);

    /// <summary>Whether a fact is explicit: asserted, and not since retracted; here, or in the TBox closure beneath.</summary>
    public bool IsExplicit(Fact fact) => _explicit.Contains(fact) || (Beneath is { } tbox && tbox.IsExplicit(fact));

    /// <summary>The facts the closure holds that are not explicit, generalised ones included.</summary>
    public IEnumerable<Fact> Implicit
    {
        get
        {
            foreach (Fact fact in All)
            {
                if (!IsExplicit(fact))
                {
                    yield return fact;
                }
            }
        }
    }

    /// <summary>The facts a data graph's closure holds itself, beyond the TBox closure beneath it; every fact for a closure with none.</summary>
    public IEnumerable<Fact> Own => Facts.All;

    /// <summary>Every fact of the closure, the TBox closure's beneath it included.</summary>
    public IEnumerable<Fact> All
    {
        get
        {
            foreach (Fact fact in Facts.All)
            {
                yield return fact;
            }

            if (Beneath is not null)
            {
                foreach (Fact fact in Beneath.All)
                {
                    yield return fact;
                }
            }
        }
    }

    /// <summary>
    /// Every clash the profile's checks find over the closure (ADR 0197
    /// point 5), each check and set of premises once. A clash changes nothing
    /// the closure holds: it is reported, never served as everything.
    /// </summary>
    public IReadOnlyList<Clash> Clashes()
    {
        List<Clash> clashes = [];
        List<Fact[]> matches = [];
        foreach (Check check in Program.Checks)
        {
            matches.Clear();
            check.Matches(Facts, matches);
            HashSet<string> seen = [];
            foreach (Fact[] premises in matches)
            {
                Fact[] ordered = [.. premises];
                Array.Sort(ordered, static (x, y) =>
                    x.Subject.Value != y.Subject.Value ? x.Subject.Value.CompareTo(y.Subject.Value)
                    : x.Predicate.Value != y.Predicate.Value ? x.Predicate.Value.CompareTo(y.Predicate.Value)
                    : x.Object.Value.CompareTo(y.Object.Value));
                if (seen.Add(string.Join(' ', ordered)))
                {
                    clashes.Add(new Clash(check.Iri, premises));
                }
            }
        }

        return clashes;
    }

    internal FactSet Facts { get; }

    internal Program Program { get; }

    /// <summary>
    /// Applies an exact delta: <paramref name="retracted"/> were explicit and
    /// are not, <paramref name="asserted"/> were not and are. The closure
    /// after equals the closure of the explicit facts after, from scratch
    /// (ADR 0195 point 9).
    /// </summary>
    /// <returns>
    /// The facts whose membership in the closure changed: each one held
    /// before and not after, or after and not before.
    /// </returns>
    /// <exception cref="ClosureLimitExceededException">
    /// The closure would pass its budget. It is then partly applied, and its
    /// owner drops it.
    /// </exception>
    public IReadOnlyCollection<Fact> Apply(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted)
    {
        Dictionary<Fact, bool> touched = [];
        Facts.Touched = touched;
        try
        {
            ApplyDelta(asserted, retracted);
        }
        finally
        {
            Facts.Touched = null;
        }

        List<Fact> changed = [];
        foreach ((Fact fact, bool held) in touched)
        {
            if (Facts.Contains(fact) != held)
            {
                changed.Add(fact);
            }
        }

        return changed;
    }

    /// <summary>
    /// A well-founded proof of a fact the closure holds (ADR 0199), or null
    /// when it does not hold it. Found by Backward/Forward's check (ADR 0196):
    /// every fact a proof reaches is proved in an order, and each node is
    /// concluded from premises proved before it.
    /// </summary>
    public Proof? Explain(Fact fact)
    {
        if (!Facts.Contains(fact))
        {
            return null;
        }

        // A fact of the TBox closure beneath is proved there.
        if (Beneath is { } tbox && !Facts.Holds(fact) && !_explicit.Contains(fact))
        {
            return tbox.Explain(fact);
        }

        ProofSearch search = new(this);
        if (!search.Proved(fact))
        {
            // A fact the closure holds always has a proof; a closure that does not is no closure.
            throw new InvalidOperationException("A fact of the closure has no well-founded proof.");
        }

        Dictionary<Fact, Proof> built = [];
        return Build(fact, search, built);
    }

    private Proof Build(Fact fact, ProofSearch search, Dictionary<Fact, Proof> built)
    {
        if (built.TryGetValue(fact, out Proof? done))
        {
            return done;
        }

        if (Beneath is { } tbox && !Facts.Holds(fact) && !_explicit.Contains(fact))
        {
            Proof under = tbox.Explain(fact)!;
            built[fact] = under;
            return under;
        }

        if (_explicit.Contains(fact) || _axioms.Contains(fact))
        {
            Proof leaf = new(fact, _explicit.Contains(fact) ? ProofStatus.Explicit : ProofStatus.Axiom, null, []);
            built[fact] = leaf;
            return leaf;
        }

        search.TryOrder(fact, out int order);
        List<Fact> premises = [];
        foreach (Rule rule in Program.Rules)
        {
            premises.Clear();
            if (rule.Proves(fact, Facts, premise => !premise.Equals(fact) && search.Proved(premise) && search.TryOrder(premise, out int before) && before < order, premises))
            {
                List<Proof> proofs = [];
                foreach (Fact premise in premises)
                {
                    proofs.Add(Build(premise, search, built));
                }

                Proof proof = new(fact, ProofStatus.Implicit, rule.Iri, proofs);
                built[fact] = proof;
                return proof;
            }
        }

        throw new InvalidOperationException("A proved fact has no instance whose premises were proved before it.");
    }

    /// <summary>The facts the closure holds, explicit, axioms and derived alike: its owner's accounting.</summary>
    public QuadCount Count => new(Facts.Count);

    /// <summary>Gives back to the budget every fact this closure holds: its owner is dropping it.</summary>
    public void Release()
    {
        Facts.Budget?.GiveAll(new QuadCount(Facts.Count));
        Facts.Budget = null;
    }

    private void ApplyDelta(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted)
    {
        List<Fact> gone = [];
        foreach (Fact fact in retracted)
        {
            if (_explicit.Remove(fact))
            {
                gone.Add(fact);
            }
        }

        if (gone.Count > 0)
        {
            Retract(gone);
        }

        List<Fact> added = [];
        foreach (Fact fact in asserted)
        {
            if (_explicit.Add(fact))
            {
                added.Add(fact);
            }
        }

        Insert(added);
    }

    /// <summary>
    /// Semi-naive insertion: each new fact is added, then joined once with
    /// every rule at each premise it matches, against the facts there by then
    /// (ADR 0196 point 1).
    /// </summary>
    internal void Insert(IEnumerable<Fact> facts)
    {
        if (Strategy == Retraction.RecursiveCounting)
        {
            CountingInsert(facts);
            return;
        }

        Propagation<Fact, Rule> pass = new(Program.Dependencies, Program.Strata, EqualityComparer<Fact>.Default);
        foreach (Fact fact in facts)
        {
            if (Facts.Add(fact))
            {
                pass.Seed(0, fact);
            }
        }

        List<Fact> heads = [];
        pass.Run((edge, fact, into) =>
        {
            heads.Clear();
            edge.Label.Fire(fact, Facts, heads);
            foreach (Fact head in heads)
            {
                if (Facts.Add(head))
                {
                    into.Add(head);
                }
            }
        });
    }

    private void Retract(List<Fact> gone)
    {
        switch (Strategy)
        {
            case Retraction.DeleteAndRederive:
                DeleteAndRederive(gone);
                break;
            case Retraction.BackwardForward:
                BackwardForward(gone);
                break;
            case Retraction.RecursiveCounting:
                CountingRetract(gone);
                break;
            case Retraction.FromScratch:
                Rebuild();
                break;
            default:
                throw new InvalidOperationException("an unknown retraction strategy");
        }
    }

    /// <summary>
    /// DRed: over-delete every fact a retracted one derives, through facts
    /// that are neither explicit nor axioms; then put back each one still
    /// derivable in one step from what is left, and insert from those.
    /// </summary>
    private void DeleteAndRederive(List<Fact> gone)
    {
        Propagation<Fact, Rule> overDeletion = new(Program.Dependencies, Program.Strata, EqualityComparer<Fact>.Default);
        foreach (Fact fact in gone)
        {
            if (Facts.Contains(fact))
            {
                overDeletion.Seed(0, fact);
            }
        }

        List<Fact> heads = [];
        overDeletion.Run((edge, fact, into) =>
        {
            heads.Clear();
            edge.Label.Fire(fact, Facts, heads);
            foreach (Fact head in heads)
            {
                if (Facts.Contains(head) && !IsGiven(head))
                {
                    into.Add(head);
                }
            }
        });

        IReadOnlySet<Fact> deleted = overDeletion.SetOf(0);
        foreach (Fact fact in deleted)
        {
            Facts.Remove(fact);
        }

        List<Fact> rederived = [];
        foreach (Fact fact in deleted)
        {
            if (IsGiven(fact) || Derivable(fact))
            {
                rederived.Add(fact);
            }
        }

        Insert(rederived);
    }

    /// <summary>
    /// Backward/Forward (Motik, Nenov, Piro and Horrocks, AAAI 2015): a fact
    /// that loses a premise is kept when a well-founded proof of it remains,
    /// searched backwards; one with none is deleted, and the facts it was a
    /// premise of are checked in turn.
    /// </summary>
    private void BackwardForward(List<Fact> gone)
    {
        Queue<Fact> pending = new(gone);
        List<Fact> heads = [];
        ProofSearch search = new(this);
        while (pending.TryDequeue(out Fact fact))
        {
            if (!Facts.Contains(fact) || IsGiven(fact))
            {
                continue;
            }

            if (search.Proved(fact))
            {
                continue;
            }

            heads.Clear();
            foreach (Rule rule in Program.Rules)
            {
                rule.Fire(fact, Facts, heads);
            }

            Facts.Remove(fact);
            foreach (Fact head in heads)
            {
                if (Facts.Contains(head) && !IsGiven(head))
                {
                    pending.Enqueue(head);
                }
            }
        }
    }

    /// <summary>Whether a fact is explicit, an axiom, or the TBox closure's beneath: a leaf of every proof here.</summary>
    internal bool IsGiven(in Fact fact) => _explicit.Contains(fact) || _axioms.Contains(fact) || (Beneath is { } tbox && tbox.Contains(fact));

    private bool Derivable(in Fact fact)
    {
        foreach (Rule rule in Program.Rules)
        {
            if (rule.Supports(fact, Facts))
            {
                return true;
            }
        }

        return false;
    }

    private void Rebuild()
    {
        List<Fact> all = [.. Facts.All];
        foreach (Fact fact in all)
        {
            Facts.Remove(fact);
        }

        Insert(Program.Axioms);
        Insert(_explicit);
    }
}

/// <summary>How a closure retracts (ADR 0196): the candidates measured, and the baseline.</summary>
internal enum Retraction
{
    /// <summary>The closure of what is left, from scratch: the baseline every candidate is measured against.</summary>
    FromScratch,

    /// <summary>DRed: over-delete, then rederive.</summary>
    DeleteAndRederive,

    /// <summary>Backward/Forward: delete only what has no proof left.</summary>
    BackwardForward,

    /// <summary>Recursive counting: each fact's round and the instances that derive it there.</summary>
    RecursiveCounting,
}
