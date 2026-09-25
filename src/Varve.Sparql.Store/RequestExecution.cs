// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Turtle;

namespace Varve.Sparql.Store;

/// <summary>
/// One attempt at one request: the operations in order, each over the overlay
/// of the deltas before it, composed into one delta against the pinned
/// position (<c>sparql-update-store.md</c> §3, §5).
/// </summary>
/// <remarks>
/// The composed delta is kept as two sets of quads over the staging view's
/// handles. The staging view is the store's, whose equality is by handle
/// (ADR 0022's comparer, and no private terms before erasure mode), so the
/// sets compare quads by their bits.
/// </remarks>
internal sealed class RequestExecution
{
    private readonly StagingView _staging;
    private readonly Update _update;
    private readonly UpdateOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly SparqlEvaluator _evaluator;
    private readonly HashSet<Quad> _asserted = [];
    private readonly HashSet<Quad> _retracted = [];

    // INSERT DATA's blank nodes: one per label per request (§3.1.1).
    private readonly Dictionary<string, TermHandle> _dataBlanks = new(StringComparer.Ordinal);

    internal RequestExecution(StagingView staging, Update update, UpdateOptions options, CancellationToken cancellationToken)
    {
        _staging = staging;
        _update = update;
        _options = options;
        _cancellationToken = cancellationToken;
        _evaluator = new SparqlEvaluator(options.Evaluation);
    }

    /// <summary>Executes every operation; the first failure fails the request (§2.2).</summary>
    internal async Task RunAsync()
    {
        for (int index = 0; index < _update.Operations.Count; index++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            UpdateOperation operation = _update.Operations[index];
            List<Quad> delete = [];
            List<Quad> insert = [];

            try
            {
                switch (operation)
                {
                    case InsertData data:
                        foreach (QuadPattern quad in data.Quads)
                        {
                            AddData(quad, insert);
                        }

                        break;

                    case DeleteData data:
                        foreach (QuadPattern quad in data.Quads)
                        {
                            if (TryResolveGround(quad, out Quad resolved))
                            {
                                delete.Add(resolved);
                            }
                        }

                        break;

                    case DeleteWhere where:
                        Modify(null, where.Quads, AlgebraList.Empty<QuadPattern>(), null, PatternOf(where.Quads), delete, insert);
                        break;

                    case Modify modify:
                        Modify(modify.With, modify.Delete, modify.Insert, modify.Using, modify.Where, delete, insert);
                        break;

                    case Load load:
                        await LoadAsync(load, index, insert).ConfigureAwait(false);
                        break;

                    case Clear clear:
                        AddTarget(clear.Target, delete);
                        break;

                    case Drop drop:
                        AddTarget(drop.Target, delete);
                        break;

                    case Create create:
                        Create(create, index);
                        break;

                    case Add add:
                        Transfer(add.From, add.To, clearTarget: false, clearSource: false, delete, insert);
                        break;

                    case Copy copy:
                        Transfer(copy.From, copy.To, clearTarget: true, clearSource: false, delete, insert);
                        break;

                    case Move move:
                        Transfer(move.From, move.To, clearTarget: true, clearSource: true, delete, insert);
                        break;

                    default:
                        throw new NotSupportedException("Not an update operation this executor knows.");
                }
            }
            catch (Exception error) when (error is not SparqlUpdateException and not OperationCanceledException)
            {
                throw new SparqlUpdateException(index, KindOf(operation), error.Message, error);
            }

            Apply(delete, insert);
        }
    }

    /// <summary>
    /// The composed delta as one request: retractions and assertions in a
    /// fixed order, the pinned position expected (§3 step 3).
    /// </summary>
    internal CommitRequest ToCommitRequest()
    {
        CommitRequest request = new() { ExpectedPosition = _staging.Position, Metadata = _options.Metadata };

        try
        {
            foreach (Quad quad in Sorted(_retracted))
            {
                Add(request, quad, assert: false);
            }

            foreach (Quad quad in Sorted(_asserted))
            {
                Add(request, quad, assert: true);
            }
        }
        catch (NotSupportedException error)
        {
            throw new SparqlUpdateException(error.Message, error);
        }

        return request;
    }

