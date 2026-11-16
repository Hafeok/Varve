// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Varve.Inference;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Turtle;
using InferenceProgram = Varve.Inference.Program;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--inference-closure &lt;S|M&gt; &lt;rdfs|owl2rl&gt; [held]</c> (ADRs 0195,
/// 0196): the closure of the REC portfolio under a materialising profile,
/// placed as the projection places it, in memory, over terms of its own.
/// </summary>
/// <remarks>
/// <para>
/// The TBox is REC 4.1.0 and Brick with its patches
/// (<c>testdata/rec/v4.1.0</c>), the data each site's graph at the head.
/// <strong>From scratch:</strong> the TBox closure and one closure per site
/// graph of its quads over it, the TBox closure shared beneath each, as a
/// cold start or a read below what is held computes them; its time, its facts, and the managed heap it
/// holds, measured after a full collection, per fact and per data quad.
/// </para>
/// <para>
/// <strong>Retraction</strong> (ADR 0196), with <c>held</c> above zero: the
/// closures built at the history less its last <c>held</c> commits, then
/// those commits applied one by one under each candidate, delete-and-rederive,
/// Backward/Forward and recursive counting, each in a closure of its own;
/// the time per commit (p50, p99, the worst and which commit) and the
/// closures' size after. The portfolio's renovations retract as it goes:
/// rooms split, merged and retyped, assets moved, points retired, tenants
/// out.
/// </para>
/// </remarks>
internal static class InferenceBench
{
    private const ulong Seed = 20261015;

    internal static void Run(string sizeName, string profileName, int held)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        InferenceProfile profile = profileName == "owl2rl" ? InferenceProfile.Owl2Rl : InferenceProfile.Rdfs;
        Console.WriteLine($"// REC {size} (generator version {RecPortfolio.Version}, seed {Seed}), profile {profile}, .NET {Environment.Version}, {Environment.ProcessorCount} cores");

        Terms terms = new();
        Fact[] tbox = TBox(terms);
        List<RecCommit> history = [.. RecPortfolio.Generate(size, Seed)];
        int cut = Math.Max(0, history.Count - held);
        Dictionary<string, HashSet<Fact>> graphs = State(terms, history.Take(cut));
        long quads = graphs.Values.Sum(g => (long)g.Count);
        Console.WriteLine($"// TBox {tbox.Length} triples; at commit {cut} of {history.Count}: {graphs.Count} graphs, {quads} quads");

        // From scratch: the TBox closure, and one per graph of its quads and the TBox's.
        long before = Heap();
        Stopwatch clock = Stopwatch.StartNew();
        Closure tboxClosure = new(InferenceProgram.Of(profile, terms), Retraction.DeleteAndRederive);
        tboxClosure.Apply(tbox, []);
        Console.WriteLine($"//   the TBox closure: {clock.Elapsed.TotalSeconds:F1} s, {tboxClosure.Count.Value} facts");
        Dictionary<string, Closure> closures = [];
        foreach ((string graph, HashSet<Fact> graphFacts) in graphs)
        {
            TimeSpan start = clock.Elapsed;
            Closure closure = new(InferenceProgram.Of(profile, terms), Retraction.DeleteAndRederive, tbox: tboxClosure);
            closure.Apply([.. graphFacts], []);
            closures[graph] = closure;
            Console.WriteLine($"//   {graph}: {graphFacts.Count} quads, {(clock.Elapsed - start).TotalSeconds:F1} s, {closure.Count.Value} facts");
        }

        TimeSpan built = clock.Elapsed;
        long after = Heap();
        long facts = tboxClosure.Count.Value + closures.Values.Sum(c => c.Count.Value);
        long rows = tboxClosure.Implicit.LongCount() + closures.Values.Sum(c => c.Own.LongCount(f => !c.IsExplicit(f)));
        Console.WriteLine($"from scratch: {built.TotalSeconds:F1} s; {facts} facts held ({tboxClosure.Count.Value} in the TBox closure); {rows} rows; heap {(after - before) / 1048576.0:F0} MiB, {(double)(after - before) / facts:F0} B a fact, {(double)(after - before) / Math.Max(1, quads):F0} B a data quad; working set {Environment.WorkingSet / 1048576.0:F0} MiB");
        GC.KeepAlive(closures);

        if (held == 0)
        {
            return;
        }

        closures.Clear();
        foreach (Retraction strategy in new[] { Retraction.RecursiveCounting, Retraction.DeleteAndRederive, Retraction.BackwardForward })
        {
            long start = Heap();
            Stopwatch building = Stopwatch.StartNew();
            Dictionary<string, Closure> mine = [];
            foreach ((string graph, HashSet<Fact> graphFacts) in graphs)
            {
                Closure closure = new(InferenceProgram.Of(profile, terms), strategy, tbox: tboxClosure);
                closure.Apply([.. graphFacts], []);
                mine[graph] = closure;
            }

            double build = building.Elapsed.TotalSeconds;
            long holding = Heap() - start;
            Console.WriteLine($"//   {strategy}: built in {build:F1} s, heap {holding / 1048576.0:F0} MiB, {(double)holding / mine.Values.Sum(c => c.Count.Value):F0} B a fact");
            TimeSpan budget = TimeSpan.FromMinutes(20);
            Stopwatch spent = Stopwatch.StartNew();

            List<double> times = [];
            (double Worst, int At, string Kind) worst = (0, 0, string.Empty);
            int number = cut;
            foreach (RecCommit commit in history.Skip(cut))
            {
                number++;
                Dictionary<string, (List<Fact> Asserted, List<Fact> Retracted)> deltas = [];
                foreach (RecQuad quad in commit.Asserted)
                {
                    Delta(deltas, quad.Graph).Asserted.Add(Fact(terms, quad));
                }

                foreach (RecQuad quad in commit.Retracted)
                {
                    Delta(deltas, quad.Graph).Retracted.Add(Fact(terms, quad));
                }

                Stopwatch one = Stopwatch.StartNew();
                foreach ((string graph, (List<Fact> asserted, List<Fact> retracted)) in deltas)
                {
                    if (!mine.TryGetValue(graph, out Closure? closure))
                    {
                        closure = new Closure(InferenceProgram.Of(profile, terms), strategy, tbox: tboxClosure);
                        mine[graph] = closure;
                    }

                    closure.Apply([.. asserted], [.. retracted]);
                }

                double ms = one.Elapsed.TotalMilliseconds;
                times.Add(ms);
                if (ms > worst.Worst)
                {
                    worst = (ms, number, commit.Kind);
                }

                if (spent.Elapsed > budget)
                {
                    Console.WriteLine($"//   {strategy}: stopped after {times.Count} commits, past its budget of {budget.TotalMinutes:F0} minutes");
                    break;
                }
            }

            times.Sort();
            Console.WriteLine($"{strategy}: {times.Count} commits, total {times.Sum() / 1000:F1} s; per commit p50 {Percentile(times, 0.5):F2} ms, p99 {Percentile(times, 0.99):F2} ms, worst {worst.Worst:F1} ms (commit {worst.At}, {worst.Kind}); facts after {mine.Values.Sum(c => c.Count.Value)}");
            mine.Clear();
        }
    }

    private static (List<Fact> Asserted, List<Fact> Retracted) Delta(Dictionary<string, (List<Fact>, List<Fact>)> deltas, string graph)
    {
        if (!deltas.TryGetValue(graph, out (List<Fact>, List<Fact>) lists))
        {
            deltas[graph] = lists = ([], []);
        }

        return lists;
    }

    /// <summary>
    /// Cross-graph growth (ADR 0190 point 6, ADR 0195 point 3): the closure
    /// of the first k data graphs' merge over the TBox closure, for k an
    /// eighth, a quarter, a half and all of them, and each fact's minimal
    /// premise sets over it; the distinct sets of two graphs or more, which
    /// are the reserved graphs a cross-graph binding places rows in.
    /// </summary>
    internal static void RunPremiseSets(string sizeName, string profileName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        InferenceProfile profile = profileName == "owl2rl" ? InferenceProfile.Owl2Rl : InferenceProfile.Rdfs;
        Console.WriteLine($"// REC {size} (generator version {RecPortfolio.Version}, seed {Seed}), profile {profile}, cross-graph, .NET {Environment.Version}, {Environment.ProcessorCount} cores");
        Terms terms = new();
        Fact[] tbox = TBox(terms);
        Dictionary<string, HashSet<Fact>> graphs = State(terms, RecPortfolio.Generate(size, Seed));
        string[] names = [.. graphs.Keys.Order(StringComparer.Ordinal)];
        Closure tboxClosure = new(InferenceProgram.Of(profile, terms), Retraction.DeleteAndRederive);
        tboxClosure.Apply(tbox, []);
        foreach (int k in new[] { names.Length / 8, names.Length / 4, names.Length / 2, names.Length }.Where(k => k > 0).Distinct())
        {
            long before = Heap();
            Stopwatch clock = Stopwatch.StartNew();
            Closure merge = new(InferenceProgram.Of(profile, terms), Retraction.DeleteAndRederive, tbox: tboxClosure);
            merge.Apply([.. names.Take(k).SelectMany(n => graphs[n]).Distinct()], []);
            TimeSpan closed = clock.Elapsed;
            List<(Fact, DataGraphIndex)> held = [];
            for (int g = 0; g < k; g++)
            {
                held.AddRange(graphs[names[g]].Select(f => (f, new DataGraphIndex(g))));
            }

            PremiseSets sets = new(merge, held);
            TimeSpan labelled = clock.Elapsed - closed;
            long after = Heap();
            long crossFacts = 0, onlyCross = 0, several = 0;
            foreach (Fact fact in merge.Own)
            {
                IReadOnlyList<PremiseSet> of = sets.Of(fact);
                several += of.Count > 1 ? 1 : 0;
                int cross = of.Count(s => s.IsCrossGraph);
                crossFacts += cross > 0 ? 1 : 0;
                onlyCross += cross > 0 && cross == of.Count ? 1 : 0;
            }

            IReadOnlyCollection<PremiseSet> distinct = sets.CrossGraph();
            int widest = distinct.Count == 0 ? 0 : distinct.Max(s => s.Graphs.Count);
            Console.WriteLine($"{k} graphs: closed in {closed.TotalSeconds:F1} s, {merge.Count.Value} facts; premise sets in {labelled.TotalSeconds:F1} s; {distinct.Count} distinct cross-graph sets (widest {widest} graphs); {crossFacts} facts with one, {onlyCross} with no other, {several} with more than one set; heap {(after - before) / 1048576.0:F0} MiB");
            GC.KeepAlive(merge);
            GC.KeepAlive(sets);
        }
    }

    /// <summary>
    /// A cross-graph binding's commits (ADR 0195 point 3): the merge of every
    /// data graph and its premise sets built at the history less its last
    /// <paramref name="held"/> commits, then those commits applied one by one,
    /// the merge by recursive counting and the sets from the delta; the time
    /// per commit and the facts whose sets changed.
    /// </summary>
    internal static void RunPremiseSetCommits(string sizeName, string profileName, int held)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        InferenceProfile profile = profileName == "owl2rl" ? InferenceProfile.Owl2Rl : InferenceProfile.Rdfs;
        Console.WriteLine($"// REC {size} (generator version {RecPortfolio.Version}, seed {Seed}), profile {profile}, cross-graph commits, .NET {Environment.Version}, {Environment.ProcessorCount} cores");
        Terms terms = new();
        Fact[] tbox = TBox(terms);
        List<RecCommit> history = [.. RecPortfolio.Generate(size, Seed)];
        int cut = Math.Max(0, history.Count - held);
        Dictionary<string, HashSet<Fact>> graphs = State(terms, history.Take(cut));
        List<RecCommit> tail = [.. history.Skip(cut)];
        history = [];

        // How many graphs hold each fact: the merge's delta, without a copy of the merge.
        Dictionary<Fact, int> holders = [];
        foreach (HashSet<Fact> facts in graphs.Values)
        {
            foreach (Fact fact in facts)
            {
                holders[fact] = holders.GetValueOrDefault(fact) + 1;
            }
        }

        Dictionary<string, int> indices = new(StringComparer.Ordinal);
        foreach (string name in graphs.Keys.Order(StringComparer.Ordinal))
        {
            indices[name] = indices.Count;
        }

        Closure tboxClosure = new(InferenceProgram.Of(profile, terms), Retraction.DeleteAndRederive);
        tboxClosure.Apply(tbox, []);
        Stopwatch clock = Stopwatch.StartNew();
        Closure merge = new(InferenceProgram.Of(profile, terms), Retraction.RecursiveCounting, tbox: tboxClosure);
        merge.Apply([.. holders.Keys], []);
        PremiseSets sets = new(merge, graphs.SelectMany(g => g.Value.Select(f => (f, new DataGraphIndex(indices[g.Key])))));
        Console.WriteLine($"//   built in {clock.Elapsed.TotalSeconds:F1} s: {merge.Count.Value} facts, {sets.CrossGraph().Count} cross-graph sets");

        List<double> times = [];
        double reachMs = 0, closeMs = 0, updateMs = 0;
        long touched = 0;
        (double Worst, int At, string Kind) worst = (0, 0, string.Empty);
        int number = cut;
        foreach (RecCommit commit in tail)
        {
            number++;
            Stopwatch one = Stopwatch.StartNew();
            List<Fact> gone = [], come = [];
            List<(Fact, DataGraphIndex)> unpaired = [];
            List<(Fact, DataGraphIndex)> paired = [];
            foreach (RecQuad quad in commit.Retracted)
            {
                if (graphs.TryGetValue(quad.Graph, out HashSet<Fact>? facts) && facts.Remove(Fact(terms, quad)))
                {
                    Fact fact = Fact(terms, quad);
                    unpaired.Add((fact, new DataGraphIndex(indices[quad.Graph])));
                    if ((holders[fact] -= 1) == 0)
                    {
                        holders.Remove(fact);
                        gone.Add(fact);
                    }
                }
            }

            foreach (RecQuad quad in commit.Asserted)
            {
                if (!graphs.TryGetValue(quad.Graph, out HashSet<Fact>? facts))
                {
                    graphs[quad.Graph] = facts = [];
                    indices.TryAdd(quad.Graph, indices.Count);
                }

                if (facts.Add(Fact(terms, quad)))
                {
                    Fact fact = Fact(terms, quad);
                    paired.Add((fact, new DataGraphIndex(indices[quad.Graph])));
                    int now = holders.GetValueOrDefault(fact) + 1;
                    holders[fact] = now;
                    if (now == 1)
                    {
                        come.Add(fact);
                    }
                }
            }

            string[] names = [.. indices.OrderBy(i => i.Value).Select(i => i.Key)];
            Stopwatch step = Stopwatch.StartNew();
            IReadOnlySet<Fact> affected = sets.Reach(unpaired, (fact, graph) => graphs.TryGetValue(names[graph.Value], out HashSet<Fact>? held) && held.Contains(fact));
            reachMs += step.Elapsed.TotalMilliseconds;
            step.Restart();
            // A fact gone and back within the commit is no change to the merge.
            HashSet<Fact> back = [.. gone.Intersect(come)];
            merge.Apply([.. come.Where(f => !back.Contains(f))], [.. gone.Where(f => !back.Contains(f))]);
            closeMs += step.Elapsed.TotalMilliseconds;
            step.Restart();
            foreach ((string graph, HashSet<Fact> facts) in graphs)
            {
                foreach (Fact fact in affected)
                {
                    if (facts.Contains(fact))
                    {
                        paired.Add((fact, new DataGraphIndex(indices[graph])));
                    }
                }
            }

            touched += sets.Update(affected, paired).Count;
            updateMs += step.Elapsed.TotalMilliseconds;
            double ms = one.Elapsed.TotalMilliseconds;
            times.Add(ms);
            if (ms > worst.Worst)
            {
                worst = (ms, number, commit.Kind);
            }
        }

        times.Sort();
        Console.WriteLine($"cross-graph: {times.Count} commits; per commit p50 {Percentile(times, 0.5):F2} ms, p99 {Percentile(times, 0.99):F2} ms, worst {worst.Worst:F1} ms (commit {worst.At}, {worst.Kind}); {touched} facts' sets changed in all; {sets.CrossGraph().Count} cross-graph sets after; in all, reach {reachMs:F0} ms, the merge's closure {closeMs:F0} ms, the sets {updateMs:F0} ms, the rest the harness's");
        GC.KeepAlive(merge);
    }

    private static double Percentile(List<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

    private static long Heap()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    /// <summary>Each graph's quads after <paramref name="commits"/>.</summary>
    private static Dictionary<string, HashSet<Fact>> State(Terms terms, IEnumerable<RecCommit> commits)
    {
        Dictionary<string, HashSet<Fact>> graphs = new(StringComparer.Ordinal);
        foreach (RecCommit commit in commits)
        {
            foreach (RecQuad quad in commit.Retracted)
            {
                if (graphs.TryGetValue(quad.Graph, out HashSet<Fact>? facts))
                {
                    facts.Remove(Fact(terms, quad));
                }
            }

            foreach (RecQuad quad in commit.Asserted)
            {
                if (!graphs.TryGetValue(quad.Graph, out HashSet<Fact>? facts))
                {
                    graphs[quad.Graph] = facts = [];
                }

                facts.Add(Fact(terms, quad));
            }
        }

        return graphs;
    }

    private static Fact Fact(Terms terms, RecQuad quad) => new(terms.Handle(Term(quad.Subject)), terms.Handle(Term(quad.Predicate)), terms.Handle(Term(quad.Object)));

    /// <summary>A term of the portfolio's N-Triples: an IRI in angle brackets, or a plain literal in quotes.</summary>
    private static RdfTerm Term(string text) => text[0] == '<'
        ? RdfTerm.Iri(Encoding.UTF8.GetBytes(text[1..^1]))
        : RdfTerm.Literal(Encoding.UTF8.GetBytes(text[1..^1]));

    /// <summary>REC 4.1.0 and Brick with its patches, as one TBox.</summary>
    private static Fact[] TBox(Terms terms)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "testdata", "rec", "v4.1.0");
        List<Fact> facts = [];
        foreach (string file in new[] { "rec.ttl", "Brick+patches.ttl" })
        {
            ParseResult result = TurtleParser.Parse(
                File.ReadAllBytes(Path.Combine(root, file)),
                (in QuadView quad) => facts.Add(new Fact(terms.Handle(quad.Subject.Materialise()), terms.Handle(quad.Predicate.Materialise()), terms.Handle(quad.Object.Materialise()))),
                new TurtleOptions { Syntax = RdfSyntax.Turtle });
            if (!result.Succeeded)
            {
                throw new InvalidDataException(file + ": " + result.FirstError.ToString());
            }
        }

        return [.. facts.Distinct()];
    }

    /// <summary>Handles over terms, as the projection's over a dataset's.</summary>
    private sealed class Terms : IInferenceTerms
    {
        private const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);
        private readonly List<RdfTerm> _terms = [];

        public TermHandle Handle(RdfTerm term)
        {
            if (!_handles.TryGetValue(term, out TermHandle handle))
            {
                _terms.Add(term);
                handle = new TermHandle((ulong)_terms.Count);
                _handles[term] = handle;
            }

            return handle;
        }

        public bool IsLiteral(TermHandle handle) => Term(handle).Kind == RdfTermKind.Literal;

        public bool TryDatatype(TermHandle literal, out TermHandle datatype)
        {
            RdfTerm term = Term(literal);
            datatype = term.Kind == RdfTermKind.Literal ? Handle(RdfTerm.Iri(term.DatatypeIri)) : TermHandle.None;
            return term.Kind == RdfTermKind.Literal;
        }

        public bool IsContainerMembership(TermHandle handle)
        {
            RdfTerm term = Term(handle);
            string iri = Encoding.UTF8.GetString(term.Lexical);
            return term.Kind == RdfTermKind.Iri && iri.StartsWith(RdfNs + "_", StringComparison.Ordinal) && iri.Length > RdfNs.Length + 1
                && iri[RdfNs.Length + 1] != '0' && iri.AsSpan(RdfNs.Length + 1).IndexOfAnyExceptInRange('0', '9') < 0;
        }

        private RdfTerm Term(TermHandle handle) => _terms[(int)handle.Value - 1];
    }
}
