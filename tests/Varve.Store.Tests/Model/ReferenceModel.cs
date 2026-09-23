// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Rdf;

namespace Varve.Store.Tests.Model;

/// <summary>A quad of terms, the model's unit. Blank nodes carry model labels, unique for the run.</summary>
internal readonly record struct MQuad(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G);

/// <summary>What the model says a request should come to.</summary>
internal readonly record struct Expected(CommitOutcome Outcome, long Position);

/// <summary>
/// The reference model of ADR 0043: specification §3 and T1 restated as naively
/// as possible, over terms rather than ids, sharing no code with the store.
/// </summary>
/// <remarks>
/// <c>D_P = D_{P-1} ∪ alloc_P</c>, <c>G_P = (G_{P-1} \ R_P) ∪ A_P</c>. The
/// dictionary is a set of terms; which terms are inline and therefore never
/// allocated is ADR 0012's rule, restated here from its text.
/// </remarks>
internal sealed class ReferenceModel
{
    private readonly HashSet<RdfTerm> _known = [];

    public HashSet<MQuad> Graph { get; private set; } = [];

    /// <summary><c>G_P</c> for every position, index = position.</summary>
    public List<HashSet<MQuad>> History { get; } = [[]];

    /// <summary>The default access scope at every position.</summary>
    public List<AccessScope> Settings { get; } = [AccessScope.AllHistory];

    /// <summary><c>alloc_P</c> as terms, index = position.</summary>
    public List<HashSet<RdfTerm>> Allocated { get; } = [[]];

    /// <summary>The effective delta at every position.</summary>
    public List<(HashSet<MQuad> A, HashSet<MQuad> R)> Deltas { get; } = [([], [])];

    /// <summary>Timestamps in ticks, index = position; position 0 has none.</summary>
    public List<long> Timestamps { get; } = [long.MinValue];

    /// <summary>Blank nodes that exist, as model labels, in allocation order.</summary>
    public List<RdfTerm> Blanks { get; } = [];

    public long Head => History.Count - 1;

    public bool IsKnown(RdfTerm term) => IsInline(term) || _known.Contains(term);

    /// <summary>
    /// T1 for a data commit. Returns the outcome and, when committed, the
    /// terms allocated in the order the store must meet them.
    /// </summary>
    public Expected Commit(
        IReadOnlyList<(bool Assert, MQuad Quad)> operations,
        RdfTerm? agent,
        long? expected,
        Func<HashSet<MQuad>, HashSet<MQuad>, HashSet<MQuad>, (bool Accept, RdfTerm? Attachment)> validate,
        long clockTicks,
        out List<RdfTerm> freshBlanks)
    {
        freshBlanks = [];
        long head = Head;

        if (expected is long e && e != head)
        {
            return new Expected(CommitOutcome.Conflict, head);
        }

        Dictionary<MQuad, bool> pending = [];

        foreach ((bool assert, MQuad quad) in operations)
        {
            pending[quad] = assert;
        }

        HashSet<MQuad> asserted = [.. pending.Where(p => p.Value && !Graph.Contains(p.Key)).Select(p => p.Key)];
        HashSet<MQuad> retracted = [.. pending.Where(p => !p.Value && Graph.Contains(p.Key)).Select(p => p.Key)];

        if (asserted.Count == 0 && retracted.Count == 0)
        {
            return new Expected(CommitOutcome.NoChange, head);
        }

        HashSet<MQuad> after = [.. Graph];
        after.ExceptWith(retracted);
        after.UnionWith(asserted);

        (bool accept, RdfTerm? attachment) = validate(after, asserted, retracted);

        if (!accept)
        {
            return new Expected(CommitOutcome.Rejected, head);
        }

        // Terms allocated: every term the delta and the metadata mention that D
        // does not have, components of triple terms included. Retracted quads
        // are all in G, so all their terms are known already.
        HashSet<RdfTerm> fresh = [];
        List<RdfTerm> metadata = [.. new[] { agent, attachment }.OfType<RdfTerm>()];

        foreach (RdfTerm term in asserted.SelectMany(Terms).Concat(metadata))
        {
            Collect(term, fresh);
        }

        // The order the store allocates blank ids in: first mention in the
        // request, operations in order, subject to graph.
        foreach ((bool _, MQuad quad) in operations)
        {
            foreach (RdfTerm term in Terms(quad))
            {
                if (term.Kind == RdfTermKind.BlankNode && fresh.Contains(term) && !freshBlanks.Contains(term))
                {
                    freshBlanks.Add(term);
                }
            }
        }

        Commit(asserted, retracted, fresh, Settings[^1], clockTicks);
        Blanks.AddRange(freshBlanks);
        return new Expected(CommitOutcome.Committed, Head);
    }

