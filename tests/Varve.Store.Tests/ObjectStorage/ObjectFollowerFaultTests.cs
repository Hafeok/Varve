// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Tier 1 under failure injection (#97's gate; Replication part 1, #165): a
/// follower beside its writer, over generated schedules, equals the
/// reference model at every position it reports. The schedules hold:
/// - commits, seals and consolidations;
/// - takeovers by a new writer, after a graceful close or a crash;
/// - re-opens of the follower;
/// - advances whose reads the store fails, before it applies them or after,
///   at any one of them.
///
/// The reference model is the quad set at each position, kept from what each
/// acknowledged commit asserted and retracted, never read from the store.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Faults)]
public sealed class ObjectFollowerFaultTests
{
    // The land gate's count; VARVE_FOLLOWER_FAULT_SCHEDULES raises it for
    // the nightly run (docs/testing.md §8).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_FOLLOWER_FAULT_SCHEDULES"), out int n) ? n : 60;

    private static DatasetOptions Small() => T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var options
        ? new DatasetOptions { Clock = options.Clock, MaxRecordBytes = options.MaxRecordBytes, SegmentBytes = options.SegmentBytes, CommitCache = options.CommitCache, Maintenance = MaintenanceMode.Off }
        : throw new InvalidOperationException();

    [Fact]
    public async Task a_follower_under_faults_equals_the_reference_model_at_every_position_it_reports()
    {
        Gen<(int Kind, int Subject, bool Assert)[]> schedules = Gen.Select(Gen.Int[0, 9], Gen.Int[0, 7], Gen.Bool).Array[1, 60];

        await schedules.SampleAsync(
            RunAsync,
            iter: Schedules,
            print: schedule => string.Join(" ", schedule.Select(step => step.Kind + ":" + step.Subject + (step.Assert ? "+" : "-"))));
    }

    /// <summary>
    /// A schedule the property shrank to, replayed as it was found (#165).
    /// An advance whose scan read the log and lost its answer, after a
    /// refresh had adopted the commits, left them held and not applied.
    /// The next advance's refresh found nothing new, so the follower stayed a
    /// commit behind until the writer appended again.
    /// </summary>
    [Fact]
    public async Task a_follower_applies_what_a_failed_advance_adopted_without_a_further_write() =>
        await RunAsync(Parse("5:2- 3:2+ 5:3- 5:3- 7:3- 8:1+ 1:2+ 0:7+ 2:1- 3:4- 0:7- 2:7+ 4:7- 3:2- 4:3- 5:2- 3:4- 2:5+ 6:2+ 4:7+ 1:5+ 7:0+ 7:7+"));

    private static (int Kind, int Subject, bool Assert)[] Parse(string schedule) =>
        [.. schedule.Split(' ').Select(step => (int.Parse(step[..step.IndexOf(':', StringComparison.Ordinal)], CultureInfo.InvariantCulture), int.Parse(step[(step.IndexOf(':', StringComparison.Ordinal) + 1)..^1], CultureInfo.InvariantCulture), step[^1] == '+'))];

    // One schedule: the writer, the follower, the store's faults, and the model.
    private static async Task RunAsync((int Kind, int Subject, bool Assert)[] schedule)
    {
        MemoryObjectStore store = new();
        (int Countdown, bool After)? fault = null;

        // One fault at the countdown's read; reads only, since the
        // follower makes no other request (ADR 0164 point 3).
        store.Hook = (operation, _) =>
        {
            if (operation != ObjectOperation.Read || fault is not { } armed)
            {
                return null;
            }

            if (armed.Countdown > 0)
            {
                fault = (armed.Countdown - 1, armed.After);
                return null;
            }

            fault = null;
            return armed.After ? () => throw new IOException("injected: the read's answer was lost") : throw new IOException("injected: the store refused the read");
        };

        await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        List<SortedSet<string>> model = [[]];
        SortedSet<string> current = [];
        Backend written = await objects.OpenAsync();
        Dataset writer = await T.OpenOrCreate(written, Small());
        List<Dataset> abandoned = [];
        Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);

        try
        {
            foreach ((int kind, int subject, bool assert) in schedule)
            {
                switch (kind)
                {
                    case 4:
                        await written.MaintainStorageAsync(T.Ct);
                        break;
                    case 5:
                        await follower.AdvanceAsync(T.Ct);
                        await SameAsModelAsync(follower, model);
                        break;
                    case 6 or 7:
                        fault = (subject, kind == 7);

                        try
                        {
                            await follower.AdvanceAsync(T.Ct);
                        }
                        catch (IOException)
                        {
                            // The read is served at what was applied (ADR 0164).
                        }

                        fault = null;
                        await SameAsModelAsync(follower, model);
                        break;
                    case 8:
                        // A new writer claims the next epoch and fences, after
                        // the old one closed, or crashed without closing.
                        if (assert)
                        {
                            await writer.DisposeAsync();
                        }
                        else
                        {
                            abandoned.Add(writer);
                        }

                        written = await objects.OpenAsync();
                        writer = await Dataset.OpenAsync(written, Small(), T.Ct);
                        Assert.Equal(model.Count - 1, writer.Head.Value);
                        break;
                    case 9:
                        await follower.DisposeAsync();
                        follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);
                        await SameAsModelAsync(follower, model);
                        break;
                    default:
                        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) quad = (T.Iri("s" + subject), T.Iri("p"), T.Literal(new string('o', 40 + subject)), T.Iri("g" + (subject % 3)));
                        CommitRequestBuilder request = assert ? new CommitRequestBuilder().Assert(quad.S, quad.P, quad.O, quad.G) : new CommitRequestBuilder().Retract(quad.S, quad.P, quad.O, quad.G);
                        CommitResult result = await writer.CommitAsync(request, T.Ct);

                        if (result.Outcome == CommitOutcome.Committed)
                        {
                            string rendered = Rendered(quad);
                            current = [.. current];

                            if (assert)
                            {
                                current.Add(rendered);
                            }
                            else
                            {
                                current.Remove(rendered);
                            }

                            model.Add(current);
                            Assert.Equal(model.Count - 1, result.Position.Value);
                        }

                        break;
                }
            }

            // Faults off: the follower reaches the writer's head, and every position below it is the model's.
            await follower.AdvanceAsync(T.Ct);
            Assert.Equal(model.Count - 1, follower.Head.Value);
            await SameAsModelAsync(follower, model);
        }
        finally
        {
            await follower.DisposeAsync();
            await writer.DisposeAsync();

            foreach (Dataset crashed in abandoned)
            {
                await crashed.DisposeAsync();
            }
        }
    }

    // At its reported position and every one before it, the follower holds
    // what the model says, and never a position the model has not reached.
    private static async Task SameAsModelAsync(Dataset follower, List<SortedSet<string>> model)
    {
        long head = follower.Head.Value;
        Assert.InRange(head, 0, model.Count - 1);

        for (long p = 0; p <= head; p++)
        {
            using DatasetView view = await follower.AsOfAsync(new Position(p), T.Ct);
            Assert.Equal(model[(int)p], T.Terms(view));
        }
    }

    private static string Rendered((RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) quad) =>
        T.Render(quad.S) + " " + T.Render(quad.P) + " " + T.Render(quad.O) + " " + T.Render(quad.G);
}