    private void Add(CommitRequest request, Quad quad, bool assert)
    {
        RequestTerm s = _staging.ToRequestTerm(quad.Subject);
        RequestTerm p = _staging.ToRequestTerm(quad.Predicate);
        RequestTerm o = _staging.ToRequestTerm(quad.Object);

        if (quad.Graph.IsNone)
        {
            _ = assert ? request.Assert(s, p, o) : request.Retract(s, p, o);
        }
        else
        {
            RequestTerm g = _staging.ToRequestTerm(quad.Graph);
            _ = assert ? request.Assert(s, p, o, g) : request.Retract(s, p, o, g);
        }
    }

    // A fixed order, so that the same request allocates the same ids in the
    // same order on every machine (the specification's determinism property).
    private static List<Quad> Sorted(HashSet<Quad> quads)
    {
        List<Quad> list = [.. quads];
        list.Sort(static (a, b) =>
        {
            int c = a.Subject.Value.CompareTo(b.Subject.Value);
            c = c != 0 ? c : a.Predicate.Value.CompareTo(b.Predicate.Value);
            c = c != 0 ? c : a.Object.Value.CompareTo(b.Object.Value);
            return c != 0 ? c : a.Graph.Value.CompareTo(b.Graph.Value);
        });
        return list;
    }

    // ---- The state and the composition.

    /// <summary>The state operation <c>k</c> reads: the staging view with the deltas before it.</summary>
    private IQuadSource Current() =>
        _asserted.Count == 0 && _retracted.Count == 0
            ? _staging
            : new QuadOverlay(_staging, QuadDelta.Create([.. _asserted], [.. _retracted]));

    private bool InState(in Quad quad) =>
        _asserted.Contains(quad) || (!_retracted.Contains(quad) && _staging.Contains(in quad));

    /// <summary>
    /// Composes this operation's change onto the request's: its deletions
    /// before its insertions (§3.1.3), each only where it changes something,
    /// so that every step is exact and their composition a chain (ADR 0047).
    /// </summary>
    private void Apply(List<Quad> delete, List<Quad> insert)
    {
        foreach (Quad quad in delete)
        {
            if (InState(in quad) && !_asserted.Remove(quad))
            {
                _retracted.Add(quad);
            }
        }

        foreach (Quad quad in insert)
        {
            if (!InState(in quad) && !_retracted.Remove(quad))
            {
                _asserted.Add(quad);
            }
        }
    }

    // ---- INSERT DATA and DELETE DATA.

    private void AddData(QuadPattern pattern, List<Quad> insert)
    {
        TermHandle s = Data(pattern.Subject);
        TermHandle p = Data(pattern.Predicate);
        TermHandle o = Data(pattern.Object);
        TermHandle g = pattern.Graph is null ? TermHandle.None : Data(pattern.Graph);
        insert.Add(new Quad(s, p, o, g));
    }

    private TermHandle Data(PatternTerm term) => term switch
    {
        TermPattern constant => Stage(constant.Term, DataBlank),
        BlankNodePattern blank => DataBlank(blank.Label),
        TripleTermPattern triple => _staging.StageTriple(Data(triple.Subject), Data(triple.Predicate), Data(triple.Object)),
        _ => throw new InvalidOperationException("INSERT DATA holds no variables."),
    };

    private TermHandle DataBlank(string label)
    {
        if (!_dataBlanks.TryGetValue(label, out TermHandle handle))
        {
            handle = _staging.StageBlank();
            _dataBlanks[label] = handle;
        }

        return handle;
    }

    private bool TryResolveGround(QuadPattern pattern, out Quad quad)
    {
        quad = default;

        if (!TryResolve(pattern.Subject, out TermHandle s)
            || !TryResolve(pattern.Predicate, out TermHandle p)
            || !TryResolve(pattern.Object, out TermHandle o))
        {
            return false;
        }

        TermHandle g = TermHandle.None;

        if (pattern.Graph is { } graph && !TryResolve(graph, out g))
        {
            return false;
        }

        quad = new Quad(s, p, o, g);
        return true;
    }

