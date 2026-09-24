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
using Varve.Rdf;

namespace Varve.Store.Tests.Model;

/// <summary>
/// Feeds one script to a store and to the reference model in lockstep and
/// compares them after every step and over the finished run.
/// </summary>
internal sealed class Harness : IAsyncDisposable
{
    private static readonly RdfTerm Forbidden = T.Iri("forbidden");
    private static readonly RdfTerm P1 = T.Iri("p1");
    private static readonly RdfTerm Admin = T.Iri("admin");
    private static readonly RdfTerm Why = T.Iri("why");

    private readonly Dictionary<RdfTerm, TermHandle> _blankHandles = [];
    private readonly Dictionary<string, RdfTerm> _storeToModel = new(StringComparer.Ordinal);
    private readonly List<(long Position, DatasetView View)> _pins = [];
    private readonly HashSet<char> _labelsUsed = [];
    private int _nextBlank;

    private Harness(MemoryStorage storage, ManualClock clock, Dataset dataset, DatasetOptions options)
    {
        Storage = storage;
        Clock = clock;
        Dataset = dataset;
        Options = options;
    }

    public MemoryStorage Storage { get; }

    public ManualClock Clock { get; }

    public Dataset Dataset { get; private set; }

    public DatasetOptions Options { get; }

    public ReferenceModel Model { get; } = new();

    public RecordingProjection Recorder { get; } = new();

    public static async Task<Harness> StartAsync(int maxRecordBytes = 1 << 20, long segmentBytes = 64L << 20)
    {
        MemoryStorage storage = new();
        ManualClock clock = ManualClock.Epoch();
        DatasetOptions options = T.Options(clock, maxRecordBytes, segmentBytes);
        return new Harness(storage, clock, await Dataset.OpenAsync(storage, options, T.Ct), options);
    }

    public async Task RunAsync(Script script)
    {
        foreach (Step step in script.Steps)
        {
            await StepAsync(step);
        }
    }

    public async Task StepAsync(Step step)
    {
        switch (step)
        {
            case CommitStep commit:
                await CommitAsync(commit);
                break;

            case SettingsStep settings:
                long? expected = Expected(settings.Expected);
                Expected model = Model.ChangeSettings(settings.Scope, Admin, Why, expected, Clock.Now.UtcTicks);
                CommitResult result = await Dataset.ChangeSettingsAsync(
                    new SettingsChange { DefaultAccessScope = settings.Scope },
                    new CommitMetadata { Agent = Admin, Cause = Why },
                    expected,
                    T.Ct);
                Check(model, result, step);
                Coverage.Hit(Coverage.Settings);
                await AfterAsync(result);
                break;

            case ClockStep clock:
                Clock.Now = Clock.Now.AddSeconds(clock.Seconds);

                if (clock.Seconds < 0)
                {
                    Coverage.Hit(Coverage.ClockBackwards);
                }

                break;

            case CheckpointStep checkpoint when Model.Head > 0:
                await Dataset.CheckpointAsync(1 + (checkpoint.Choice % Model.Head), T.Ct);
                Coverage.Hit(Coverage.Checkpoint);
                break;
        }
    }

