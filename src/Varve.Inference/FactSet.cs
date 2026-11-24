// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference;

/// <summary>
/// The facts of a closure with the indices its rules join over: by subject,
/// by object, by predicate, by subject and predicate, and by predicate and
/// object (ADR 0195 point 2). In memory, over handles.
/// </summary>
/// <remarks>
/// <para>
/// Each fact is one record in a dense array: the fact, recursive counting's
/// round and count for it, and its links in the five indices. An index is a
/// map from its key to the first record under it, the rest chained through
/// the records both ways, so adding or removing a fact is constant time in
/// every index and no index holds a collection per key. A table of record
/// numbers, open addressed, finds a fact's record. A removed record is
/// reused.
/// </para>
/// <para>
/// Reading an answer while the set changes throws, as a .NET collection's
/// enumerator does.
/// </para>
/// </remarks>
internal sealed class FactSet
{
    private const int None = -1;
    private const int Subjects = 0, Predicates = 1, Objects = 2, SubjectPredicates = 3, PredicateObjects = 4;

    private Record[] _records = new Record[16];
    private int _used;
    private int _free = None;

    // Record numbers plus one, 0 for an empty slot; linear probing, removal by backward shift.
    private int[] _table = new int[32];
    private int _version;

    private readonly Dictionary<ulong, int> _bySubject = [];
    private readonly Dictionary<ulong, int> _byPredicate = [];
    private readonly Dictionary<ulong, int> _byObject = [];
    private readonly Dictionary<(ulong, ulong), int> _bySubjectPredicate = [];
    private readonly Dictionary<(ulong, ulong), int> _byPredicateObject = [];

    /// <summary>The facts this set holds itself, its base's not counted.</summary>
    internal int Count { get; private set; }

    /// <summary>
    /// A read-only set beneath this one, whose facts this one holds as its
    /// own but never adds, removes or counts: the TBox closure under a data
    /// graph's (ADR 0195 point 3). It does not change while this set lives.
    /// </summary>
    internal FactSet? Base { get; init; }

    // What this set has taken from its budget, and what its pass's record of changes has: each given back whole.
    private long _charged;
    private long _touchedCharge;

    /// <summary>The budget this set takes from before it allocates, if the closure has one.</summary>
    internal FactBudget? Budget { get; private set; }

    /// <summary>Takes from <paramref name="budget"/> what the set holds already, and asks it before every allocation after.</summary>
    /// <exception cref="ClosureLimitExceededException">What the set holds already would pass the budget.</exception>
    internal void Charge(FactBudget budget)
    {
        long held = (_records.Length * (long)Unsafe.SizeOf<Record>()) + (_table.Length * (long)sizeof(int)) + (Count * budget.Costs.PerFact.Value);
        budget.Take(new MemoryAmount(held));
        Budget = budget;
        _charged = held;
    }

    /// <summary>Gives back to the budget everything the set took: its owner is dropping it.</summary>
    internal void Uncharge()
    {
        Budget?.Give(new MemoryAmount(_charged + _touchedCharge));
        _charged = 0;
        _touchedCharge = 0;
        Budget = null;
    }

    /// <summary>While a pass records its changes: each fact added or removed, and whether it was held before its first change; each entry charged until the record is put down.</summary>
    internal Dictionary<Fact, bool>? Touched
    {
        get;
        set
        {
            if (value is null)
            {
                Budget?.Give(new MemoryAmount(_touchedCharge));
                _touchedCharge = 0;
            }

            field = value;
        }
    }

    /// <summary>The facts this set holds itself, its base's not among them.</summary>
    internal IEnumerable<Fact> All
    {
        get
        {
            int version = _version;
            for (int i = 0; i < _used; i++)
            {
                Check(version);
                if (_records[i].Held)
                {
                    yield return _records[i].Fact;
                }
            }
        }
    }

    internal bool Contains(in Fact fact) => Find(fact) != None || (Base is { } under && under.Contains(fact));

    /// <summary>Whether this set holds a fact itself, not through its base.</summary>
    internal bool Holds(in Fact fact) => Find(fact) != None;

    internal bool Add(in Fact fact)
    {
        if (Find(fact) != None || (Base is { } under && under.Contains(fact)))
        {
            return false;
        }

        // Every allocation below is asked of the budget first: the fact, a pass's record of it, and either array's growth.
        Take(Budget?.Costs.PerFact.Value ?? 0);
        Note(fact, false);
        if ((Count + 1) * 4 > _table.Length * 3)
        {
            Grow();
        }

        int at = _free;
        if (at != None)
        {
            _free = _records[at].Next(Subjects);
        }
        else
        {
            if (_used == _records.Length)
            {
                long size = Unsafe.SizeOf<Record>();
                Take(_records.Length * 2 * size);
                Array.Resize(ref _records, _records.Length * 2);
                Give(_records.Length / 2 * size);
            }

            at = _used++;
        }

        _records[at] = new Record(fact);
        Count++;
        _version++;
        Place(at);

        Link(_bySubject, fact.Subject.Value, at, Subjects);
        Link(_byPredicate, fact.Predicate.Value, at, Predicates);
        Link(_byObject, fact.Object.Value, at, Objects);
        Link(_bySubjectPredicate, (fact.Subject.Value, fact.Predicate.Value), at, SubjectPredicates);
        Link(_byPredicateObject, (fact.Predicate.Value, fact.Object.Value), at, PredicateObjects);
        return true;
    }