    // A ground term the state could hold; one it has no handle for is in no quad.
    private bool TryResolve(PatternTerm term, out TermHandle handle)
    {
        handle = TermHandle.None;
        return term is TermPattern constant && _staging.TryInternalise(constant.Term, out handle);
    }

    /// <summary>
    /// A term's handle for assertion: the state's own when it has one, staged
    /// otherwise; its blank nodes, and those inside a triple term, from
    /// <paramref name="blank"/>.
    /// </summary>
    private TermHandle Stage(RdfTerm term, Func<string, TermHandle> blank) => term.Kind switch
    {
        RdfTermKind.BlankNode => blank(System.Text.Encoding.UTF8.GetString(term.Lexical)),
        RdfTermKind.TripleTerm when _staging.TryInternalise(term, out TermHandle known) => known,
        RdfTermKind.TripleTerm => _staging.StageTriple(Stage(term.Subject!, blank), Stage(term.Predicate!, blank), Stage(term.Object!, blank)),
        _ => _staging.Stage(term),
    };

    // ---- DELETE/INSERT … WHERE — §3.1.3.

    private void Modify(
        RdfTerm? with,
        AlgebraList<QuadPattern> deleteTemplate,
        AlgebraList<QuadPattern> insertTemplate,
        DatasetSpec? usingDataset,
        QueryPattern where,
        List<Quad> delete,
        List<Quad> insert)
    {
        IQuadSource state = Current();
        IQuadSource source = state;

        // USING wins over WITH for the WHERE (§3.1.3); WITH still names the
        // templates' graph.
        if (usingDataset is null && with is not null)
        {
            source = new DefaultGraphView(state, state.TryInternalise(with, out TermHandle graph) ? graph : TermHandle.None);
        }

        List<Variable> variables = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        Collect(deleteTemplate, variables, seen);
        Collect(insertTemplate, variables, seen);

        SelectQuery query = new(_update.Prologue, usingDataset, new Project(where, AlgebraList.From(variables)));
        using QueryResults results = _evaluator.Evaluate(query, source, _cancellationToken);
        SolutionResults solutions = (SolutionResults)results;

        Dictionary<string, int> columns = new(StringComparer.Ordinal);
        for (int i = 0; i < solutions.Variables.Count; i++)
        {
            columns[solutions.Variables[i].Name] = i;
        }

        // Blank nodes the evaluator minted, by their labels, unique in the execution.
        Dictionary<string, TermHandle> minted = new(StringComparer.Ordinal);
        TermHandle withGraph = TermHandle.None;
        bool withKnown = with is null || state.TryInternalise(with, out withGraph);

        Solution solution = new(this, solutions, columns, minted);

        while (solutions.MoveNext())
        {

            if (withKnown)
            {
                foreach (QuadPattern pattern in deleteTemplate)
                {
                    if (solution.TryDelete(pattern, withGraph, out Quad quad))
                    {
                        delete.Add(quad);
                    }
                }
            }

            TermHandle insertGraph = with is null ? TermHandle.None : _staging.Stage(with);
            Dictionary<string, TermHandle> fresh = new(StringComparer.Ordinal);

            foreach (QuadPattern pattern in insertTemplate)
            {
                if (solution.TryInsert(pattern, insertGraph, fresh, out Quad quad))
                {
                    insert.Add(quad);
                }
            }
        }
    }

    private static void Collect(AlgebraList<QuadPattern> template, List<Variable> variables, HashSet<string> seen)
    {
        foreach (QuadPattern pattern in template)
        {
            Collect(pattern.Subject, variables, seen);
            Collect(pattern.Predicate, variables, seen);
            Collect(pattern.Object, variables, seen);

            if (pattern.Graph is { } graph)
            {
                Collect(graph, variables, seen);
            }
        }
    }

    private static void Collect(PatternTerm term, List<Variable> variables, HashSet<string> seen)
    {
        switch (term)
        {
            case VariablePattern variable when seen.Add(variable.Variable.Name):
                variables.Add(variable.Variable);
                break;
            case TripleTermPattern triple:
                Collect(triple.Subject, variables, seen);
                Collect(triple.Predicate, variables, seen);
                Collect(triple.Object, variables, seen);
                break;
        }
    }

