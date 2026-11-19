// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CsCheck;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The move of a dataset's event declarations (ADR 0167) against a model of
/// its states, over generated interleavings (#254). One server per schedule,
/// a fleet of one, with its interval loop's timer idle. Two actors run the
/// move's code and are held between its steps by signals, never by time:
/// <list type="bullet">
/// <item><b>L</b>, one round of the interval loop (<see cref="Fleet.TickAsync"/>),
/// which picks a dataset that is <c>Moving</c> and resumes it;</item>
/// <item><b>M</b>, a move request, <c>POST /manage/datasets/d/events/move</c>.</item>
/// </list>
/// Each pauses wherever the move's code advances the management plane, which
/// it does at every step (<see cref="Fleet.Advancing"/>). Between their steps
/// the schedule does, atomically: a freeze by apply, a cancel by apply, a
/// catalogue write, and switching the system dataset's refusal of every
/// write on or off, so that the move's own writes fail. The catalogue
/// translates one-to-one or does not, by schedule.
/// <para>
/// A move is <i>started</i> by a move request or an acknowledged freeze, and
/// <i>cancelled</i> by an acknowledged cancel: a cancel by apply answered
/// <c>204</c>, or a move answered "cancelled" (<c>422</c>) or
/// <c>409 event-move-cancelled</c>. A cancel acknowledged while a move request
/// is in flight does not close the move: that request is concurrent with it,
/// and may start a move after it. A move is <i>in progress</i> from its start
/// until a cancel, or while a move request is in flight. After every action:
/// </para>
/// <list type="number">
/// <item>the declarations are never <c>moved</c> after an acknowledged cancel,
/// unless a move was started since;</item>
/// <item>with no move in progress, they are <c>in-dataset</c>, and a catalogue
/// write is accepted: the catalogue is never frozen;</item>
/// <item>an answer that says the move was cancelled holds: the declarations
/// are <c>in-dataset</c> when it is given.</item>
/// </list>
/// </summary>
public sealed class EventMoveModelTests
{
    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Catalogue = Varve + "events-catalogue/default";

    // The count of schedules; VARVE_EVENT_MOVE_SCHEDULES raises it (docs/testing.md).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_EVENT_MOVE_SCHEDULES"), out int n) ? n : 40;

    // How long one step may take before the schedule is called stuck: a bound on a hang, never a timing the test relies on.
    private static readonly TimeSpan Stuck = TimeSpan.FromSeconds(60);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The kinds of step a schedule draws.
    private enum Kind
    {
        Freeze,
        Cancel,
        StartMove,
        StepMove,
        StartTick,
        StepTick,
        CatalogueWrite,
        Refusal,
    }

    [Fact]
    public async Task Over_generated_interleavings_a_cancel_stays_cancelled_and_a_cancelled_answer_holds()
    {
        Gen<(bool Translates, (int Kind, int Arg)[] Steps)> schedules = Gen.Select(Gen.Bool, Gen.Select(Gen.Int[0, 7], Gen.Int[0, 3]).Array[4, 24]);
        await schedules.SampleAsync(
            schedule => RunAsync(schedule.Translates, schedule.Steps),
            iter: Schedules,
            threads: 1,
            print: schedule => (schedule.Translates ? "translates: " : "does not translate: ") + string.Join(" ", schedule.Steps.Select(step => (Kind)step.Kind + (step.Kind == (int)Kind.Refusal ? (step.Arg % 2 == 0 ? "(on)" : "(off)") : ""))));
    }

