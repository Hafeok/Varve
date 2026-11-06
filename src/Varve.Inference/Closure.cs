// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Derivation;
using Varve.Inference.Model;

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
public sealed class Closure
{
    private readonly HashSet<Fact> _explicit = [];
    private readonly HashSet<Fact> _axioms = [];

    /// <summary>The empty graph's closure under <paramref name="profile"/>: its axioms and what they entail.</summary>
    public Closure(InferenceProfile profile, IInferenceTerms terms)
        : this(Program.Of(profile, terms), Retraction.DeleteAndRederive)
    {
    }

    internal Closure(Program program, Retraction retraction)
    {
        Program = program;
        Strategy = retraction;
        foreach (Fact axiom in program.Axioms)
        {
            _axioms.Add(axiom);
        }

        Insert(program.Axioms);
    }

    internal Retraction Strategy { get; }

    /// <summary>Whether the closure holds a fact.</summary>
    public bool Contains(Fact fact) => Facts.Contains(fact);

    /// <summary>Whether a fact is explicit: asserted, and not since retracted.</summary>
    public bool IsExplicit(Fact fact) => _explicit.Contains(fact);

    /// <summary>The facts the closure holds that are not explicit, generalised ones included.</summary>
    public IEnumerable<Fact> Implicit
    {
        get
        {
            foreach (Fact fact in Facts.All)
            {
                if (!_explicit.Contains(fact))
                {
                    yield return fact;
                }
            }
        }
    }

    /// <summary>Every fact of the closure.</summary>
    public IEnumerable<Fact> All => Facts.All;

    internal FactSet Facts { get; } = new();

    internal Program Program { get; }

    /// <summary>
    /// Applies an exact delta: <paramref name="retracted"/> were explicit and
    /// are not, <paramref name="asserted"/> were not and are. The closure
    /// after equals the closure of the explicit facts after, from scratch
    /// (ADR 0195 point 9).
    /// </summary>
    public void Apply(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted)
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
                if (Facts.Contains(head) && !_explicit.Contains(head) && !_axioms.Contains(head))
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
            if (_explicit.Contains(fact) || Derivable(fact))
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
            if (!Facts.Contains(fact) || _explicit.Contains(fact) || _axioms.Contains(fact))
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
                if (Facts.Contains(head) && !_explicit.Contains(head) && !_axioms.Contains(head))
                {
                    pending.Enqueue(head);
                }
            }
        }
    }

    /// <summary>Whether a fact is explicit or an axiom: a leaf of every proof.</summary>
    internal bool IsGiven(in Fact fact) => _explicit.Contains(fact) || _axioms.Contains(fact);

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
}