    internal bool Remove(in Fact fact)
    {
        int at = Find(fact);
        if (at == None)
        {
            return false;
        }

        Note(fact, true);
        Give(Budget?.Costs.PerFact.Value ?? 0);

        Unlink(_bySubject, fact.Subject.Value, at, Subjects);
        Unlink(_byPredicate, fact.Predicate.Value, at, Predicates);
        Unlink(_byObject, fact.Object.Value, at, Objects);
        Unlink(_bySubjectPredicate, (fact.Subject.Value, fact.Predicate.Value), at, SubjectPredicates);
        Unlink(_byPredicateObject, (fact.Predicate.Value, fact.Object.Value), at, PredicateObjects);

        Displace(fact);
        _records[at] = default;
        _records[at].SetNext(Subjects, _free);
        _free = at;
        Count--;
        _version++;
        return true;
    }

    /// <summary>
    /// The facts matching a pattern, <see cref="TermHandle.None"/> a wildcard,
    /// from the narrowest index that answers it. The caller does not change
    /// the set while it reads the answer.
    /// </summary>
    internal IEnumerable<Fact> Match(TermHandle subject, TermHandle predicate, TermHandle @object) =>
        Base is { } under ? Both(Own(subject, predicate, @object), under.Match(subject, predicate, @object)) : Own(subject, predicate, @object);

    private static IEnumerable<Fact> Both(IEnumerable<Fact> own, IEnumerable<Fact> under)
    {
        foreach (Fact fact in own)
        {
            yield return fact;
        }

        foreach (Fact fact in under)
        {
            yield return fact;
        }
    }

    private IEnumerable<Fact> Own(TermHandle subject, TermHandle predicate, TermHandle @object)
    {
        bool s = !subject.IsNone, p = !predicate.IsNone, o = !@object.IsNone;
        if (s && p && o)
        {
            Fact fact = new(subject, predicate, @object);
            return Find(fact) != None ? [fact] : [];
        }

        if (s && p)
        {
            return Chain(_bySubjectPredicate, (subject.Value, predicate.Value), SubjectPredicates, 0);
        }

        if (p && o)
        {
            return Chain(_byPredicateObject, (predicate.Value, @object.Value), PredicateObjects, 0);
        }

        if (s)
        {
            return Chain(_bySubject, subject.Value, Subjects, o ? @object.Value : 0);
        }

        if (p)
        {
            return Chain(_byPredicate, predicate.Value, Predicates, 0);
        }

        return o ? Chain(_byObject, @object.Value, Objects, 0) : All;
    }

