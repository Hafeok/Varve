// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Xsd;

namespace Varve.Sparql.Evaluation.Execution;

/// <summary>
/// One execution: the source and its equality, the options, the cancellation
/// token, the local term table, the dataset, and the clock value read once.
/// Nothing here outlives the results it serves.
/// </summary>
internal sealed class Exec
{
    private readonly Dictionary<(string Pattern, string Flags), Regex?> _regexes = [];
    private List<TermHandle>? _namedGraphs;
    private DateTimeOffset? _now;
    private int _blankNodes;
    private ulong[]? _blankNodeRow;
    private Dictionary<RdfTerm, TermRef>? _blankNodesByLabel;
    private int _steps;

    internal Exec(IQuadSource source, EvaluationOptions options, int width, CancellationToken cancellationToken)
    {
        Source = source;
        Comparer = source.TermComparer;
        Options = options;
        Width = width;
        RowLength = Rows.Length(width);
        Token = cancellationToken;
        Materialising = options.ValueAccess == ValueAccess.Materialise;
    }

    internal IQuadSource Source { get; }

    internal IEqualityComparer<TermHandle> Comparer { get; }

    internal EvaluationOptions Options { get; }

    internal CancellationToken Token { get; }

    internal LocalTerms Locals { get; } = new();

    internal int Width { get; }

    internal int RowLength { get; }

    internal bool Materialising { get; }

    /// <summary>The FROM graphs, or null for the source's default graph.</summary>
    internal TermHandle[]? DefaultGraphs { get; set; }

    /// <summary>The FROM NAMED graphs, or null for every named graph the source has.</summary>
    internal List<TermHandle>? NamedGraphList { get; set; }

    internal HashSet<TermHandle>? NamedGraphSet { get; set; }

    /// <summary>The base IRI of the query, for IRI().</summary>
    internal RdfTerm? BaseIri { get; set; }

    internal ulong[] NewRow() => new ulong[RowLength];

    /// <summary>Throws if cancelled; cheap enough for every solution.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Check() => Token.ThrowIfCancellationRequested();

    /// <summary>Throws if cancelled, looking only every 1,024 calls: for scans and searches.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Step()
    {
        if ((++_steps & 1023) == 0)
        {
            Token.ThrowIfCancellationRequested();
        }
    }

    // --------------------------------------------------------------- terms

    /// <summary>A handle a scan found, as a slot value: itself, or in the materialised arm an owned term.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal TermRef FromSource(TermHandle handle)
    {
        if (!Materialising)
        {
            return new TermRef(handle.Value, false);
        }

        return MaterialiseFromSource(handle);
    }

    private TermRef MaterialiseFromSource(TermHandle handle) =>
        Source.TryExternalise(handle, out RdfTerm? term)
            ? new TermRef(Locals.Intern(term, handle), true)
            : new TermRef(handle.Value, false);

    /// <summary>
    /// The source's handle for a slot value, when the source has the term. In
    /// every arm but the materialised one a local term is never the source's,
    /// by construction (§4.1), so no lookup is made. In the materialised arm a
    /// term a scan found goes back to the scan by the handle it came from —
    /// the arm still joins on handles, which is why its cost is a lower bound
    /// (§10) — and any other term is looked up.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryGetSourceHandle(TermRef value, out TermHandle handle)
    {
        if (!value.IsLocal)
        {
            handle = new TermHandle(value.Raw);
            return true;
        }

        if (Materialising)
        {
            return Locals.TryGetOrigin(value.Raw, out handle) || Source.TryInternalise(Locals.Get(value.Raw), out handle);
        }

        handle = default;
        return false;
    }

    /// <summary>A computed term as a slot value: the source's handle if it has the term, a local term otherwise.</summary>
    internal TermRef Intern(RdfTerm term)
    {
        if (!Materialising && Source.TryInternalise(term, out TermHandle handle))
        {
            return new TermRef(handle.Value, false);
        }

        return new TermRef(Locals.Intern(term), true);
    }

    /// <summary>A term minted by this execution — a blank node — which never equals one of the source's.</summary>
    internal TermRef InternLocal(RdfTerm term) => new(Locals.Intern(term), true);

