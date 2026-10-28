// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes a dataset with derived format 3, as v0.1.0-preview.6 writes it:
// a seeded history with retractions in three graphs, full checkpoints, and
// the projection's runs on disk.
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

string root = args[0];
int commits = int.Parse(args[1], CultureInfo.InvariantCulture);
int every = int.Parse(args[2], CultureInfo.InvariantCulture);
Clock clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
DatasetId id = new(new Guid("6a0e7b3c-1d2f-4a5b-8c9d-0e1f2a3b4c5d"));
static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));
static RdfTerm Literal(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));

await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(root), new FileStorageOptions { Clock = clock });
DatasetOptions options = new() { Clock = clock, Maintenance = MaintenanceMode.Off, MemtableLimit = new QuadCount(64) };
await using Dataset dataset = await Dataset.CreateAsync(storage, id, options);

for (int i = 1; i <= commits; i++)
{
    clock.Now = clock.Now.AddSeconds(7);
    string n = i.ToString(CultureInfo.InvariantCulture);
    CommitRequestBuilder request = new CommitRequestBuilder()
        .Assert(Iri("s" + (i % 17)), Iri("p" + (i % 3)), Literal("v" + n), Iri("g" + (i % 3)))
        .Assert(Iri("s" + (i % 11)), Iri("q"), Iri("o" + (i % 5)));

    if (i > 4)
    {
        string m = (i - 4).ToString(CultureInfo.InvariantCulture);
        request.Retract(Iri("s" + ((i - 4) % 17)), Iri("p" + ((i - 4) % 3)), Literal("v" + m), Iri("g" + ((i - 4) % 3)));
    }

    await dataset.CommitAsync(request);
    await dataset.MaintainAsync();

    if (i % every == 0)
    {
        await dataset.CheckpointAsync(new Position(i));
    }
}

Console.WriteLine("head " + dataset.Head.Value + ", checkpoints " + string.Join(",", dataset.Checkpoints.Select(p => p.Value)));

sealed class Clock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}
