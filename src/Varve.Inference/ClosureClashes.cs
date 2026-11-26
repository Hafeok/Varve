// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;

namespace Varve.Inference;

/// <summary>The clashes of a closure, kept from the facts each pass changes.</summary>
public sealed partial class Closure
{
    // Null until the first call after a build, which matches every check in full.
    private KeptClashes? _clashes;

    /// <summary>
    /// Every clash the profile's checks find over the closure (ADR 0197
    /// point 5), each check and set of premises once. A clash changes nothing
    /// the closure holds: it is reported, never served as everything.
    /// </summary>
    /// <remarks>
    /// The first call after a build matches every check over the closure; each
    /// call after costs what the passes since changed, not what the closure
    /// holds (ADR 0195 point 6, amended 2026-11-25).
    /// </remarks>
    public IReadOnlyList<Clash> Clashes()
    {
        _clashes ??= new KeptClashes(Program, Facts);
        return _clashes.All();
    }

    // Keeps a pass's changes: the facts whose membership changed, each held before or after and not both.
    private void Maintain(List<Fact> changed, ListsRead lists) => _clashes?.Maintain(changed, Facts, lists);
}

/// <summary>
/// A closure's clashes, by check, each set of premises once, kept from the
/// facts each pass changes: a clash with a premise that left goes, and a
/// match that uses a fact that came is added. A check over a list's members
/// is matched again in full.
/// </summary>
[Contract(typeof(TheInferenceProjection.ClashesKeptPerPass), Role = "a closure's clashes, kept per pass")]
internal sealed class KeptClashes
{
    private readonly Check[] _checks;
    private readonly Dictionary<string, Kept>[] _kept;

    // Each premise of a kept clash, to the clashes it is a premise of.
    private readonly Dictionary<Fact, List<Kept>> _byPremise = [];

    /// <summary>Every check of <paramref name="program"/> matched over <paramref name="facts"/>: the clashes after a build.</summary>
    internal KeptClashes(Program program, FactSet facts)
    {
        Check[] checks = program.Checks;
        _checks = checks;
        _kept = new Dictionary<string, Kept>[checks.Length];
        List<Fact[]> matches = [];
        for (int i = 0; i < checks.Length; i++)
        {
            _kept[i] = [];
            matches.Clear();
            checks[i].Matches(facts, matches);
            Keep(i, matches);
        }
    }

    internal IReadOnlyList<Clash> All()
    {
        List<Clash> clashes = [];
        foreach (Dictionary<string, Kept> check in _kept)
        {
            foreach (Kept kept in check.Values)
            {
                clashes.Add(kept.Clash);
            }
        }

        return clashes;
    }

    /// <summary>
    /// Keeps a pass's <paramref name="changed"/> facts, each now in
    /// <paramref name="facts"/> or not. When the pass read the closure's lists
    /// differently, a check over a list's members is matched again in full.
    /// </summary>
    internal void Maintain(List<Fact> changed, FactSet facts, ListsRead lists)
    {
        if (changed.Count == 0)
        {
            return;
        }

        List<Fact> came = [];
        foreach (Fact fact in changed)
        {
            if (facts.Contains(fact))
            {
                came.Add(fact);
            }
            else if (_byPremise.TryGetValue(fact, out List<Kept>? gone))
            {
                foreach (Kept kept in gone.ToArray())
                {
                    Drop(kept);
                }
            }
        }

        List<Fact[]> matches = [];
        for (int i = 0; i < _checks.Length; i++)
        {
            Check check = _checks[i];
            matches.Clear();
            if (check.Anchored && !(lists == ListsRead.Differently && check.ReadsLists))
            {
                foreach (Fact fact in came)
                {
                    check.MatchesAt(fact, facts, matches);
                }
            }
            else
            {
                // Matched again in full: a check that cannot be anchored, or one over lists read differently.
                foreach (Kept kept in new List<Kept>(_kept[i].Values))
                {
                    Drop(kept);
                }

                check.Matches(facts, matches);
            }

            Keep(i, matches);
        }
    }

    private void Keep(int check, List<Fact[]> matches)
    {
        Dictionary<string, Kept> kept = _kept[check];
        foreach (Fact[] premises in matches)
        {
            string key = KeyOf(premises);
            if (kept.ContainsKey(key))
            {
                continue;
            }

            Kept clash = new(check, key, new Clash(_checks[check].Iri, premises));
            kept.Add(key, clash);
            foreach (Fact premise in premises)
            {
                if (!_byPremise.TryGetValue(premise, out List<Kept>? of))
                {
                    of = [];
                    _byPremise.Add(premise, of);
                }

                if (!of.Contains(clash))
                {
                    of.Add(clash);
                }
            }
        }
    }

    private void Drop(Kept kept)
    {
        if (!_kept[kept.Check].Remove(kept.Key))
        {
            return;
        }

        foreach (Fact premise in kept.Clash.Premises)
        {
            if (_byPremise.TryGetValue(premise, out List<Kept>? of))
            {
                of.Remove(kept);
                if (of.Count == 0)
                {
                    _byPremise.Remove(premise);
                }
            }
        }
    }

    // One set of premises, in any order of the body, as one key.
    private static string KeyOf(Fact[] premises)
    {
        Fact[] ordered = [.. premises];
        Array.Sort(ordered, static (x, y) =>
            x.Subject.Value != y.Subject.Value ? x.Subject.Value.CompareTo(y.Subject.Value)
            : x.Predicate.Value != y.Predicate.Value ? x.Predicate.Value.CompareTo(y.Predicate.Value)
            : x.Object.Value.CompareTo(y.Object.Value));
        return string.Join(' ', ordered);
    }

    private sealed record Kept(int Check, string Key, Clash Clash);
}

/// <summary>Whether a pass read a closure's lists as before, or differently and rebuilt it (ADR 0197, amended 2026-11-24).</summary>
internal enum ListsRead
{
    /// <summary>The same cells, from the same statements.</summary>
    AsBefore,

    /// <summary>Differently: the closure was rebuilt from them.</summary>
    Differently,
}