    /// <summary>The term a slot value names.</summary>
    internal RdfTerm Materialise(TermRef value)
    {
        if (value.IsLocal)
        {
            return Locals.Get(value.Raw);
        }

        if (!Source.TryExternalise(new TermHandle(value.Raw), out RdfTerm? term))
        {
            throw new QueryEvaluationException("The source could not externalise a term it returned; if it is a private term, its key has been destroyed.");
        }

        return term;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TermEquals(TermRef left, TermRef right)
    {
        if (left.IsLocal != right.IsLocal)
        {
            return false;
        }

        if (left.Raw == right.Raw)
        {
            return true;
        }

        return !left.IsLocal && Comparer.Equals(new TermHandle(left.Raw), new TermHandle(right.Raw));
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int TermHash(TermRef value) =>
        value.IsLocal ? HashCode.Combine(value.Raw, 0x5bd1e995) : Comparer.GetHashCode(new TermHandle(value.Raw));

    /// <summary>A fresh blank node for this execution (§7.9). Its label cannot come from a parse: it begins with a dot.</summary>
    internal RdfTerm MintBlankNode()
    {
        int n = ++_blankNodes;
        return RdfTerm.BlankNode(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $".b{n}")));
    }

    /// <summary>BNODE(label): one node per label per solution, the solution recognised by its array (§17.4.2.9).</summary>
    internal TermRef BlankNodeFor(ulong[] row, RdfTerm label)
    {
        _blankNodesByLabel ??= new Dictionary<RdfTerm, TermRef>(RdfTerm.Comparer);
        if (!ReferenceEquals(row, _blankNodeRow))
        {
            _blankNodeRow = row;
            _blankNodesByLabel.Clear();
        }

        if (!_blankNodesByLabel.TryGetValue(label, out TermRef node))
        {
            node = InternLocal(MintBlankNode());
            _blankNodesByLabel.Add(label, node);
        }

        return node;
    }

    /// <summary>
    /// An Extend copied a solution to bind one more variable: it is still the
    /// same solution for BNODE(str), so the memo follows the copy.
    /// </summary>
    internal void SameSolution(ulong[] from, ulong[] to)
    {
        if (ReferenceEquals(from, _blankNodeRow))
        {
            _blankNodeRow = to;
        }
    }

    // --------------------------------------------------------------- world

    /// <summary>The instant of this execution, read from the injected clock once (ADR 0056).</summary>
    internal XsdDateTime Now()
    {
        if (_now is null)
        {
            TimeProvider clock = Options.Clock
                ?? throw new QueryEvaluationException("NOW() needs a clock: set EvaluationOptions.Clock (for the system clock, Clock = TimeProvider.System).");
            DateTimeOffset utc = clock.GetUtcNow();
            TimeSpan offset = clock.LocalTimeZone.GetUtcOffset(utc);
            _now = utc.ToOffset(offset);
        }

        DateTimeOffset now = _now.Value;
        XsdDecimal seconds = XsdDecimal.FromMantissa(
            ((System.Int128)now.Second * 1_000_000_000_000_000_000L) + ((System.Int128)(now.Ticks % TimeSpan.TicksPerSecond) * 100_000_000_000L));
        return new XsdDateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, seconds, (int)now.Offset.TotalMinutes);
    }

    internal void RandomBytes(Span<byte> destination, string function)
    {
        IRandomSource random = Options.Randomness
            ?? throw new QueryEvaluationException(function + " needs randomness: set EvaluationOptions.Randomness.");
        random.NextBytes(destination);
    }

    internal Regex? Regex(string pattern, string flags, Func<string, string, Regex?> compile)
    {
        if (!_regexes.TryGetValue((pattern, flags), out Regex? regex))
        {
            regex = compile(pattern, flags);
            _regexes[(pattern, flags)] = regex;
        }

        return regex;
    }

    /// <summary>The named graphs of the dataset: the FROM NAMED list, or every graph name the source has, found once.</summary>
    internal List<TermHandle> NamedGraphs()
    {
        if (NamedGraphList is not null)
        {
            return NamedGraphList;
        }

        if (_namedGraphs is null)
        {
            _namedGraphs = [];
            HashSet<TermHandle> seen = new(Comparer);
            using IQuadCursor cursor = Source.Match(default, default, default, GraphPattern.AnyNamed);
            while (cursor.MoveNext())
            {
                Step();
                if (seen.Add(cursor.Current.Graph))
                {
                    _namedGraphs.Add(cursor.Current.Graph);
                }
            }
        }

        return _namedGraphs;
    }

    /// <summary>Whether a graph is one of the dataset's named graphs.</summary>
    internal bool IsNamedGraph(TermHandle graph)
    {
        if (NamedGraphSet is not null)
        {
            return NamedGraphSet.Contains(graph);
        }

        using IQuadCursor cursor = Source.Match(default, default, default, GraphPattern.Named(graph));
        return cursor.MoveNext();
    }
}