    private async Task CommitAsync(CommitStep step)
    {
        Dictionary<char, RdfTerm> labels = [];
        List<(bool, MQuad)> modelOps = [];
        CommitRequest request = new()
        {
            ExpectedPosition = Expected(step.Expected),
            Metadata = new CommitMetadata { Agent = step.Agent == 0 ? RequestTerm.None : T.Iri("agent" + step.Agent) },
        };

        List<MQuad> ordered = [.. Model.Graph.OrderBy(q => Render(q), StringComparer.Ordinal)];
        HashSet<MQuad> touched = [];

        foreach (OpSpec op in step.Ops)
        {
            MQuad quad;
            RequestTerm s, p, o, g;

            if (op.Existing >= 0 && ordered.Count > 0)
            {
                quad = ordered[op.Existing % ordered.Count];
                (s, p, o) = (Existing(quad.S), Existing(quad.P), Existing(quad.O));
                g = quad.G is null ? RequestTerm.None : Existing(quad.G);
            }
            else
            {
                (RdfTerm ms, RequestTerm rs) = Resolve(op.S, labels);
                (RdfTerm mp, RequestTerm rp) = Resolve(op.P, labels);
                (RdfTerm mo, RequestTerm ro) = Resolve(op.O, labels);
                RdfTerm? mg = op.Graph == 0 ? null : T.Iri("g" + (op.Graph - 1));
                quad = new MQuad(ms, mp, mo, mg);
                (s, p, o, g) = (rs, rp, ro, mg is null ? RequestTerm.None : mg);
            }

            bool present = Model.Graph.Contains(quad);

            if (op.Assert && present)
            {
                Coverage.Hit(Coverage.RedundantAssert);
            }

            if (!op.Assert && !present)
            {
                Coverage.Hit(!touched.Contains(quad) ? Coverage.RedundantRetract : Coverage.AssertThenRetractAbsent);
            }

            if (op.Assert && !present)
            {
                touched.Add(quad);
            }

            modelOps.Add((op.Assert, quad));

            if (op.Assert)
            {
                request.Assert(s, p, o, g);
            }
            else
            {
                request.Retract(s, p, o, g);
            }
        }

        if (step.Validator != ValidatorKind.None)
        {
            request.Validators.Add(new ScriptValidator(step.Validator));
        }

        RdfTerm? agent = step.Agent == 0 ? null : T.Iri("agent" + step.Agent);
        Expected model = Model.Commit(modelOps, agent, request.ExpectedPosition, (after, a, r) => Validate(step.Validator, after, a), Clock.Now.UtcTicks, out List<RdfTerm> freshBlanks);
        CommitResult result = await Dataset.CommitAsync(request, T.Ct);
        Check(model, result, step);

        if (step.Expected == Expect.Right)
        {
            Coverage.Hit(Coverage.ExpectedRight);
        }

        Coverage.Hit(result.Outcome switch
        {
            CommitOutcome.Conflict => Coverage.Conflict,
            CommitOutcome.Rejected => Coverage.Rejected,
            CommitOutcome.NoChange => Coverage.NoChange,
            _ => Coverage.Committed,
        });

        if (result.Outcome != CommitOutcome.Committed)
        {
            // I3: a rejected, empty or conflicting request leaves no trace in the dictionary.
            using DatasetView view = Dataset.Pin();

            foreach (RdfTerm term in modelOps.SelectMany(op => ReferenceModel.Terms(op.Item2)).Where(t => t.Kind != RdfTermKind.BlankNode))
            {
                if (!Model.IsKnown(term) && view.TryInternalise(term, out _))
                {
                    throw new InvalidOperationException("I3: a term from a request that did not commit reached the dictionary: " + T.Render(term));
                }
            }
        }

        await AfterAsync(result);

        if (result.Outcome == CommitOutcome.Committed)
        {
            // Relate the store's fresh blank ids to the model's, in allocation order.
            Commit commit = Recorder.Commits[^1];
            TermAllocation[] blanks = [.. commit.Allocations.ToArray().Where(a => a.Term.Kind == RdfTermKind.BlankNode)];

            if (blanks.Length != freshBlanks.Count)
            {
                throw new InvalidOperationException("The store allocated " + blanks.Length + " blank nodes where the model expected " + freshBlanks.Count + ".");
            }

            for (int i = 0; i < blanks.Length; i++)
            {
                _blankHandles[freshBlanks[i]] = blanks[i].Handle;
                _storeToModel[Encoding.UTF8.GetString(blanks[i].Term.Lexical)] = freshBlanks[i];
            }

            if (commit.Attachments.Length > 0)
            {
                Coverage.Hit(Coverage.Attached);
            }
        }

        foreach (char label in labels.Keys)
        {
            if (!_labelsUsed.Add(label))
            {
                Coverage.Hit(Coverage.LabelReused);
            }
        }

        if (result.Outcome == CommitOutcome.Committed)
        {
            await CompareHeadAsync("after commit " + result.Position);
        }

        if (step.Pin && !Dataset.IsFailed)
        {
            _pins.Add((Dataset.Head, Dataset.Pin()));
        }
    }

