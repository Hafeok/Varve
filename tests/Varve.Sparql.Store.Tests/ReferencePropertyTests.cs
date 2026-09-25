// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Conformance.Tests;
using Varve.Rdf;
using Varve.Store;
using Xunit;

namespace Varve.Sparql.Store.Tests;

/// <summary>
/// The reference property (<c>sparql-update-store.md</c> §7): for generated
/// sequences of requests, applying them through <see cref="SparqlUpdate"/>
/// and applying through the store's commit API the deltas a term-level model
/// computes by hand give the same as-of state at every position and the same
/// number of commits.
/// </summary>
/// <remarks>
/// The model is naive on purpose: a set of quads of strings, each operation
/// applied as SPARQL 1.1 Update §3 describes it, sharing no code with the
/// package. Its deltas reach the second store as ordinary commits, blank nodes
/// the model created earlier addressed by the handles the store gave them.
/// </remarks>
public class ReferencePropertyTests
{
    /// <summary>The iterations the property runs; the report states it.</summary>
    internal const int Iterations = 2_000;

    private const string Ex = "http://example.org/";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- The generator.

    private static readonly Gen<string> Subject = Gen.OneOfConst("s0", "s1", "s2");
    private static readonly Gen<string> Predicate = Gen.OneOfConst("p0", "p1");
    private static readonly Gen<string> Object = Gen.OneOfConst("s0", "s1", "o0", "o1", "\"1\"");
    private static readonly Gen<string> GraphName = Gen.OneOfConst("", "", "g0", "g1");

    private static readonly Gen<Operation> Insert =
        Gen.Select(Gen.OneOf(Subject, Gen.Const("_:b0"), Gen.Const("_:b1")), Predicate, Gen.OneOf(Object, Gen.Const("_:b0")), GraphName, (s, p, o, g) => (s, p, o, g))
            .List[1, 3].Select(quads => (Operation)new InsertData(quads));

    private static readonly Gen<Operation> Delete =
        Gen.Select(Subject, Predicate, Object, GraphName, (s, p, o, g) => (s, p, o, g))
            .List[1, 3].Select(quads => (Operation)new DeleteData(quads));

    private static readonly Gen<Operation> Where = Gen.Select(Predicate, GraphName, (p, g) => (Operation)new DeleteWhere(p, g));

    private static readonly Gen<Operation> Modify = Gen.Select(
        GraphName, Predicate, Predicate, Gen.Int[0, 2],
        (with, from, to, shape) => (Operation)new ModifyOperation(with, from, shape != 1, shape != 0 ? to : null));

    private static readonly Gen<Operation> Clear =
        Gen.Select(Gen.OneOfConst("CLEAR", "DROP"), Gen.OneOfConst("DEFAULT", "NAMED", "ALL", "GRAPH :g0", "GRAPH :g1"), (verb, target) => (Operation)new ClearOperation(verb, target));

    private static readonly Gen<Operation> Create = GraphName.Where(g => g.Length > 0).Select(g => (Operation)new CreateSilent(g));

    private static readonly Gen<Operation> Transfer =
        Gen.Select(Gen.OneOfConst("ADD", "COPY", "MOVE"), GraphName, GraphName, (verb, from, to) => (Operation)new TransferOperation(verb, from, to));

    private static readonly Gen<Operation> AnyOperation = Gen.OneOf(Insert, Insert, Insert, Delete, Where, Modify, Modify, Clear, Create, Transfer);

    private static readonly Gen<Operation[][]> Requests = AnyOperation.Array[1, 4].Array[1, 6];

    // ---- The property.

    [Fact]
    public async Task Update_requests_and_the_models_deltas_give_the_same_states_and_commits()
    {
        Dictionary<string, int> kinds = [];

        await Requests.SampleAsync(
            async requests =>
            {
                foreach (Operation[] request in requests)
                {
                    foreach (Operation operation in request)
                    {
                        lock (kinds)
                        {
                            kinds[operation.GetType().Name] = kinds.GetValueOrDefault(operation.GetType().Name) + 1;
                        }
                    }
                }

                await Check(requests);
            },
            iter: Iterations);

        // Every kind of operation occurred (ADR 0043's rule for generators).
        Assert.Equal(7, kinds.Count);
    }