    /// <summary><c>DELETE WHERE { Q }</c>'s pattern: <c>Q</c> as triples blocks, one per graph, joined (§3.1.3.3).</summary>
    private static QueryPattern PatternOf(AlgebraList<QuadPattern> quads)
    {
        QueryPattern? pattern = null;
        int i = 0;

        while (i < quads.Count)
        {
            PatternTerm? graph = quads[i].Graph;
            List<TriplePattern> block = [];

            while (i < quads.Count && Equals(quads[i].Graph, graph))
            {
                block.Add(new TriplePattern(quads[i].Subject, quads[i].Predicate, quads[i].Object));
                i++;
            }

            QueryPattern part = new Bgp(AlgebraList.From(block));

            if (graph is not null)
            {
                part = new Graph(graph, part);
            }

            pattern = pattern is null ? part : new Join(pattern, part);
        }

        return pattern ?? new Bgp(AlgebraList.Empty<TriplePattern>());
    }

    /// <summary>One solution's instantiation of the templates.</summary>
    private sealed class Solution(
        RequestExecution owner,
        SolutionResults solutions,
        Dictionary<string, int> columns,
        Dictionary<string, TermHandle> minted)
    {
        internal bool TryDelete(QuadPattern pattern, TermHandle defaultGraph, out Quad quad)
        {
            quad = default;
            TermHandle g = defaultGraph;

            if (!TryExisting(pattern.Subject, out TermHandle s)
                || !TryExisting(pattern.Predicate, out TermHandle p)
                || !TryExisting(pattern.Object, out TermHandle o)
                || (pattern.Graph is { } graph && !TryExisting(graph, out g)))
            {
                return false;
            }

            quad = new Quad(s, p, o, g);
            return owner.IsWellFormed(in quad);
        }

        internal bool TryInsert(QuadPattern pattern, TermHandle defaultGraph, Dictionary<string, TermHandle> fresh, out Quad quad)
        {
            quad = default;
            TermHandle g = defaultGraph;

            if (!TryNew(pattern.Subject, fresh, out TermHandle s)
                || !TryNew(pattern.Predicate, fresh, out TermHandle p)
                || !TryNew(pattern.Object, fresh, out TermHandle o)
                || (pattern.Graph is { } graph && !TryNew(graph, fresh, out g)))
            {
                return false;
            }

            quad = new Quad(s, p, o, g);
            return owner.IsWellFormed(in quad);
        }

        // For DELETE: a term the state holds, or the quad cannot be there.
        private bool TryExisting(PatternTerm term, out TermHandle handle)
        {
            handle = TermHandle.None;

            switch (term)
            {
                case TermPattern constant:
                    return owner._staging.TryInternalise(constant.Term, out handle);

                case VariablePattern variable:
                    if (!columns.TryGetValue(variable.Variable.Name, out int column))
                    {
                        return false;
                    }

                    if (solutions.TryGetHandle(column, out handle))
                    {
                        return true;
                    }

                    return solutions.TryGetTerm(column, out RdfTerm? value)
                        && value!.Kind != RdfTermKind.BlankNode
                        && owner._staging.TryInternalise(value, out handle);

                case TripleTermPattern triple:
                    return TryExisting(triple.Subject, out TermHandle s)
                        && TryExisting(triple.Predicate, out TermHandle p)
                        && TryExisting(triple.Object, out TermHandle o)
                        && owner.TryExistingTriple(s, p, o, out handle);

                default:
                    // A blank node in a DELETE template never parses (§3.1.3, Note 9).
                    return false;
            }
        }

        // For INSERT: any term, staged when the state has none.
        private bool TryNew(PatternTerm term, Dictionary<string, TermHandle> fresh, out TermHandle handle)
        {
            handle = TermHandle.None;

            switch (term)
            {
                case TermPattern constant:
                    handle = owner.Stage(constant.Term, label => Fresh(fresh, label));
                    return true;

                case BlankNodePattern blank:
                    handle = Fresh(fresh, blank.Label);
                    return true;

                case VariablePattern variable:
                    if (!columns.TryGetValue(variable.Variable.Name, out int column))
                    {
                        return false;
                    }

                    if (solutions.TryGetHandle(column, out handle))
                    {
                        return true;
                    }

                    if (!solutions.TryGetTerm(column, out RdfTerm? value))
                    {
                        return false;
                    }

                    Dictionary<string, TermHandle> byLabel = minted;
                    handle = owner.Stage(value!, label => Fresh(byLabel, label));
                    return true;

                case TripleTermPattern triple:
                    if (!TryNew(triple.Subject, fresh, out TermHandle s)
                        || !TryNew(triple.Predicate, fresh, out TermHandle p)
                        || !TryNew(triple.Object, fresh, out TermHandle o))
                    {
                        return false;
                    }

                    handle = owner._staging.StageTriple(s, p, o);
                    return true;

                default:
                    return false;
            }
        }

        private TermHandle Fresh(Dictionary<string, TermHandle> labels, string label)
        {
            if (!labels.TryGetValue(label, out TermHandle handle))
            {
                handle = owner._staging.StageBlank();
                labels[label] = handle;
            }

            return handle;
        }
    }

