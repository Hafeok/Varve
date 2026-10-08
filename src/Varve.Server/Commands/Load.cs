// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Server.Commands;

/// <summary><c>varve load &lt;dir&gt; &lt;files…&gt;</c>: the bulk loader (ADR 0081), one commit for every file.</summary>
internal static class Load
{
    internal static Command Command(Io io)
    {
        Argument<string> directory = new("directory") { Description = "A dataset directory." };
        Argument<string[]> files = new("files") { Description = "N-Triples (.nt), N-Quads (.nq), Turtle (.ttl) or TriG (.trig) files, by extension." };
        Option<string?> graph = new("--graph") { Description = "Load every triple into this named graph; a document's own graphs are replaced." };
        Option<int> memory = new("--memory") { Description = "MiB the load may hold in memory.", DefaultValueFactory = _ => 256 };
        Option<int?> workers = new("--workers") { Description = "Threads that resolve and spill while the parser reads (ADR 0108); the processor count less one by default." };
        Command command = new("load", "Bulk-load files into a local dataset as one commit.");
        command.Arguments.Add(directory);
        command.Arguments.Add(files);
        command.Options.Add(graph);
        command.Options.Add(memory);
        command.Options.Add(workers);
        Target target = new();
        command.Options.Add(target.AllowEndpoint);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            string[] paths = parsed.GetValue(files) ?? [];

            if (paths.Length == 0)
            {
                throw new Cli.CommandException("Name at least one file to load.");
            }

            RdfTerm? into = parsed.GetValue(graph) is { } iri ? RdfTerm.Iri(Encoding.UTF8.GetBytes(iri)) : null;
            await using Local local = await target.OpenLocalAsync(parsed.GetValue(directory)!, parsed, mustExist: true, cancellationToken).ConfigureAwait(false);
            ByteCount budget = new((long)Math.Max(16, parsed.GetValue(memory)) << 20);
            BulkLoadOptions options = parsed.GetValue(workers) is int count
                ? new BulkLoadOptions { MemoryBytes = budget, Workers = Math.Max(1, count) }
                : new BulkLoadOptions { MemoryBytes = budget };
            await using BulkLoad load = await local.Dataset.BeginBulkLoadAsync(options, cancellationToken).ConfigureAwait(false);

            foreach (string path in paths)
            {
                Varve.Turtle.ParseResult result = Parse(path, load, into);

                if (!result.Succeeded)
                {
                    throw new Cli.CommandException(path + " does not parse: " + result.FirstError.ToString());
                }

                await io.Out.WriteLineAsync(path + ": " + result.QuadCount.ToString("N0", CultureInfo.InvariantCulture) + " quads").ConfigureAwait(false);
            }

            CommitResult committed = await load.CommitAsync(new CommitMetadata(), cancellationToken).ConfigureAwait(false);
            await io.Out.WriteLineAsync(committed.Outcome switch
            {
                CommitOutcome.Committed => "committed, position " + committed.Position.Value.ToString(CultureInfo.InvariantCulture),
                CommitOutcome.NoChange => "no change",
                _ => committed.Outcome.ToString().ToLowerInvariant() + (committed.Reason is null ? "" : ": " + committed.Reason),
            }).ConfigureAwait(false);
            return committed.Outcome is CommitOutcome.Committed or CommitOutcome.NoChange ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));
        return command;
    }

    private static Varve.Turtle.ParseResult Parse(string path, BulkLoad load, RdfTerm? into)
    {
        RdfSyntax syntax = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".nt" => RdfSyntax.NTriples,
            ".nq" => RdfSyntax.NQuads,
            ".trig" => RdfSyntax.TriG,
            ".ttl" => RdfSyntax.Turtle,
            _ => throw new Cli.CommandException(path + ": the extension names no syntax; .nt, .nq, .ttl or .trig."),
        };
        QuadHandler handler = into is null
            ? load.Assert
            : (in QuadView quad) => load.Assert(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), into);
        using FileStream stream = File.OpenRead(path);

        return syntax is RdfSyntax.NTriples or RdfSyntax.NQuads
            ? NQuadsParser.Parse(stream, handler, new ParseOptions { Syntax = syntax })
            : TurtleParser.Parse(stream, handler, new TurtleOptions { Syntax = syntax, BaseIri = Encoding.UTF8.GetBytes(new Uri(Path.GetFullPath(path)).AbsoluteUri) });
    }
}