    private async Task AfterAsync(CommitResult result)
    {
        if (result.Outcome == CommitOutcome.Committed)
        {
            await Dataset.CatchUpAsync(Recorder, T.Ct);
        }
    }

    private long? Expected(Expect expect) => expect switch
    {
        Expect.Right => Model.Head,
        Expect.Stale => Model.Head == 0 ? 7 : Model.Head - 1,
        _ => null,
    };

    private static void Check(Expected model, CommitResult result, Step step)
    {
        if (model.Outcome != result.Outcome || model.Position != result.Position)
        {
            throw new InvalidOperationException(
                "Model said " + model.Outcome + "(" + model.Position + "), store said " + result + " for\n" + step);
        }
    }

    private RequestTerm Existing(RdfTerm term) =>
        term.Kind == RdfTermKind.BlankNode ? RequestTerm.Existing(_blankHandles[term]) : term;

    private (RdfTerm Model, RequestTerm Store) Resolve(TermSpec spec, Dictionary<char, RdfTerm> labels)
    {
        switch (spec)
        {
            case IriSpec iri:
                return (T.Iri(iri.Local), T.Iri(iri.Local));

            case LiteralSpec literal:
                return (Terms.Literal(literal.Which), Terms.Literal(literal.Which));

            case ExistingBlankSpec existing when Model.Blanks.Count > 0:
                Coverage.Hit(Coverage.ExistingBlank);
                RdfTerm node = Model.Blanks[existing.Index % Model.Blanks.Count];
                return (node, RequestTerm.Existing(_blankHandles[node]));

            case ExistingBlankSpec:
                return Resolve(new LabelSpec('z'), labels);

            case LabelSpec label:
                if (!labels.TryGetValue(label.Label, out RdfTerm? blank))
                {
                    blank = T.Blank("m" + (_nextBlank++).ToString(CultureInfo.InvariantCulture));
                    labels[label.Label] = blank;
                }

                return (blank, T.Blank(label.Label.ToString()));

            case TripleSpec triple:
                (RdfTerm s, RequestTerm _) = Resolve(triple.S, labels);
                (RdfTerm p, RequestTerm _) = Resolve(triple.P, labels);
                (RdfTerm o, RequestTerm _) = Resolve(triple.O, labels);
                RdfTerm term = RdfTerm.TripleTerm(s, p, o);
                return (term, term);

            default:
                throw new InvalidOperationException(spec.ToString());
        }
    }

    private static (bool Accept, RdfTerm? Attachment) Validate(ValidatorKind kind, HashSet<MQuad> after, HashSet<MQuad> asserted) => kind switch
    {
        ValidatorKind.RejectForbidden => (!asserted.Any(q => q.P.Equals(Forbidden)), null),
        ValidatorKind.AttachCount => (true, T.Iri("report" + asserted.Count)),
        ValidatorKind.RejectCrowded => (after.Count(q => q.P.Equals(P1)) <= 3, null),
        _ => (true, null),
    };

    /// <summary>The same rules as <see cref="Validate"/>, stated over the store's contract.</summary>
    private sealed class ScriptValidator(ValidatorKind kind) : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            switch (kind)
            {
                case ValidatorKind.RejectForbidden:
                    if (proposed.TryInternalise(Forbidden, out TermHandle forbidden))
                    {
                        foreach (Quad quad in delta.Asserted)
                        {
                            if (proposed.TermComparer.Equals(quad.Predicate, forbidden))
                            {
                                return ValidationVerdict.Reject([T.Iri("rejected")]);
                            }
                        }
                    }