    private bool TryExistingTriple(TermHandle s, TermHandle p, TermHandle o, out TermHandle handle)
    {
        handle = TermHandle.None;
        return _staging.TryExternalise(s, out RdfTerm? subject)
            && _staging.TryExternalise(p, out RdfTerm? predicate)
            && _staging.TryExternalise(o, out RdfTerm? @object)
            && subject.Kind != RdfTermKind.BlankNode
            && @object.Kind != RdfTermKind.BlankNode
            && _staging.TryInternalise(RdfTerm.TripleTerm(subject, predicate, @object), out handle);
    }

    /// <summary>
    /// §3.1.3: a triple with "an illegal RDF construct, such as a literal in a
    /// subject or predicate position" is not included. A subject is an IRI or
    /// a blank node, a predicate an IRI, a graph name an IRI.
    /// </summary>
    private bool IsWellFormed(in Quad quad) =>
        KindOf(quad.Subject) is RdfTermKind.Iri or RdfTermKind.BlankNode
        && KindOf(quad.Predicate) is RdfTermKind.Iri
        && (quad.Graph.IsNone || KindOf(quad.Graph) is RdfTermKind.Iri);

    private RdfTermKind? KindOf(TermHandle handle) =>
        _staging.TryExternalise(handle, out RdfTerm? term) ? term.Kind : null;

    // ---- LOAD — §3.1.4.

    private async Task LoadAsync(Load load, int index, List<Quad> insert)
    {
        LoadedDocument document;

        try
        {
            document = await _options.LoadSource.LoadAsync(load.Source, _cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            document = LoadedDocument.Failed(error.Message);
        }

        List<Quad> loaded = [];
        string? failure = document.Failure ?? Parse(document, load.Graph, loaded);

        if (failure is null)
        {
            insert.AddRange(loaded);
        }
        else if (!load.Silent)
        {
            throw new SparqlUpdateException(index, "LOAD", "<" + System.Text.Encoding.UTF8.GetString(load.Source.Lexical) + ">: " + failure);
        }
    }

    // Parses the document into quads; its blank nodes fresh, one per label per load.
    private string? Parse(LoadedDocument document, RdfTerm? into, List<Quad> quads)
    {
        Dictionary<string, TermHandle> blanks = new(StringComparer.Ordinal);
        TermHandle intoGraph = into is null ? TermHandle.None : _staging.Stage(into);

        TermHandle Term(RdfTerm term) => Stage(term, label =>
        {
            if (!blanks.TryGetValue(label, out TermHandle handle))
            {
                handle = _staging.StageBlank();
                blanks[label] = handle;
            }

            return handle;
        });

        void Handle(in QuadView quad)
        {
            TermHandle graph = into is not null ? intoGraph
                : quad.HasGraph ? Term(quad.Graph.Materialise())
                : TermHandle.None;
            quads.Add(new Quad(Term(quad.Subject.Materialise()), Term(quad.Predicate.Materialise()), Term(quad.Object.Materialise()), graph));
        }

        ParseResult result = document.Syntax is RdfSyntax.NTriples or RdfSyntax.NQuads
            ? NQuadsParser.Parse(document.Content.Span, Handle, new ParseOptions { Syntax = document.Syntax })
            : TurtleParser.Parse(document.Content.Span, Handle, new TurtleOptions { Syntax = document.Syntax, BaseIri = document.BaseIri });

        return result.Succeeded ? null : "the document does not parse: " + result.FirstError;
    }

    // ---- Graph management — §3.1.5, §3.2.

    private void AddTarget(GraphTarget target, List<Quad> delete)
    {
        IQuadSource state = Current();

        switch (target.Kind)
        {
            case GraphTargetKind.Default:
                AddAll(state, GraphPattern.DefaultGraph, delete);
                break;
            case GraphTargetKind.Named:
                AddAll(state, GraphPattern.AnyNamed, delete);
                break;
            case GraphTargetKind.All:
                AddAll(state, GraphPattern.Any, delete);
                break;
            default:
                if (state.TryInternalise(target.Graph!, out TermHandle graph))
                {
                    AddAll(state, GraphPattern.Named(graph), delete);
                }

                break;
        }
    }

    private static void AddAll(IQuadSource state, GraphPattern graph, List<Quad> quads)
    {
        using IQuadCursor cursor = state.Match(TermHandle.None, TermHandle.None, TermHandle.None, graph);

        while (cursor.MoveNext())
        {
            quads.Add(cursor.Current);
        }
    }

    // A graph exists when it holds a quad (ADR 0057); CREATE of one fails, SHOULD (§3.2.1).
    private void Create(Create create, int index)
    {
        IQuadSource state = Current();

        if (create.Silent || !state.TryInternalise(create.Graph, out TermHandle graph))
        {
            return;
        }

        using IQuadCursor cursor = state.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph));