    private static async Task Check(Operation[][] requests)
    {
        await using Dataset viaUpdate = await Open();
        await using Dataset viaModel = await Open();
        Model model = new();

        for (int r = 0; r < requests.Length; r++)
        {
            string text = "PREFIX : <" + Ex + ">\n" + string.Join(" ;\n", requests[r].Select((op, i) => op.ToSparql(i)));
            CommitResult result = await SparqlUpdate.ExecuteAsync(viaUpdate, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new UpdateOptions(), Ct);

            HashSet<MQuad> before = [.. model.State];

            for (int i = 0; i < requests[r].Length; i++)
            {
                requests[r][i].Apply(model, i);
            }

            await model.CommitAsync(viaModel, before);

            Assert.True(
                viaUpdate.Head == viaModel.Head,
                "After request " + (r + 1) + " the update store is at " + viaUpdate.Head + " and the model's at " + viaModel.Head + ":\n" + text);
            Assert.Equal(model.State.SetEquals(before) ? CommitOutcome.NoChange : CommitOutcome.Committed, result.Outcome);
        }

        for (long position = 1; position <= viaUpdate.Head; position++)
        {
            using DatasetView a = await viaUpdate.AsOfAsync(position, Ct);
            using DatasetView b = await viaModel.AsOfAsync(position, Ct);
            IsomorphismResult verdict = Isomorphism.Compare(Quads(a), Quads(b));
            Assert.True(
                verdict.IsSame,
                "At position " + position + ": " + verdict.Reason + "\nrequests:\n" + string.Join("\n--\n", requests.Select(q => string.Join(" ;\n", q.Select((op, i) => op.ToSparql(i))))));
        }
    }