                    return ValidationVerdict.Accept();

                case ValidatorKind.AttachCount:
                    return ValidationVerdict.Accept(T.Iri("report" + delta.Asserted.Length));

                case ValidatorKind.RejectCrowded:
                    int crowd = proposed.TryInternalise(P1, out TermHandle p1)
                        ? T.Drain(proposed.Match(TermHandle.None, p1, TermHandle.None, GraphPattern.Any)).Count
                        : 0;
                    return crowd <= 3 ? ValidationVerdict.Accept() : ValidationVerdict.Reject([T.Iri("crowded")]);

                default:
                    return ValidationVerdict.Accept();
            }
        }
    }

    // --- comparison -----------------------------------------------------------

    public static SortedSet<string> Rendered(HashSet<MQuad> quads) => [.. quads.Select(Render)];

    public static string Render(MQuad quad) =>
        T.Render(quad.S) + " " + T.Render(quad.P) + " " + T.Render(quad.O) + (quad.G is null ? string.Empty : " " + T.Render(quad.G));

    /// <summary>A store source's quads, with its blank labels translated to the model's.</summary>
    public SortedSet<string> Rendered(IQuadSource source) =>
        [.. T.All(source).Select(q => Render(source, q))];

    public string Render(IQuadSource source, Quad quad) =>
        Render(Translate(source, quad.Subject), Translate(source, quad.Predicate), Translate(source, quad.Object),
            quad.Graph.IsNone ? null : Translate(source, quad.Graph));

    private static string Render(RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) => Render(new MQuad(s, p, o, g));

    public RdfTerm Translate(IQuadSource source, TermHandle handle)
    {
        if (!source.TryExternalise(handle, out RdfTerm? term))
        {
            throw new InvalidOperationException("A handle in a quad does not externalise: " + handle.Value);
        }

        return Translate(term);
    }

    public RdfTerm Translate(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.BlankNode => _storeToModel.TryGetValue(Encoding.UTF8.GetString(term.Lexical), out RdfTerm? model)
            ? model
            : T.Blank("unmapped-" + Encoding.UTF8.GetString(term.Lexical)),
        RdfTermKind.TripleTerm => RdfTerm.TripleTerm(Translate(term.Subject!), Translate(term.Predicate!), Translate(term.Object!)),
        _ => term,
    };

    public async Task CompareHeadAsync(string when)
    {
        if (Dataset.IsFailed)
        {
            return;
        }

        using DatasetView view = Dataset.Pin();
        Same(Rendered(Model.Graph), Rendered(view), when);
        await Task.CompletedTask;
    }

    public static void Same(SortedSet<string> expected, SortedSet<string> actual, string when)
    {
        if (!expected.SetEquals(actual))
        {
            throw new InvalidOperationException(
                when + ": the store and the model disagree.\n  only in the model: " + string.Join(" | ", expected.Except(actual))
                + "\n  only in the store: " + string.Join(" | ", actual.Except(expected)));
        }
    }

    /// <summary>The checks that need the whole run: R1, R2, R3, I2, I3, I5, settings, reopen, rebuild.</summary>
    public async Task VerifyRunAsync(CancellationToken cancellationToken)
    {
        long head = Model.Head;

        // R1: every pinned read still answers for its own position.
        foreach ((long position, DatasetView view) in _pins)
        {
            Same(Rendered(Model.History[(int)position]), Rendered(view), "R1: pin at " + position);
        }

        // R2 and R4: as-of via checkpoint and overlay equals full replay, at every position.
        for (long p = 0; p <= head; p++)
        {
            using DatasetView view = await Dataset.AsOfAsync(p, cancellationToken);
            Same(Rendered(Model.History[(int)p]), Rendered(view), "R2: as-of " + p);

            if ((await Dataset.SettingsAtAsync(p, cancellationToken)).DefaultAccessScope != Model.Settings[(int)p])
            {
                throw new InvalidOperationException("Settings at " + p + " are not the fold of the settings commits up to it.");
            }
        }

        using DatasetView headView = Dataset.IsFailed ? await Dataset.AsOfAsync(head, cancellationToken) : Dataset.Pin();

        // R3: Diff is the set difference of the model's graphs, forwards and backwards.
        for (long p1 = 0; p1 <= head; p1 += Math.Max(1, head / 5))
        {
            for (long p2 = 0; p2 <= head; p2 += Math.Max(1, head / 4))
            {
                QuadDelta diff = await Dataset.DiffAsync(p1, p2, cancellationToken);
                HashSet<MQuad> g1 = Model.History[(int)p1];
                HashSet<MQuad> g2 = Model.History[(int)p2];
                Same(Rendered([.. g2.Except(g1)]), [.. diff.Asserted.ToArray().Select(q => Render(headView, q))], "R3: diff " + p1 + ".." + p2 + " asserted");
                Same(Rendered([.. g1.Except(g2)]), [.. diff.Retracted.ToArray().Select(q => Render(headView, q))], "R3: diff " + p1 + ".." + p2 + " retracted");
            }
        }

        // I2 and I3, commit by commit, from the log.
        foreach (Commit commit in Recorder.Commits)
        {
            long p = commit.Position;
            (HashSet<MQuad> a, HashSet<MQuad> r) = Model.Deltas[(int)p];
            Same(Rendered(a), [.. commit.Delta.Asserted.ToArray().Select(q => Render(headView, q))], "I2: A at " + p);
            Same(Rendered(r), [.. commit.Delta.Retracted.ToArray().Select(q => Render(headView, q))], "I2: R at " + p);
            Same([.. Model.Allocated[(int)p].Select(T.Render)], [.. commit.Allocations.ToArray().Select(x => T.Render(Translate(x.Term)))], "I3: alloc at " + p);

            using DatasetView before = await Dataset.AsOfAsync(p - 1, cancellationToken);
            using DatasetView after = await Dataset.AsOfAsync(p, cancellationToken);

            foreach (Quad quad in commit.Delta.Asserted)
            {
                Check(!before.Contains(in quad), "I2: an asserted quad was already present at " + p);
            }

            foreach (Quad quad in commit.Delta.Retracted)
            {
                Check(before.Contains(in quad), "I2: a retracted quad was absent at " + p);
            }

            HashSet<TermHandle> allocated = [.. commit.Allocations.ToArray().Select(x => x.Handle)];
            IEnumerable<TermHandle> mentioned = commit.Delta.Asserted.ToArray().Concat(commit.Delta.Retracted.ToArray())
                .SelectMany(q => new[] { q.Subject, q.Predicate, q.Object, q.Graph })
                .Concat([commit.Agent, commit.Cause, .. commit.Attachments.ToArray()])
                .Where(h => !h.IsNone);

            foreach (TermHandle handle in mentioned)
            {
                Check(after.TryExternalise(handle, out _), "I3: an id at " + p + " is not in D_P");
                Check(before.TryExternalise(handle, out _) != allocated.Contains(handle), "I3: id " + handle.Value + " at " + p + " was allocated before, or used before its allocation");
            }

            // I5.
            Check(commit.Timestamp.UtcTicks == Model.Timestamps[(int)p], "I5: the timestamp at " + p + " is not max(clock, ts(head))");
            long resolved = Dataset.PositionAt(commit.Timestamp);
            Check(resolved == Model.PositionAt(commit.Timestamp.UtcTicks), "I5: as-of " + commit.Timestamp + " resolves wrongly");
            using DatasetView byTime = await Dataset.AsOfTimestampAsync(commit.Timestamp, cancellationToken);
            Check(byTime.Position == resolved, "I5: as-of by timestamp is not as-of by the resolved position");
            Same(Rendered(Model.History[(int)resolved]), Rendered(byTime), "I5: as-of by timestamp at " + p);
        }

        Check(Dataset.PositionAt(Clock.Now.AddYears(-1)) == 0, "I5: a time before every commit resolves to 0");

        // Reopening rebuilds from the newest checkpoint and the tail: I7 and I8.
        await using (Dataset reopened = await Dataset.OpenAsync(Storage, Options, cancellationToken))
        {
            Check(reopened.Head == head, "a reopened dataset has a different head");
            using DatasetView view = reopened.Pin();
            Same(Rendered(Model.Graph), Rendered(view), "I7/I8: reopened");
            Check(reopened.Checkpoints.SequenceEqual(Dataset.Checkpoints), "a reopened dataset lost a checkpoint");
        }

        // A projection of the contract's own, maintained incrementally, then rebuilt both ways.
        QuadSetProjection incremental = new();
        await Dataset.CatchUpAsync(incremental, cancellationToken);
        Same(Rendered(Model.Graph), [.. incremental.Quads.Select(q => Render(headView, q))], "I8: incremental projection");

        foreach (bool fromCheckpoint in new[] { false, true })
        {
            QuadSetProjection rebuilt = new();
            await Dataset.RebuildAsync(rebuilt, fromCheckpoint, cancellationToken);
            Same(Rendered(Model.Graph), [.. rebuilt.Quads.Select(q => Render(headView, q))], "I8: rebuilt projection, from checkpoint " + fromCheckpoint);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach ((long _, DatasetView view) in _pins)
        {
            view.Dispose();
        }

        await Dataset.DisposeAsync();
    }
}