    /// <summary>The records chained under a key in one index; with <paramref name="object"/> not 0, only those with that object.</summary>
    private IEnumerable<Fact> Chain<TKey>(Dictionary<TKey, int> index, TKey key, int which, ulong @object)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out int at))
        {
            yield break;
        }

        int version = _version;
        while (at != None)
        {
            Fact fact = _records[at].Fact;
            int next = _records[at].Next(which);
            if (@object == 0 || fact.Object.Value == @object)
            {
                yield return fact;
                Check(version);
            }

            at = next;
        }
    }

    private void Check(int version)
    {
        if (version != _version)
        {
            throw new InvalidOperationException("The fact set changed while an answer of it was read.");
        }
    }

    private void Link<TKey>(Dictionary<TKey, int> index, TKey key, int at, int which)
        where TKey : notnull
    {
        ref Record record = ref _records[at];
        record.SetPrevious(which, None);
        if (index.TryGetValue(key, out int head))
        {
            record.SetNext(which, head);
            _records[head].SetPrevious(which, at);
        }
        else
        {
            record.SetNext(which, None);
        }

        index[key] = at;
    }

    private void Unlink<TKey>(Dictionary<TKey, int> index, TKey key, int at, int which)
        where TKey : notnull
    {
        int previous = _records[at].Previous(which), next = _records[at].Next(which);
        if (next != None)
        {
            _records[next].SetPrevious(which, previous);
        }

        if (previous != None)
        {
            _records[previous].SetNext(which, next);
        }
        else if (next != None)
        {
            index[key] = next;
        }
        else
        {
            index.Remove(key);
        }
    }

    private int Held(in Fact fact)
    {
        int at = Find(fact);
        return at != None ? at : throw new InvalidOperationException("The fact set does not hold " + fact + " itself.");
    }

    private int Find(in Fact fact)
    {
        int mask = _table.Length - 1;
        for (int slot = Slot(fact, mask); ; slot = (slot + 1) & mask)
        {
            int entry = _table[slot];
            if (entry == 0)
            {
                return None;
            }

            if (_records[entry - 1].Fact == fact)
            {
                return entry - 1;
            }
        }
    }

    private void Place(int at)
    {
        int mask = _table.Length - 1;
        int slot = Slot(_records[at].Fact, mask);
        while (_table[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }

        _table[slot] = at + 1;
    }

    /// <summary>Takes a fact out of the table, shifting back the entries its slot displaced.</summary>
    private void Displace(in Fact fact)
    {
        int mask = _table.Length - 1;
        int hole = Slot(fact, mask);
        while (_records[_table[hole] - 1].Fact != fact)
        {
            hole = (hole + 1) & mask;
        }

        for (int slot = (hole + 1) & mask; _table[slot] != 0; slot = (slot + 1) & mask)
        {
            int home = Slot(_records[_table[slot] - 1].Fact, mask);

            // The entry may move into the hole if its home is not in (hole, slot], cyclically.
            if (hole <= slot ? home <= hole || home > slot : home <= hole && home > slot)
            {
                _table[hole] = _table[slot];
                hole = slot;
            }
        }

        _table[hole] = 0;
    }

    private void Take(long bytes)
    {
        if (Budget is { } budget && bytes > 0)
        {
            budget.Take(new MemoryAmount(bytes));
            _charged += bytes;
        }
    }

    private void Give(long bytes)
    {
        if (Budget is { } budget && bytes > 0)
        {
            budget.Give(new MemoryAmount(bytes));
            _charged -= bytes;
        }
    }

    /// <summary>Records a change in the pass's record, the entry charged first.</summary>
    private void Note(in Fact fact, bool held)
    {
        if (Touched is not { } touched || touched.ContainsKey(fact))
        {
            return;
        }

        if (Budget is { } budget)
        {
            budget.Take(budget.Costs.PerTouched);
            _touchedCharge += budget.Costs.PerTouched.Value;
        }

        touched[fact] = held;
    }

    private void Grow()
    {
        Take(_table.Length * 2L * sizeof(int));
        int[] old = _table;
        _table = new int[_table.Length * 2];
        Give(old.Length * (long)sizeof(int));
        for (int i = 0; i < _used; i++)
        {
            if (_records[i].Held)
            {
                Place(i);
            }
        }
    }

    private static int Slot(in Fact fact, int mask)
    {
        ulong hash = (fact.Subject.Value * 0x9E3779B97F4A7C15UL) ^ (fact.Predicate.Value * 0xC2B2AE3D27D4EB4FUL) ^ (fact.Object.Value * 0x165667B19E3779F9UL);
        return (int)((hash ^ (hash >> 32)) & (uint)mask);
    }

    /// <summary>
    /// A fact and its links in the five indices: 64 bytes. It carried
    /// recursive counting's round and count besides, 8 bytes more, until
    /// DRed was chosen (ADR 0196); DRed needs neither.
    /// </summary>
    private struct Record
    {
        private int _nextSubject, _previousSubject, _nextPredicate, _previousPredicate, _nextObject, _previousObject;
        private int _nextSubjectPredicate, _previousSubjectPredicate, _nextPredicateObject, _previousPredicateObject;

        internal Record(in Fact fact)
        {
            Fact = fact;
        }

        internal Fact Fact { get; }

        /// <summary>Whether the record holds a fact: a fact's subject is never <see cref="TermHandle.None"/>.</summary>
        internal readonly bool Held => !Fact.Subject.IsNone;

        internal readonly int Next(int which) => which switch
        {
            Subjects => _nextSubject,
            Predicates => _nextPredicate,
            Objects => _nextObject,
            SubjectPredicates => _nextSubjectPredicate,
            _ => _nextPredicateObject,
        };

        internal readonly int Previous(int which) => which switch
        {
            Subjects => _previousSubject,
            Predicates => _previousPredicate,
            Objects => _previousObject,
            SubjectPredicates => _previousSubjectPredicate,
            _ => _previousPredicateObject,
        };

        internal void SetNext(int which, int value)
        {
            switch (which)
            {
                case Subjects: _nextSubject = value; break;
                case Predicates: _nextPredicate = value; break;
                case Objects: _nextObject = value; break;
                case SubjectPredicates: _nextSubjectPredicate = value; break;
                default: _nextPredicateObject = value; break;
            }
        }

        internal void SetPrevious(int which, int value)
        {
            switch (which)
            {
                case Subjects: _previousSubject = value; break;
                case Predicates: _previousPredicate = value; break;
                case Objects: _previousObject = value; break;
                case SubjectPredicates: _previousSubjectPredicate = value; break;
                default: _previousPredicateObject = value; break;
            }
        }
    }
}