        if (cursor.MoveNext())
        {
            throw new SparqlUpdateException(
                index, "CREATE", "<" + System.Text.Encoding.UTF8.GetString(create.Graph.Lexical) + "> already exists: it holds a quad (§3.2.1).");
        }
    }

    /// <summary>
    /// <c>ADD</c>, <c>COPY</c> and <c>MOVE</c> (§3.2.3–§3.2.5): the source's
    /// triples into the target, the target cleared first for <c>COPY</c> and
    /// <c>MOVE</c>, the source cleared after for <c>MOVE</c>. The same graph
    /// both ways does nothing. A graph that does not exist is an empty one.
    /// </summary>
    private void Transfer(GraphOrDefault from, GraphOrDefault to, bool clearTarget, bool clearSource, List<Quad> delete, List<Quad> insert)
    {
        if (from.IsDefault == to.IsDefault && (from.IsDefault || from.Graph!.Equals(to.Graph)))
        {
            return;
        }

        IQuadSource state = Current();
        List<Quad> source = [];

        if (TryPattern(state, from, out GraphPattern fromPattern))
        {
            AddAll(state, fromPattern, source);
        }

        if (clearTarget && TryPattern(state, to, out GraphPattern toPattern))
        {
            AddAll(state, toPattern, delete);
        }

        if (clearSource)
        {
            delete.AddRange(source);
        }

        TermHandle target = to.IsDefault ? TermHandle.None : _staging.Stage(to.Graph!);

        foreach (Quad quad in source)
        {
            insert.Add(new Quad(quad.Subject, quad.Predicate, quad.Object, target));
        }
    }

    private static bool TryPattern(IQuadSource state, GraphOrDefault graph, out GraphPattern pattern)
    {
        if (graph.IsDefault)
        {
            pattern = GraphPattern.DefaultGraph;
            return true;
        }

        pattern = default;

        if (!state.TryInternalise(graph.Graph!, out TermHandle handle))
        {
            return false;
        }

        pattern = GraphPattern.Named(handle);
        return true;
    }

    private static string KindOf(UpdateOperation operation) => operation switch
    {
        InsertData _ => "INSERT DATA",
        DeleteData _ => "DELETE DATA",
        DeleteWhere _ => "DELETE WHERE",
        Algebra.Modify _ => "DELETE/INSERT",
        Load _ => "LOAD",
        Clear _ => "CLEAR",
        Drop _ => "DROP",
        Algebra.Create _ => "CREATE",
        Algebra.Add _ => "ADD",
        Copy _ => "COPY",
        Move _ => "MOVE",
        _ => operation.GetType().Name,
    };
}