    private static ValueTask<Dataset> Open() =>
        Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = new Clock() }, Ct);

    private static List<ParsedQuad> Quads(DatasetView view)
    {
        List<ParsedQuad> quads = [];
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad q = cursor.Current;
            quads.Add(new ParsedQuad(Text(view, q.Subject), Text(view, q.Predicate), Text(view, q.Object), q.Graph.IsNone ? null : Text(view, q.Graph)));
        }

        return quads;
    }

    private static string Text(DatasetView view, TermHandle handle)
    {
        Assert.True(view.TryExternalise(handle, out RdfTerm? term));
        string lexical = Encoding.UTF8.GetString(term.Lexical);
        return term.Kind switch
        {
            RdfTermKind.Iri => "<" + lexical + ">",
            RdfTermKind.BlankNode => "_:" + lexical,
            _ => "\"" + lexical + "\"",
        };
    }

    // ---- The model: quads of strings. An IRI is its local name, a literal
    // is quoted, a blank node is "_:m<n>", the default graph is "".

    private readonly record struct MQuad(string S, string P, string O, string G);

    private sealed class Model
    {
        private readonly Dictionary<string, TermHandle> _handles = new(StringComparer.Ordinal);
        private int _blanks;

        internal HashSet<MQuad> State { get; } = [];

        internal string NewBlank() => "_:m" + (++_blanks).ToString(CultureInfo.InvariantCulture);

        /// <summary>The request's net delta as one commit, as a caller of the store would make it.</summary>
        internal async Task CommitAsync(Dataset dataset, HashSet<MQuad> before)
        {
            List<MQuad> retract = [.. before.Where(q => !State.Contains(q)).OrderBy(q => q.ToString(), StringComparer.Ordinal)];
            List<MQuad> assert = [.. State.Where(q => !before.Contains(q)).OrderBy(q => q.ToString(), StringComparer.Ordinal)];

            if (retract.Count == 0 && assert.Count == 0)
            {
                return;
            }

            CommitRequest request = new();
            List<string> fresh = [];

            foreach (MQuad quad in retract)
            {
                request.Retract(Term(quad.S, fresh), Term(quad.P, fresh), Term(quad.O, fresh), quad.G.Length == 0 ? RequestTerm.None : Term(quad.G, fresh));
            }

            foreach (MQuad quad in assert)
            {
                request.Assert(Term(quad.S, fresh), Term(quad.P, fresh), Term(quad.O, fresh), quad.G.Length == 0 ? RequestTerm.None : Term(quad.G, fresh));
            }

            long head = dataset.Head;
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, Ct)).Outcome);

            // The new blank nodes, in the order the request first named them,
            // are the commit's blank allocations in id order.
            await foreach (Varve.Store.Commit commit in dataset.Subscribe(head, SubscriptionFilter.All, Ct))
            {
                TermHandle[] blanks = [.. commit.Allocations.ToArray().Where(a => a.Term.Kind == RdfTermKind.BlankNode).Select(a => a.Handle)];
                Assert.Equal(fresh.Count, blanks.Length);

                for (int i = 0; i < fresh.Count; i++)
                {
                    _handles[fresh[i]] = blanks[i];
                }

                break;
            }
        }

        private RequestTerm Term(string term, List<string> fresh)
        {
            if (term.StartsWith("_:", StringComparison.Ordinal))
            {
                if (_handles.TryGetValue(term, out TermHandle handle))
                {
                    return RequestTerm.Existing(handle);
                }

                if (!fresh.Contains(term))
                {
                    fresh.Add(term);
                }

                return RdfTerm.BlankNode(Encoding.UTF8.GetBytes(term[2..]));
            }

            return term.StartsWith('"')
                ? RdfTerm.Literal(Encoding.UTF8.GetBytes(term.Trim('"')))
                : RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + term));
        }
    }

    // ---- The operations: SPARQL text, and the model's reading of it.

    private abstract record Operation
    {
        internal abstract string ToSparql(int index);

        internal abstract void Apply(Model model, int index);

        protected static string Term(string term, int index) =>
            term.StartsWith("_:", StringComparison.Ordinal) ? term + "_" + index.ToString(CultureInfo.InvariantCulture)
            : term.StartsWith('"') ? term
            : ":" + term;

        protected static string Quad(string s, string p, string o, string g, int index)
        {
            string triple = Term(s, index) + " " + Term(p, index) + " " + Term(o, index) + " .";
            return g.Length == 0 ? triple : "GRAPH :" + g + " { " + triple + " }";
        }
    }

    private sealed record InsertData(List<(string S, string P, string O, string G)> Quads) : Operation
    {
        internal override string ToSparql(int index) =>
            "INSERT DATA { " + string.Join(" ", Quads.Select(q => Quad(q.S, q.P, q.O, q.G, index))) + " }";

        // §3.1.1: blank nodes are fresh, one per label (labels are scoped to the operation here).
        internal override void Apply(Model model, int index)
        {
            Dictionary<string, string> labels = [];
            string Node(string term) => term.StartsWith("_:", StringComparison.Ordinal)
                ? labels.TryGetValue(term, out string? node) ? node : labels[term] = model.NewBlank()
                : term;

            foreach ((string s, string p, string o, string g) in Quads)
            {
                model.State.Add(new MQuad(Node(s), p, Node(o), g));
            }
        }
    }

    private sealed record DeleteData(List<(string S, string P, string O, string G)> Quads) : Operation
    {
        internal override string ToSparql(int index) =>
            "DELETE DATA { " + string.Join(" ", Quads.Select(q => Quad(q.S, q.P, q.O, q.G, index))) + " }";

        internal override void Apply(Model model, int index)
        {
            foreach ((string s, string p, string o, string g) in Quads)
            {
                model.State.Remove(new MQuad(s, p, o, g));
            }
        }
    }

    private sealed record DeleteWhere(string P, string G) : Operation
    {
        internal override string ToSparql(int index) =>
            "DELETE WHERE { " + (G.Length == 0 ? "?s :" + P + " ?o" : "GRAPH :" + G + " { ?s :" + P + " ?o }") + " }";

        internal override void Apply(Model model, int index) => model.State.RemoveWhere(q => q.P == P && q.G == G);
    }

    /// <summary><c>[WITH :g] DELETE { ?s :p ?o } INSERT { ?o :q ?s } WHERE { ?s :p ?o }</c>.</summary>
    private sealed record ModifyOperation(string With, string From, bool Delete, string? To) : Operation
    {
        internal override string ToSparql(int index) =>
            (With.Length == 0 ? "" : "WITH :" + With + " ")
            + (Delete ? "DELETE { ?s :" + From + " ?o } " : "")
            + (To is null ? "" : "INSERT { ?o :" + To + " ?s } ")
            + "WHERE { ?s :" + From + " ?o }";

        // One evaluation of the WHERE, deletions before insertions (§3.1.3);
        // a literal where a subject goes is skipped.
        internal override void Apply(Model model, int index)
        {
            List<MQuad> solutions = [.. model.State.Where(q => q.P == From && q.G == With)];

            if (Delete)
            {
                foreach (MQuad quad in solutions)
                {
                    model.State.Remove(quad);
                }
            }

            if (To is not null)
            {
                foreach (MQuad quad in solutions.Where(q => !q.O.StartsWith('"')))
                {
                    model.State.Add(new MQuad(quad.O, To, quad.S, With));
                }
            }
        }
    }

    private sealed record ClearOperation(string Verb, string Target) : Operation
    {
        internal override string ToSparql(int index) => Verb + " " + Target;

        internal override void Apply(Model model, int index) =>
            model.State.RemoveWhere(q => Target switch
            {
                "DEFAULT" => q.G.Length == 0,
                "NAMED" => q.G.Length > 0,
                "ALL" => true,
                _ => q.G == Target["GRAPH :".Length..],
            });
    }

    private sealed record CreateSilent(string G) : Operation
    {
        internal override string ToSparql(int index) => "CREATE SILENT GRAPH :" + G;

        internal override void Apply(Model model, int index)
        {
        }
    }

    private sealed record TransferOperation(string Verb, string From, string To) : Operation
    {
        internal override string ToSparql(int index) => Verb + " " + Name(From) + " TO " + Name(To);

        private static string Name(string graph) => graph.Length == 0 ? "DEFAULT" : ":" + graph;

        // §3.2.3–§3.2.5; the same graph both ways does nothing.
        internal override void Apply(Model model, int index)
        {
            if (From == To)
            {
                return;
            }

            List<MQuad> source = [.. model.State.Where(q => q.G == From)];

            if (Verb != "ADD")
            {
                model.State.RemoveWhere(q => q.G == To);
            }

            if (Verb == "MOVE")
            {
                model.State.RemoveWhere(q => q.G == From);
            }

            foreach (MQuad quad in source)
            {
                model.State.Add(quad with { G = To });
            }
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    }
}