    /// <summary>T5: a settings commit. Always a commit — I4 exempts it from non-emptiness.</summary>
    public Expected ChangeSettings(AccessScope scope, RdfTerm agent, RdfTerm cause, long? expected, long clockTicks)
    {
        if (expected is long e && e != Head)
        {
            return new Expected(CommitOutcome.Conflict, Head);
        }

        HashSet<RdfTerm> fresh = [];
        Collect(agent, fresh);
        Collect(cause, fresh);
        Commit([], [], fresh, scope, clockTicks);
        return new Expected(CommitOutcome.Committed, Head);
    }

    /// <summary>I5: the greatest position whose timestamp is at or before <paramref name="ticks"/>.</summary>
    public long PositionAt(long ticks)
    {
        long found = 0;

        for (int p = 1; p < Timestamps.Count; p++)
        {
            if (Timestamps[p] <= ticks)
            {
                found = p;
            }
        }

        return found;
    }

    private void Commit(HashSet<MQuad> asserted, HashSet<MQuad> retracted, HashSet<RdfTerm> fresh, AccessScope scope, long clockTicks)
    {
        Graph = [.. Graph];
        Graph.ExceptWith(retracted);
        Graph.UnionWith(asserted);
        History.Add(Graph);
        Settings.Add(scope);
        Allocated.Add(fresh);
        Deltas.Add((asserted, retracted));
        Timestamps.Add(Math.Max(clockTicks, Timestamps[^1]));
        _known.UnionWith(fresh);
    }

    private void Collect(RdfTerm term, HashSet<RdfTerm> fresh)
    {
        if (term.Kind == RdfTermKind.TripleTerm)
        {
            Collect(term.Subject!, fresh);
            Collect(term.Predicate!, fresh);
            Collect(term.Object!, fresh);
        }

        if (!IsKnown(term))
        {
            fresh.Add(term);
        }
    }

    public static IEnumerable<RdfTerm> Terms(MQuad quad)
    {
        yield return quad.S;
        yield return quad.P;
        yield return quad.O;

        if (quad.G is not null)
        {
            yield return quad.G;
        }
    }

    /// <summary>
    /// ADR 0012's amendment, restated: a literal has no dictionary entry when it
    /// is <c>xsd:boolean</c> true or false, or an <c>xsd:integer</c> whose
    /// lexical form is canonical and whose value fits the inline payload.
    /// </summary>
    public static bool IsInline(RdfTerm term)
    {
        if (term.Kind != RdfTermKind.Literal)
        {
            return false;
        }

        string datatype = Encoding.UTF8.GetString(term.DatatypeIri);
        string lexical = Encoding.UTF8.GetString(term.Lexical);

        if (datatype == "http://www.w3.org/2001/XMLSchema#boolean")
        {
            return lexical is "true" or "false";
        }

        if (datatype != "http://www.w3.org/2001/XMLSchema#integer")
        {
            return false;
        }

        bool canonical = lexical == "0" || System.Text.RegularExpressions.Regex.IsMatch(lexical, "^-?[1-9][0-9]*$");
        return canonical && long.TryParse(lexical, out long value) && value >= -(1L << 55) && value < (1L << 55);
    }
}

/// <summary>The terms the generators draw from.</summary>
internal static class Terms
{
    public const int LiteralCount = 10;

    private static readonly RdfTerm[] Literals =
    [
        T.Integer("1"),                     // inline
        T.Integer("01"),                    // not canonical: a canonical id, and a different term from "1"
        T.Integer("-5"),                    // inline
        T.Integer("99999999999999999999"),  // canonical, out of the inline range
        T.Boolean("true"),                  // inline
        T.Boolean("1"),                     // not canonical: a canonical id
        T.Literal("hello"),
        T.Lang("hello", "en"),
        T.Lang("hello", "EN"),              // the same term as @en: tags compare case-insensitively
        T.Literal("helloé"),
    ];

    public static RdfTerm Literal(int which) => Literals[which];
}
