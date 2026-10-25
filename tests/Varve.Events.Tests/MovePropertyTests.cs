// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CsCheck;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Protocol;
using Varve.Rdf;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>
/// Rendering across the move of a dataset's event declarations to the
/// management plane (ADR 0167), over generated histories that cross a move at
/// a generated point. Every data commit is rendered three ways: from the
/// dataset's history as if nothing moved (the oracle); through the switched
/// path, which takes the declarations from the dataset's catalogue before the
/// move and from the management plane's commits, translated by
/// <see cref="ManagedEventDeclarations"/>, from the move's position on, by the
/// management position each commit records; and through the switched path
/// replayed again. About half the histories also cross a freeze and its
/// cancel before the move, between which a catalogue write is refused, and
/// after which it is accepted again. #93's two rendering properties are re-run
/// over the switched path.
/// </summary>
public sealed class MovePropertyTests
{
    private static readonly EventEndpoints Endpoints = new("https://varve.example.org/", "d");

    /// <summary>
    /// A history, the step it moves at, and in about half the samples a freeze
    /// and a cancel before it (ADR 0167): a catalogue write between the two is
    /// refused, and one after the cancel accepted again.
    /// </summary>
    private static readonly Gen<(List<Generators.Step> Steps, int Move, int Freeze, int Cancel)> Moves =
        Generators.Histories.SelectMany(steps => Gen.Int[0, steps.Count].SelectMany(move =>
            Gen.Select(Gen.Bool, Gen.Int[0, move], Gen.Int[0, move], (cancelled, a, b) => cancelled
                ? (steps, move, Math.Min(a, b), Math.Max(a, b))
                : (steps, move, -1, -1))));

    [Fact]
    public void rendering_is_byte_identical_across_the_move() => Property.Run(() =>
        Moves.Sample(sample =>
        {
            Crossing crossing = Crossing.Play(sample.Steps, sample.Move, sample.Freeze, sample.Cancel);
            List<RenderedEvent> oracle = crossing.Oracle.RenderAll(Endpoints, crossing.Oracle.Head);
            List<RenderedEvent> switched = crossing.RenderSwitched(crossing.Switched.Head);
            List<RenderedEvent> again = Crossing.Play(sample.Steps, sample.Move, sample.Freeze, sample.Cancel).RenderSwitched(crossing.Switched.Head);

            Assert.Equal(oracle.Count, switched.Count);
            Assert.Equal(switched.Count, again.Count);

            for (int i = 0; i < oracle.Count; i++)
            {
                Assert.True(switched[i].Json.Span.SequenceEqual(again[i].Json.Span), "replayed: " + Json.Text(switched[i].Json));
                Assert.Equal(oracle[i].Position, switched[i].Position);

                if (!crossing.ChangedAfterTheMove(oracle[i]))
                {
                    // Declarations unchanged since the move: the same bytes, dataschema included.
                    Assert.True(oracle[i].Json.Span.SequenceEqual(switched[i].Json.Span),
                        "at " + oracle[i].Position.Value + ", move at step " + sample.Move + ":\n" + Json.Text(oracle[i].Json) + "\n" + Json.Text(switched[i].Json));
                    continue;
                }

                // A declaration changed on the management plane: the same event, its
                // schema the revision the oracle's, or one minted there (m{p}).
                JsonObject expected = Json.Node(oracle[i].Json).AsObject();
                JsonObject actual = Json.Node(switched[i].Json).AsObject();
                string? schema = actual["dataschema"]?.GetValue<string>();
                Assert.True(schema is null || schema == expected["dataschema"]?.GetValue<string>() || schema.Contains("/revisions/m", StringComparison.Ordinal), schema);
                expected.Remove("dataschema");
                actual.Remove("dataschema");
                Assert.True(JsonNode.DeepEquals(expected, actual), expected.ToJsonString() + "\n" + actual.ToJsonString());
            }

            return true;
        }, iter: PropertyTests.Histories));

    [Fact]
    public void the_switched_path_renders_every_commit_twice_with_identical_bytes() => Property.Run(() =>
        Moves.Sample(sample =>
        {
            Crossing crossing = Crossing.Play(sample.Steps, sample.Move, sample.Freeze, sample.Cancel);
            List<RenderedEvent> first = crossing.RenderSwitched(crossing.Switched.Head);
            List<RenderedEvent> second = crossing.RenderSwitched(crossing.Switched.Head);
            return first.Count == second.Count && first.Zip(second).All(p => p.First.Json.Span.SequenceEqual(p.Second.Json.Span));
        }, iter: PropertyTests.Histories));

