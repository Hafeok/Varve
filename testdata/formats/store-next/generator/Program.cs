// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Writes a dataset as the release after v0.1.0-preview.15 writes it, from this
// branch's src/Varve.Store, since no release has written it yet: log format 1
// with inline set 1, and derived format 6 (ADR 0213). A seeded history with retractions in three graphs, full
// checkpoints, and the projection's runs on disk (docs/formats/, ADR 0216).
// Beside it, expectations/operations.tsv: every operation the history issued,
// one line each, "position<TAB>+|-<TAB>quad", the quad as N-Quads; the
// dataset at a position is the set those operations leave.
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
static string Quad(string s, string p, string o, string? g) =>
    "<http://example.org/" + s + "> <http://example.org/" + p + "> " + o + (g is null ? "" : " <http://example.org/" + g + ">") + " .";
StringBuilder operations = new();

await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(Path.Combine(root, "dataset")), new FileStorageOptions { Clock = clock }))
{
    DatasetOptions options = new() { Clock = clock, Maintenance = MaintenanceMode.Off, MemtableLimit = new QuadCount(64) };
    await using Dataset dataset = await Dataset.CreateAsync(storage, id, options);

    for (int i = 1; i <= commits; i++)
    {
        clock.Now = clock.Now.AddSeconds(7);
        string n = i.ToString(CultureInfo.InvariantCulture);
        string a = (i % 17).ToString(CultureInfo.InvariantCulture), b = (i % 3).ToString(CultureInfo.InvariantCulture);
        string c = (i % 11).ToString(CultureInfo.InvariantCulture), d = (i % 5).ToString(CultureInfo.InvariantCulture);
        CommitRequestBuilder request = new CommitRequestBuilder()
            .Assert(Iri("s" + a), Iri("p" + b), Literal("v" + n), Iri("g" + b))
            .Assert(Iri("s" + c), Iri("q"), Iri("o" + d));
        operations.Append(n).Append("\t+\t").Append(Quad("s" + a, "p" + b, "\"v" + n + "\"", "g" + b)).Append('\n');
        operations.Append(n).Append("\t+\t").Append(Quad("s" + c, "q", "<http://example.org/o" + d + ">", null)).Append('\n');

        if (i > 4)
        {
            int j = i - 4;
            string m = j.ToString(CultureInfo.InvariantCulture), e = (j % 17).ToString(CultureInfo.InvariantCulture), f = (j % 3).ToString(CultureInfo.InvariantCulture);
            request.Retract(Iri("s" + e), Iri("p" + f), Literal("v" + m), Iri("g" + f));
            operations.Append(n).Append("\t-\t").Append(Quad("s" + e, "p" + f, "\"v" + m + "\"", "g" + f)).Append('\n');
        }

        await dataset.CommitAsync(request);
        await dataset.MaintainAsync();

        if (i % every == 0)
        {
            await dataset.CheckpointAsync(new Position(i));
        }
    }

    Console.WriteLine("head " + dataset.Head.Value + ", checkpoints " + string.Join(",", dataset.Checkpoints.Select(p => p.Value)));
}

Directory.CreateDirectory(Path.Combine(root, "expectations"));
File.WriteAllText(Path.Combine(root, "expectations", "operations.tsv"), operations.ToString());

sealed class Clock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}
