// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store.BrowserTests;

/// <summary>
/// One deterministic history, run on the file backend on the desktop
/// (<c>eng/browser-tests.cs</c>) and on the origin private file system in a
/// browser (this app), whose <c>log/</c> must come out byte-identical
/// (specification §10, ADR 0018's fourth impossibility, ADR 0084).
/// </summary>
/// <remarks>
/// A fixed dataset id, a clock that advances one second per reading, and
/// fixed requests: asserts, retracts, a named graph, commit metadata, a
/// commit split across records, and a segment size small enough that the log
/// seals several segments. It closes the dataset and reopens it halfway, so
/// recovery's path is in the bytes too. The storage is the caller's; the
/// script disposes it.
/// </remarks>
internal static class DeterministicScript
{
    public static DatasetId Id { get; } = new(new Guid("0c6e4d1a-8f2b-4c3d-9e5f-6a7b8c9d0e1f"));

    /// <summary>A clock for the storage's own use (the lease's diagnostic), apart from the dataset's.</summary>
    public static TimeProvider Clock() => new SteppingClock();

    public static async Task RunAsync(IStorage storage, CancellationToken cancellationToken)
    {
        SteppingClock clock = new();

        try
        {
            await using (Dataset dataset = await Dataset.CreateAsync(storage, Id, Options(clock), cancellationToken))
            {
                await CommitAsync(dataset, 0, 24, cancellationToken);
            }

            await using (Dataset dataset = await Dataset.OpenAsync(storage, Options(clock), cancellationToken))
            {
                await CommitAsync(dataset, 24, 40, cancellationToken);
            }
        }
        finally
        {
            if (storage is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    private static DatasetOptions Options(TimeProvider clock) => new()
    {
        Clock = clock,
        SegmentBytes = new ByteCount(2048),
        MaxRecordBytes = new ByteCount(512),
        Maintenance = MaintenanceMode.Off,
    };

    private static async Task CommitAsync(Dataset dataset, int from, int to, CancellationToken cancellationToken)
    {
        for (int i = from; i < to; i++)
        {
            CommitRequest request = new()
            {
                Metadata = new CommitMetadata { Agent = Iri("agent/" + (i % 3)) },
            };

            request.Assert(Iri("s" + i), Iri("p"), Literal("value " + i + " " + new string('x', i % 7 * 20)));

            if (i % 5 == 4)
            {
                request.Retract(Iri("s" + (i - 2)), Iri("p"), Literal("value " + (i - 2) + " " + new string('x', (i - 2) % 7 * 20)));
            }

            if (i % 6 == 0)
            {
                request.Assert(Iri("s" + i), Iri("in"), Iri("g"), Iri("graph/" + (i / 6)));
            }

            if (i == 30)
            {
                // A body above MaxRecordBytes: the commit is several records.
                request.Assert(Iri("big"), Iri("p"), Literal(new string('y', 1500)));
            }

            await dataset.CommitAsync(request, cancellationToken);
        }
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    private static RdfTerm Literal(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));

    /// <summary>One second later at every reading.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private long _seconds;

        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero).AddSeconds(_seconds++);
    }
}