/// <summary>Records every commit it is given. At-least-once, made exactly-once by position.</summary>
internal sealed class RecordingProjection : IProjection
{
    public List<Commit> Commits { get; } = [];

    public long Position { get; private set; }

    public ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken)
    {
        if (commit.Position > Position)
        {
            Commits.Add(commit);
            Position = commit.Position;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        Commits.Clear();
        Position = checkpoint?.Position ?? 0;
        return ValueTask.CompletedTask;
    }
}

/// <summary>A projection written against the contract alone: the set of quads, by handle.</summary>
internal sealed class QuadSetProjection : IProjection
{
    public HashSet<Quad> Quads { get; } = [];

    public long Position { get; private set; }

    public ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken)
    {
        if (commit.Position <= Position)
        {
            return ValueTask.CompletedTask;
        }

        Quads.ExceptWith(commit.Delta.Retracted.ToArray());
        Quads.UnionWith(commit.Delta.Asserted.ToArray());
        Position = commit.Position;
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        Quads.Clear();
        Position = 0;

        if (checkpoint is not null)
        {
            Quads.UnionWith(T.All(checkpoint));
            Position = checkpoint.Position;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Which cases the generators produced, across every run of a property. A
/// generator that never produces a case makes the property vacuous for it, so
/// the model test fails when any count is zero (ADR 0043).
/// </summary>
internal static class Coverage
{
    public const int RedundantAssert = 0;
    public const int RedundantRetract = 1;
    public const int AssertThenRetractAbsent = 2;
    public const int LabelReused = 3;
    public const int ExistingBlank = 4;
    public const int ExpectedRight = 5;
    public const int Conflict = 6;
    public const int Rejected = 7;
    public const int Attached = 8;
    public const int NoChange = 9;
    public const int Committed = 10;
    public const int ClockBackwards = 11;
    public const int Checkpoint = 12;
    public const int Settings = 13;

    public static readonly string[] Names =
    [
        "redundant assert", "redundant retract", "assert then retract of an absent quad", "blank label reused across requests",
        "existing blank node by handle", "expected position right", "expected position stale (conflict)", "validator reject",
        "validator attachment", "no change", "committed", "clock stepping backwards", "checkpoint", "settings commit",
    ];

    private static readonly long[] Counts = new long[Names.Length];

    public static void Hit(int which) => Interlocked.Increment(ref Counts[which]);

    public static long[] Snapshot() => [.. Counts];
}