    [Fact]
    public void the_switched_path_renders_a_commit_with_the_declaration_in_force_at_its_position() => Property.Run(() =>
        Moves.Sample(sample =>
        {
            Crossing crossing = Crossing.Play(sample.Steps, sample.Move, sample.Freeze, sample.Cancel);
            List<RenderedEvent> late = crossing.RenderSwitched(crossing.Switched.Head);

            for (long p = 1; p <= crossing.Switched.Head.Value; p++)
            {
                // The dataset's history replayed only to p, and the management
                // plane's only to what p records: the commit at p renders the same.
                List<RenderedEvent> early = [.. crossing.RenderSwitched(new FeedPosition(p)).Where(e => e.Position.Value == p)];
                List<RenderedEvent> atP = [.. late.Where(e => e.Position.Value == p)];

                if (early.Count != atP.Count || !early.Zip(atP).All(x => x.First.Json.Span.SequenceEqual(x.Second.Json.Span)))
                {
                    return false;
                }
            }

            return true;
        }, iter: PropertyTests.Histories / 4));

    [Fact]
    public void the_copy_translates_a_catalogue_one_to_one_and_back() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);
            List<FeedChange> catalogue = [.. history.Commits.SelectMany(c => c.Changes).Aggregate(new HashSet<Q>(), (state, change) =>
            {
                Q q = new(change.Subject, change.Predicate, change.Object, change.Graph);
                _ = change.Kind == FeedChangeKind.Assert ? state.Add(q) : state.Remove(q);
                return state;
            }).Where(q => q.G is { } g && EventCatalogue.IsUnderNamespace(g)).Select(q => new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G))];

            if (catalogue.Count == 0)
            {
                return true;
            }

            Assert.True(ManagedEventDeclarations.TryFromCatalogue(catalogue, out List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> nodes, out DeclarationProblem? problem), problem?.Detail);
            List<DeclarationProblem> problems = [];
            List<FeedChange> back = ManagedEventDeclarations.ToCatalogue(nodes, problems);
            Assert.Empty(problems);
            Assert.Empty(ManagedEventDeclarations.Changes(catalogue, back));
            return true;
        }, iter: PropertyTests.Histories));

    /// <summary>
    /// Two worlds of one generated history: the oracle, where every
    /// declaration stays in the catalogue graph, and the switched one, where
    /// those after the move are management commits and the dataset commits a
    /// stand-in instead, so that the positions stay the same.
    /// </summary>
    private sealed class Crossing
    {
        private readonly Dictionary<long, long> _recorded = [];
        private readonly List<(long Position, List<FeedChange> Catalogue)> _managed = [];
        private readonly Dictionary<string, long> _changedFrom = new(StringComparer.Ordinal);

        internal History Oracle { get; } = new();

        internal History Switched { get; } = new();

        /// <summary>The management position the switch was committed at: m₂.</summary>
        internal long MovedAt { get; private set; } = long.MaxValue;

        internal static Crossing Play(List<Generators.Step> steps, int move, int freeze = -1, int cancel = -1)
        {
            Crossing crossing = new();
            Dictionary<int, List<Q>> declared = [];
            long management = 1;
            int events = 0;
            int stand = 0;

            for (int i = 0; i <= steps.Count; i++)
            {
                // A freeze at m1, then its cancel, a commit of its own at m3.
                if (i == freeze)
                {
                    management++;
                }

                if (i == cancel)
                {
                    management++;
                }

                if (i == move)
                {
                    // The freeze at m1, the switch at m2: the catalogue as it is now.
                    management += 2;
                    crossing.MovedAt = management;
                    crossing._managed.Add((management, Catalogue(declared)));
                }

                if (i == steps.Count)
                {
                    break;
                }

                FeedPosition? committed = null;

                switch (steps[i])
                {
                    case Generators.Declare when i >= freeze && i < cancel:
                        // Authorised while frozen: refused, in every world.
                        break;
                    case Generators.Declare d:
                        List<Q> quads = Generators.TypeOf(d.Index, d.Fields, d.Version, d.Retired).Quads(Terms.EventsGraph);
                        List<Q> old = declared.TryGetValue(d.Index, out List<Q>? was) ? was : [];
                        committed = crossing.Oracle.Commit(old, quads);
                        declared[d.Index] = quads;

                        if (committed is null)
                        {
                            break;
                        }

                        if (i < move)
                        {
                            crossing.Switched.Commit(old, quads);
                        }
                        else
                        {
                            // On the management plane now; the dataset commits something else at this position.
                            crossing.Switched.Commit([], [new Q(Terms.Iri("https://example.org/stand-in/" + stand++), Terms.Iri(Terms.Ex + "standIn"), Terms.Plain("x"), null)]);
                            management++;
                            crossing._managed.Add((management, Catalogue(declared)));
                            crossing._changedFrom.TryAdd("T" + d.Index, committed.Value.Value);
                        }

                        break;
                    case Generators.Emit e:
                        RdfTerm node = Terms.Iri("https://example.org/e/" + events++);
                        List<Q> assert = [new Q(node, Terms.Iri(Terms.RdfType), Terms.Iri(Terms.Ex + "C" + e.Index), Terms.EventsGraph)];
                        assert.AddRange(e.Values.Select(v => new Q(node, Terms.Iri(Terms.Ex + v.Field), v.Value, Terms.EventsGraph)));
                        committed = crossing.Oracle.Commit([], assert);
                        Assert.Equal(committed, crossing.Switched.Commit([], assert));
                        break;
                    case Generators.Noise n:
                        List<Q> noise = [new Q(Terms.Iri("https://example.org/n/" + n.N), Terms.Iri(Terms.Ex + "noise"), Terms.Plain("x"), null)];
                        committed = crossing.Oracle.Commit([], noise);
                        Assert.Equal(committed, crossing.Switched.Commit([], noise));
                        break;
                }

                if (committed is { } position)
                {
                    // The position the commit was authorised at, which it records (ADR 0165).
                    crossing._recorded[position.Value] = management;
                }
            }

            return crossing;
        }

        internal bool ChangedAfterTheMove(RenderedEvent rendered) =>
            rendered.IsProblem
                ? _changedFrom.Values.Any(from => from <= rendered.Position.Value)
                : _changedFrom.TryGetValue(rendered.Type, out long from) && from <= rendered.Position.Value;

        /// <summary>Renders every commit up to <paramref name="upTo"/> through the switched path, each history replayed only as far as that.</summary>
        internal List<RenderedEvent> RenderSwitched(FeedPosition upTo)
        {
            EventCatalogueHistory before = Switched.Catalogue(upTo);
            long recordedUpTo = upTo.Value == 0 ? 0 : _recorded[upTo.Value];
            EventCatalogueHistory? after = null;
            List<FeedChange> previous = [];

            foreach ((long position, List<FeedChange> catalogue) in _managed)
            {
                if (position > recordedUpTo)
                {
                    break;
                }

                // What the management plane holds: the copy, then each change, in the dataset's graph's form.
                Assert.True(ManagedEventDeclarations.TryFromCatalogue(catalogue, out List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> graph, out DeclarationProblem? problem) || catalogue.Count == 0, problem?.Detail);
                List<FeedChange> now = ManagedEventDeclarations.ToCatalogue(graph, []);
                after ??= EventCatalogueHistory.ContinueFrom(before.Current);
                after.Apply(new FeedPosition(position), ManagedEventDeclarations.Changes(previous, now));
                previous = now;
            }

            EventRenderer renderer = new();
            List<RenderedEvent> events = [];

            foreach ((FeedPosition position, FeedTimestamp timestamp, List<FeedChange> changes) in Switched.Commits)
            {
                if (position > upTo)
                {
                    break;
                }

                long recorded = _recorded[position.Value];
                EventCommit commit = History.ToCommit(position, timestamp, changes);
                EventCatalogueSnapshot snapshot = after is not null && recorded >= MovedAt ? after.At(new FeedPosition(recorded)) : before.At(position);

                if (EventRenderer.HasEvents(commit, snapshot))
                {
                    events.AddRange(renderer.Render(commit, snapshot, Switched.At(position), Endpoints));
                }
            }

            return events;
        }

        private static List<FeedChange> Catalogue(Dictionary<int, List<Q>> declared) =>
            [.. declared.Values.SelectMany(quads => quads).Select(q => new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G))];
    }
}
