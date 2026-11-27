// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
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

    // The explicit rdf:first and rdf:rest statements of this closure, which its lists are read from with the TBox closure's (ADR 0197, amended 2026-11-24).
    private readonly HashSet<Fact> _listStatements = [];

    /// <summary>The empty graph's closure under <paramref name="profile"/>: its axioms and what they entail.</summary>
    public Closure(InferenceProfile profile, IInferenceTerms terms)
        : this(Program.Of(profile, terms), Retraction.DeleteAndRederive)
    {
    }

    /// <summary>
    /// The empty graph's closure under <paramref name="profile"/>, its facts
    /// taken from <paramref name="budget"/>, which the closures of one binding
    /// share (ADR 0195 point 6).
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">The axioms alone would pass the budget.</exception>
    public Closure(InferenceProfile profile, IInferenceTerms terms, FactBudget budget)
        : this(Program.Of(profile, terms), Retraction.DeleteAndRederive, budget)
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
        : this(Program.Of(profile, terms), Retraction.DeleteAndRederive, budget, tbox)
    {
    }

    internal Closure(Program program, Retraction retraction, FactBudget? budget = null, Closure? tbox = null)
    {
        Program = program;
        Strategy = retraction;
        Beneath = tbox;
        Facts = new FactSet { Base = tbox?.Facts };
        if (budget is not null)
        {
            Facts.Charge(budget);
        }

        foreach (Fact axiom in program.Axioms)
        {
            _axioms.Add(axiom);
        }

        if (program.List is not null)
        {
            ReadLists(_listStatements);
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
    /// The cells of asserted lists the list rules and checks do not read
    /// (ADR 0197, amended 2026-11-24), each with its reason: those this
    /// closure's statements make unread, a data graph's beyond the TBox
    /// closure's beneath it. Empty for a profile without list rules.
    /// </summary>
    public IReadOnlyList<UnreadList> UnreadLists()
    {
        if (Facts.Lists is not { } lists)
        {
            return [];
        }

        if (Beneath?.Facts.Lists is not { } beneath || beneath.Unread.Count == 0)
        {
            return lists.Unread;
        }

        HashSet<UnreadList> under = [.. beneath.Unread];
        List<UnreadList> own = [];
        foreach (UnreadList unread in lists.Unread)
        {
            if (!under.Contains(unread))
            {
                own.Add(unread);
            }
        }

        return own;
    }

    /// <summary>
    /// How many times a commit has changed the lists this closure reads, each
    /// rebuilding it (ADR 0197, amended 2026-11-24). What is kept beside the
    /// closure and drawn from its rules' instances, such as premise sets, is
    /// computed again when this moves: an instance can come or go with no
    /// change to its premises.
    /// </summary>
    public ListReadings ListReadings { get; private set; }

    internal FactSet Facts { get; }

    internal Program Program { get; }

    // What the reading of the lists has taken from the budget: a working entry a statement read.
    private long _listCharge;

    /// <summary>
    /// Reads the lists from <paramref name="statements"/>, this closure's
    /// explicit list statements, with the TBox closure's beneath, charging
    /// the budget a working entry a statement before the reading is made.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">The reading would pass the budget.</exception>
    private AssertedLists ReadLists(IReadOnlyCollection<Fact> statements)
    {
        ListTerms list = Program.List!.Value;
        // A statement the data graph and the TBox graphs both assert is one statement.
        HashSet<Fact> all = [.. statements];
        AssertedLists? beneath = null;
        if (Beneath is { } tbox)
        {
            all.UnionWith(tbox._listStatements);
            beneath = tbox.Facts.Lists;
        }

        long charge = 0;
        if (Facts.Budget is { } budget)
        {
            charge = all.Count * budget.Costs.PerWorkingEntry.Value;
            budget.Take(new MemoryAmount(charge));
            budget.Give(new MemoryAmount(_listCharge));
        }

        _listCharge = charge;
        AssertedLists lists = AssertedLists.Read(all, list, beneath);
        Facts.Lists = lists;
        return lists;
    }

    /// <summary>
    /// Whether a delta changes the lists the rules read. When it does, the
    /// delta is applied to the explicit facts, the lists are read again, and
    /// the closure is rebuilt from them, since a list read differently is a
    /// different input to every list rule (ADR 0197, amended 2026-11-24);
    /// when it does not, only the reading of the cells left unread changes.
    /// </summary>
    private bool RebuiltForLists(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted)
    {
        if (Program.List is not { } list)
        {
            return false;
        }

        HashSet<Fact>? next = null;
        foreach (Fact fact in retracted)
        {
            if (AssertedLists.IsListStatement(fact, list) && _listStatements.Contains(fact))
            {
                next ??= [.. _listStatements];
                next.Remove(fact);
            }
        }

        foreach (Fact fact in asserted)
        {
            if (AssertedLists.IsListStatement(fact, list) && !_listStatements.Contains(fact))
            {
                next ??= [.. _listStatements];
                next.Add(fact);
            }
        }

        if (next is null)
        {
            return false;
        }

        AssertedLists before = Facts.Lists!;
        AssertedLists after = ReadLists(next);
        _listStatements.Clear();
        _listStatements.UnionWith(next);
        if (after.SameReading(before))
        {
            return false;
        }

        foreach (Fact fact in retracted)
        {
            if (_explicit.Remove(fact))
            {
                GiveExplicit();
            }
        }

        foreach (Fact fact in asserted)
        {
            if (!_explicit.Contains(fact))
            {
                TakeExplicit();
                _explicit.Add(fact);
            }
        }

        ListReadings = new ListReadings(ListReadings.Value + 1);
        Rebuild();
        return true;
    }

    /// <summary>
    /// Applies an exact delta: <paramref name="retracted"/> were explicit and
    /// are not, <paramref name="asserted"/> were not and are. The closure
    /// after equals the closure of the explicit facts after, from scratch
    /// (ADR 0195 point 9).
    /// </summary>
    /// <returns>
    /// The facts whose membership in the closure changed: each one held
    /// before and not after, or after and not before. For a closure that
    /// held nothing of its own before, a view of what it holds, read before
    /// it next changes.
    /// </returns>
    /// <exception cref="ClosureLimitExceededException">
    /// The closure would pass its budget. It is then partly applied, and its
    /// owner drops it.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled; seen within a
    /// thousand rule firings (ADR 0195 point 6, amended 2026-11-25). The
    /// closure is then partly applied, and its owner drops it.
    /// </exception>
    public IReadOnlyCollection<Fact> Apply(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cancellation = cancellationToken;
        try
        {
            return ApplyOnce(asserted, retracted);
        }
        finally
        {
            _cancellation = CancellationToken.None;
        }
    }

    // The token of the Apply running, seen every thousand firings.
    private CancellationToken _cancellation;
    private int _fired;

    /// <summary>Called for each rule firing of a pass: throws within a thousand firings of a cancellation.</summary>
    internal void Fired()
    {
        if ((++_fired & 1023) == 0)
        {
            _cancellation.ThrowIfCancellationRequested();
        }
    }

    private IReadOnlyCollection<Fact> ApplyOnce(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted)
    {
        if (Facts.Count == 0 && retracted.IsEmpty)
        {
            // A closure holding nothing of its own, a data graph's or a merge's
            // from scratch: every fact it holds after is a change, so none is
            // recorded on the way, which for a large one costs as much again.
            ApplyDelta(asserted, retracted);
            ForgetClashes();
            return new Held(Facts);
        }

        ListReadings readings = ListReadings;
        Dictionary<Fact, bool> touched = [];
        Facts.Touched = touched;
        bool applied = false;
        try
        {
            ApplyDelta(asserted, retracted);
            applied = true;
        }
        finally
        {
            Facts.Touched = null;
            if (!applied)
            {
                // Partly applied: the clashes kept are no longer this closure's.
                ForgetClashes();
            }
        }

        List<Fact> changed = [];
        foreach ((Fact fact, bool held) in touched)
        {
            if (Facts.Contains(fact) != held)
            {
                changed.Add(fact);
            }
        }

        Maintain(changed, ListReadings != readings ? ListsRead.Differently : ListsRead.AsBefore);
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

    /// <summary>Gives back to the budget everything this closure took: its owner is dropping it.</summary>
    public void Release()
    {
        _clashes?.Release();
        _clashes = null;
        Facts.Budget?.Give(new MemoryAmount(_explicitCharge + _listCharge));
        _explicitCharge = 0;
        _listCharge = 0;
        Facts.Uncharge();
    }

    // What the set of explicit facts has taken from the budget.
    private long _explicitCharge;

    /// <summary>Charges an explicit fact before it is recorded.</summary>
    private void TakeExplicit()
    {
        if (Facts.Budget is { } budget)
        {
            budget.Take(budget.Costs.PerExplicit);
            _explicitCharge += budget.Costs.PerExplicit.Value;
        }
    }

    private void GiveExplicit()
    {
        if (Facts.Budget is { } budget)
        {
            budget.Give(budget.Costs.PerExplicit);
            _explicitCharge -= budget.Costs.PerExplicit.Value;
        }
    }

    private void ApplyDelta(ReadOnlySpan<Fact> asserted, ReadOnlySpan<Fact> retracted)
    {
        if (RebuiltForLists(asserted, retracted))
        {
            return;
        }

        List<Fact> gone = [];
        foreach (Fact fact in retracted)
        {
            if (_explicit.Remove(fact))
            {
                GiveExplicit();
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
            Fired();
            if (!_explicit.Contains(fact))
            {
                TakeExplicit();
                _explicit.Add(fact);
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
    internal void Insert(IEnumerable<Fact> facts) => Strategy.Insert(this, facts);

    /// <summary>
    /// Semi-naive insertion, every strategy's but one that keeps state of its
    /// own: each new fact is added, then joined once with every rule at each
    /// premise it matches. The pass's sets hold each fact it adds while it
    /// runs, and are charged to the budget per entry (ADR 0195 point 6).
    /// </summary>
    internal void SemiNaiveInsert(IEnumerable<Fact> facts)
    {
        using Working working = new(Facts.Budget);
        Propagation<Fact, Rule> pass = new(Program.Dependencies, Program.Strata, EqualityComparer<Fact>.Default);
        foreach (Fact fact in facts)
        {
            Fired();
            if (Facts.Add(fact))
            {
                working.Take();
                pass.Seed(0, fact);
            }
        }

        List<Fact> heads = [];
        pass.Run((edge, fact, into) =>
        {
            Fired();
            heads.Clear();
            edge.Label.Fire(fact, Facts, heads);
            foreach (Fact head in heads)
            {
                if (Facts.Add(head))
                {
                    working.Take();
                    into.Add(head);
                }
            }
        });
    }

    private void Retract(List<Fact> gone) => Strategy.Retract(this, gone);

    /// <summary>
    /// DRed: over-delete every fact a retracted one derives, through facts
    /// that are neither explicit nor axioms; then put back each one still
    /// derivable in one step from what is left, and insert from those.
    /// </summary>
    internal void DeleteAndRederive(List<Fact> gone)
    {
        // The over-deletion's sets, and the list of what is put back, are charged per entry while they live.
        using Working working = new(Facts.Budget);
        Propagation<Fact, Rule> overDeletion = new(Program.Dependencies, Program.Strata, EqualityComparer<Fact>.Default);
        foreach (Fact fact in gone)
        {
            if (Facts.Contains(fact))
            {
                working.Take();
                overDeletion.Seed(0, fact);
            }
        }

        List<Fact> heads = [];
        overDeletion.Run((edge, fact, into) =>
        {
            Fired();
            heads.Clear();
            edge.Label.Fire(fact, Facts, heads);
            foreach (Fact head in heads)
            {
                if (Facts.Contains(head) && !IsGiven(head))
                {
                    working.Take();
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
            Fired();
            if (IsGiven(fact) || Derivable(fact))
            {
                working.Take();
                rederived.Add(fact);
            }
        }

        Insert(rederived);
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

    /// <summary>
    /// A pass's working sets' charge (ADR 0195 point 6): each entry taken from
    /// the budget before it is stored, given back when it goes, and what is
    /// left given back when the pass ends, however it ends.
    /// </summary>
    internal sealed class Working(FactBudget? budget) : IDisposable
    {
        private long _held;

        /// <exception cref="ClosureLimitExceededException">The entry would pass the budget.</exception>
        internal void Take()
        {
            if (budget is not null)
            {
                budget.Take(budget.Costs.PerWorkingEntry);
                _held += budget.Costs.PerWorkingEntry.Value;
            }
        }

        internal void Give()
        {
            if (budget is not null && _held > 0)
            {
                budget.Give(budget.Costs.PerWorkingEntry);
                _held -= budget.Costs.PerWorkingEntry.Value;
            }
        }

        public void Dispose()
        {
            budget?.Give(new MemoryAmount(_held));
            _held = 0;
        }
    }

    internal void Rebuild()
    {
        List<Fact> all = [.. Facts.All];
        foreach (Fact fact in all)
        {
            Facts.Remove(fact);
        }

        Strategy.Rebuilding(this);

        Insert(Program.Axioms);
        Insert(_explicit);
    }
}

/// <summary>The facts a closure holds itself, as a collection: what <see cref="Closure.Apply"/> answers for one that held nothing before.</summary>
internal sealed class Held(FactSet facts) : IReadOnlyCollection<Fact>
{
    public int Count => facts.Count;

    public IEnumerator<Fact> GetEnumerator() => facts.All.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// How a closure retracts (ADR 0196): DRed, chosen by the maintainer on the
/// measurement, and the from-scratch baseline every strategy is held to. The
/// candidates not chosen, recursive counting and Backward/Forward, live with
/// the benchmarks (<c>tests/Varve.Benchmarks/Retraction</c>), where the
/// measurement can be rerun; Backward/Forward's proof search stays here, as
/// explain's (ADR 0199).
/// </summary>
[Contract(typeof(RetractionDecidedByMeasurement.ChosenOnTheMeasurement), Role = "how a closure retracts: DRed, or the from-scratch baseline")]
internal abstract class Retraction
{
    /// <summary>DRed: over-delete, then rederive.</summary>
    internal static Retraction DeleteAndRederive { get; } = new Dred();

    /// <summary>The closure of what is left, from scratch: the baseline every strategy is measured against.</summary>
    internal static Retraction FromScratch { get; } = new Scratch();

    /// <summary>Adds <paramref name="facts"/> and what they entail; semi-naive unless a strategy keeps state of its own.</summary>
    internal virtual void Insert(Closure closure, IEnumerable<Fact> facts) => closure.SemiNaiveInsert(facts);

    /// <summary>Takes out what <paramref name="gone"/>, no longer explicit, alone supported.</summary>
    internal abstract void Retract(Closure closure, List<Fact> gone);

    /// <summary>The closure has dropped every fact it held and is about to insert its axioms and explicit facts again: a strategy forgets what it kept of them.</summary>
    internal virtual void Rebuilding(Closure closure)
    {
    }

    private sealed class Dred : Retraction
    {
        internal override void Retract(Closure closure, List<Fact> gone) => closure.DeleteAndRederive(gone);
    }

    private sealed class Scratch : Retraction
    {
        internal override void Retract(Closure closure, List<Fact> gone) => closure.Rebuild();
    }
}