    private static async Task RunAsync(bool translates, (int Kind, int Arg)[] steps)
    {
        string root = Directory.CreateTempSubdirectory("varve-move-model-").FullName;

        try
        {
            await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
            {
                ["Varve:Auth:Mode"] = "Anonymous",
                ["Varve:DatasetsRoot"] = root,
                ["Varve:Fleet:FollowInterval"] = "00:01:00",
                ["Varve:Events:Source"] = "https://varve.example.org/",
            });
            Model model = new(server, translates);
            await model.SeedAsync();

            foreach ((int kind, int arg) in steps)
            {
                await model.DoAsync((Kind)kind, arg);
                await model.CheckAsync();
            }

            // The end: no refusal, and every actor run out, then the same checks.
            await model.DoAsync(Kind.Refusal, 1);
            await model.FinishAsync();
            await model.CheckAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The schedule's state: the server, its two actors, and what the model knows from the answers.</summary>
    private sealed class Model
    {
        private readonly RunningServer _server;
        private readonly Fleet _fleet;
        private readonly bool _translates;
        private readonly Actor _tick = new("L");
        private readonly Actor _move = new("M");
        private readonly AsyncLocal<bool> _inTick = new();
        private bool _refusing;
        private int _writes;

        // The model: a move started and not cancelled since; and a cancel acknowledged with no start since.
        private bool _started;
        private bool _cancelledSinceStart;

        internal Model(RunningServer server, bool translates)
        {
            _server = server;
            _fleet = server.Started!.Fleet;
            _translates = translates;
            _fleet.Advancing = async cancellationToken =>
            {
                Actor? actor = _inTick.Value ? _tick : IsMoveRequest() ? _move : null;

                if (actor is not null)
                {
                    await actor.PauseAsync(cancellationToken);
                }
            };
            _fleet.RefusingManagementWrites = () => _refusing;
        }

        private bool InProgress => (_started && !_cancelledSinceStart) || _move.InFlight;

        internal async Task SeedAsync()
        {
            HttpResponseMessage declared = await PutAsync(Declaration("d"));
            Assert.True(declared.IsSuccessStatusCode, await declared.Content.ReadAsStringAsync(Ct));
            await UpdateOkAsync(EventMoveDeclaration);

            if (!_translates)
            {
                await UpdateOkAsync(UntranslatableRefund);
            }
        }

        internal async Task DoAsync(Kind kind, int arg)
        {
            switch (kind)
            {
                case Kind.Freeze when !_move.InFlight && await StateAsync() == "in-dataset":
                    if ((await PutAsync(WithState(await DeclaredAsync(), "Moving"))).StatusCode == HttpStatusCode.NoContent)
                    {
                        Started();
                    }

                    break;
                case Kind.Cancel when await StateAsync() == "moving":
                    if ((await PutAsync(WithState(await DeclaredAsync(), "InDataset"))).StatusCode == HttpStatusCode.NoContent)
                    {
                        await CancelledAsync("a cancel by apply answered 204");
                    }

                    break;
                case Kind.StartMove when !_move.InFlight:
                    Started();
                    await _move.StartAsync(async () => await _server.Client.PostAsync(new Uri("manage/datasets/d/events/move", UriKind.Relative), null, Ct));
                    await AnsweredAsync();
                    break;
                case Kind.StepMove when _move.InFlight:
                    await _move.StepAsync();
                    await AnsweredAsync();
                    break;
                case Kind.StartTick when !_tick.InFlight:
                    await _tick.StartAsync(async () =>
                    {
                        _inTick.Value = true;
                        await _fleet.TickAsync(Ct);
                        return null;
                    });
                    break;
                case Kind.StepTick when _tick.InFlight:
                    await _tick.StepAsync();
                    break;
                case Kind.CatalogueWrite:
                    HttpResponseMessage written = await UpdateAsync(
                        "DELETE { GRAPH <" + Catalogue + "> { <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> ?c } } "
                        + "INSERT { GRAPH <" + Catalogue + "> { <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> \"write " + (++_writes) + "\" } } "
                        + "WHERE { OPTIONAL { GRAPH <" + Catalogue + "> { <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> ?c } } }");

                    if (!InProgress)
                    {
                        Assert.True(written.StatusCode == HttpStatusCode.NoContent, "Invariant 2: with no move in progress, a catalogue write was refused: " + await written.Content.ReadAsStringAsync(Ct));
                    }

                    break;
                case Kind.Refusal:
                    _refusing = arg % 2 == 0;
                    break;
                default:
                    break;
            }
        }

        /// <summary>Runs every actor to its end.</summary>
        internal async Task FinishAsync()
        {
            for (int i = 0; i < 64 && (_tick.InFlight || _move.InFlight); i++)
            {
                if (_move.InFlight)
                {
                    await _move.StepAsync();
                    await AnsweredAsync();
                }

                if (_tick.InFlight)
                {
                    await _tick.StepAsync();
                }

                await CheckAsync();
            }

            Assert.False(_tick.InFlight || _move.InFlight, "An actor did not finish in 64 steps.");
        }

        internal async Task CheckAsync()
        {
            string state = await StateAsync();

            // Invariant 1.
            Assert.False(_cancelledSinceStart && state == "moved", "Invariant 1: the declarations moved after a cancel was acknowledged, with no move started since.");

            // Invariant 2, as a state: no move in progress, and the catalogue is not frozen.
            if (!InProgress)
            {
                Assert.True(state == "in-dataset", "Invariant 2: with no move in progress, the declarations are " + state + ".");
            }
        }

        // A move request's answer, once it has one.
        private async Task AnsweredAsync()
        {
            if (_move.InFlight || _move.TakeAnswer() is not { } answer)
            {
                return;
            }

            string body = await answer.Content.ReadAsStringAsync(Ct);

            if (answer.StatusCode == HttpStatusCode.UnprocessableEntity && body.Contains("cancelled", StringComparison.Ordinal))
            {
                await CancelledAsync("the move answered 422 cancelled");
            }
            else if (answer.StatusCode == HttpStatusCode.Conflict && body.Contains("event-move-cancelled", StringComparison.Ordinal))
            {
                await CancelledAsync("the move answered 409 event-move-cancelled");
            }
            else if (answer.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("moved", await StateAsync());
            }
        }

        private void Started()
        {
            _started = true;
            _cancelledSinceStart = false;
        }

        // Invariant 3: an answer that says cancelled holds when it is given.
        private async Task CancelledAsync(string what)
        {
            string state = await StateAsync();
            Assert.True(state == "in-dataset", "Invariant 3: " + what + ", and the declarations are " + state + ".");

            // A move request in flight now is concurrent with the cancel, and
            // may still start a move after it: the cancel closes the move only
            // when none is.
            _cancelledSinceStart = !_move.InFlight;
        }

        private async Task<string> StateAsync() =>
            JsonDocument.Parse(await (await _server.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("state").GetString()!;

        private async Task<string> DeclaredAsync() =>
            await (await _server.Client.GetAsync(new Uri("manage/datasets/d", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct);

        private Task<HttpResponseMessage> PutAsync(string trig) =>
            _server.Client.PutAsync(new Uri("manage/datasets/d", UriKind.Relative), new StringContent(trig, Encoding.UTF8, "application/trig"), Ct);

        private Task<HttpResponseMessage> UpdateAsync(string update) =>
            _server.Client.PostAsync(new Uri("datasets/d/sparql", UriKind.Relative), new StringContent(update, Encoding.UTF8, "application/sparql-update"), Ct);

        private async Task UpdateOkAsync(string update)
        {
            HttpResponseMessage response = await UpdateAsync(update);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        }
    }

    // The dataset's declarations with its event declarations in `state`, from the TriG the server answers.
    private static string WithState(string trig, string state)
    {
        foreach (string from in new[] { "Moving", "InDataset" })
        {
            trig = trig.Replace("<" + Varve + from + ">", "<" + Varve + state + ">", StringComparison.Ordinal).Replace("varve:" + from, "varve:" + state, StringComparison.Ordinal);
        }

        return trig.Contains(Varve + state, StringComparison.Ordinal) || trig.Contains("varve:" + state, StringComparison.Ordinal)
            ? trig
            : trig.TrimEnd().TrimEnd('}') + " <urn:fleet:d> <" + Varve + "eventDeclarations> <" + Varve + state + "> . }";
    }

    // Whether the advance being taken is a move request's.
    private static bool IsMoveRequest() =>
        new Microsoft.AspNetCore.Http.HttpContextAccessor().HttpContext?.Request.Path.Value?.EndsWith("/events/move", StringComparison.Ordinal) == true;

    private static string Declaration(string name) =>
        ManagementTests.Declaration(name, "File").Replace("urn:varve:from-config:" + name, "urn:fleet:" + name, StringComparison.Ordinal);

    private const string EventMoveDeclaration =
        "INSERT DATA { GRAPH <" + Catalogue + "> { "
        + "<https://example.org/types/Paid> a <" + Varve + "EventType> ; <" + Varve + "eventName> \"Paid\" ; "
        + "<" + Varve + "eventClass> <https://example.org/ns#Paid> ; <" + Varve + "eventVersion> 1 ; "
        + "<" + Varve + "eventField> <https://example.org/types/Paid/total> ; "
        + "<" + Varve + "eventSelect> \"SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }\" . "
        + "<https://example.org/types/Paid/total> a <" + Varve + "EventField> ; <" + Varve + "fieldName> \"total\" ; "
        + "<" + Varve + "fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> ; <" + Varve + "fieldRequired> true } }";

    // Paid's field node in a second catalogue graph too: no one node of the dataset's graph, so the move cancels itself.
    private const string UntranslatableRefund =
        "INSERT DATA { GRAPH <" + Varve + "events-catalogue/https%3A%2F%2Fexample.org%2Fother> { "
        + "<https://example.org/types/Refund> a <" + Varve + "EventType> ; <" + Varve + "eventName> \"Refund\" ; "
        + "<" + Varve + "eventClass> <https://example.org/ns#Refund> ; <" + Varve + "eventVersion> 1 ; "
        + "<" + Varve + "eventField> <https://example.org/types/Paid/total> ; "
        + "<" + Varve + "eventSelect> \"SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }\" . "
        + "<https://example.org/types/Paid/total> a <" + Varve + "EventField> ; <" + Varve + "fieldName> \"total\" ; "
        + "<" + Varve + "fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> ; <" + Varve + "fieldRequired> true } }";

    /// <summary>
    /// One actor of a schedule: a task that runs the move's code, held at each
    /// pause until the schedule steps it, and its answer once it ends.
    /// </summary>
    private sealed class Actor(string name)
    {
        private readonly Channel<bool> _paused = Channel.CreateUnbounded<bool>();
        private readonly Channel<bool> _go = Channel.CreateUnbounded<bool>();
        private Task<HttpResponseMessage?>? _run;

        internal bool InFlight => _run is { IsCompleted: false };

        // Called on the actor's own flow, where the move's code advances.
        internal async Task PauseAsync(CancellationToken cancellationToken)
        {
            _paused.Writer.TryWrite(true);
            await _go.Reader.ReadAsync(cancellationToken);
        }

        internal async Task StartAsync(Func<Task<HttpResponseMessage?>> body)
        {
            _run = Task.Run(body, Ct);
            await NextAsync();
        }

        internal async Task StepAsync()
        {
            _go.Writer.TryWrite(true);
            await NextAsync();
        }

        // The answer of an actor that has ended, once.
        internal HttpResponseMessage? TakeAnswer()
        {
            if (_run is not { IsCompleted: true } run)
            {
                return null;
            }

            _run = null;
            return run.GetAwaiter().GetResult();
        }

        // Waits until the actor pauses again or ends.
        private async Task NextAsync()
        {
            Task<bool> paused = _paused.Reader.WaitToReadAsync(Ct).AsTask();
            Task ended = await Task.WhenAny(paused, _run!).WaitAsync(Stuck, Ct);

            if (ended == paused)
            {
                _paused.Reader.TryRead(out _);
                return;
            }

            // Ended: an exception is the test's failure.
            await _run!;
        }

        public override string ToString() => name;
    }
}
