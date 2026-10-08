// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.CommandLine;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Turtle;

namespace Varve.Server.Commands;

/// <summary><c>varve export &lt;dataset&gt;</c>: every quad as N-Quads or TriG, or one graph as N-Triples or Turtle.</summary>
internal static class Export
{
    internal static Command Command(Target target, Io io)
    {
        Option<string?> format = new("--format", "-f") { Description = "nq (default) or trig for the dataset; nt (default) or ttl for one graph." };
        Option<string?> graph = new("--graph") { Description = "One graph: its IRI, or `default`." };
        Option<string?> asOf = new("--as-of") { Description = "Export the past: position:<n> or time:<RFC 3339>; local datasets only." };
        Command command = new("export", "Write a dataset, or one of its graphs, to standard output.");
        target.AddTo(command);
        command.Options.Add(format);
        command.Options.Add(graph);
        command.Options.Add(asOf);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            string? named = parsed.GetValue(graph);
            string? syntax = parsed.GetValue(format)?.ToLowerInvariant();
            await using Opened opened = await target.OpenAsync(parsed, io, cancellationToken).ConfigureAwait(false);

            if (opened is Remote remote)
            {
                if (parsed.GetValue(asOf) is not null)
                {
                    throw new Cli.CommandException("--as-of exports a local dataset; a server is read at its head here.");
                }

                return await RemoteAsync(remote, named, syntax, io, cancellationToken).ConfigureAwait(false);
            }

            return await LocalAsync((Local)opened, named, syntax, parsed.GetValue(asOf), io, cancellationToken).ConfigureAwait(false);
        }, parsed.InvocationConfiguration.Error));
        return command;
    }

    private static async Task<int> LocalAsync(Local local, string? named, string? syntax, string? selector, Io io, CancellationToken cancellationToken)
    {
        using DatasetView view = await Query.ViewAsync(local.Dataset, selector, cancellationToken).ConfigureAwait(false);
        StreamOutput output = new(io.Data);
        GraphPattern pattern;

        if (named is null)
        {
            pattern = GraphPattern.Any;
        }
        else if (named == "default")
        {
            pattern = GraphPattern.DefaultGraph;
        }
        else if (view.TryInternalise(RdfTerm.Iri(Encoding.UTF8.GetBytes(named)), out TermHandle handle))
        {
            pattern = GraphPattern.Named(handle);
        }
        else
        {
            // A graph the dataset never named holds nothing: an empty document.
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        bool turtle = syntax is "trig" or "ttl";

        if (syntax is not (null or "nq" or "trig" or "nt" or "ttl"))
        {
            throw new Cli.CommandException("--format is nq or trig for a dataset, nt or ttl for one graph.");
        }

        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, pattern);

        if (turtle)
        {
            TurtleWriteOptions options = new() { Syntax = named is null ? RdfSyntax.TriG : RdfSyntax.Turtle };
            using TurtleWriter writer = new(output, in options);

            while (cursor.MoveNext())
            {
                Quad quad = named is null ? cursor.Current : new Quad(cursor.Current.Subject, cursor.Current.Predicate, cursor.Current.Object);
                writer.Write(in quad, view);

                if (output.IsDue)
                {
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        else
        {
            WriteOptions options = new() { Syntax = named is null ? RdfSyntax.NQuads : RdfSyntax.NTriples };

            while (cursor.MoveNext())
            {
                Quad quad = named is null ? cursor.Current : new Quad(cursor.Current.Subject, cursor.Current.Predicate, cursor.Current.Object);
                NQuadsWriter.Write(output, in quad, view, in options);

                if (output.IsDue)
                {
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    // A server has no "every quad" endpoint: one graph is a Graph Store GET,
    // and the dataset is a query whose rows are written as N-Quads.
    private static async Task<int> RemoteAsync(Remote remote, string? named, string? syntax, Io io, CancellationToken cancellationToken)
    {
        if (named is not null)
        {
            string accept = syntax switch
            {
                null or "nt" => "application/n-triples",
                "ttl" => "text/turtle",
                _ => throw new Cli.CommandException("--format is nt or ttl for one graph."),
            };
            using HttpResponseMessage graph = await remote.Client.GetGraphAsync(named == "default" ? null : named, accept, cancellationToken).ConfigureAwait(false);
            return await Commands.CopyAsync(graph, io, cancellationToken).ConfigureAwait(false);
        }

        if (syntax is not (null or "nq"))
        {
            throw new Cli.CommandException("a server's dataset is exported as N-Quads (--format nq).");
        }

        const string Everything = "SELECT ?s ?p ?o ?g WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } }";
        using HttpResponseMessage response = await remote.Client.QueryAsync(Everything, "application/sparql-results+json", null, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return await Commands.CopyAsync(response, io, cancellationToken).ConfigureAwait(false);
        }

        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Varve.Sparql.Results.SparqlResultsReader reader = new(body, Varve.Sparql.Results.SparqlResultsFormat.Json);
        StreamOutput output = new(io.Data);

        if (!reader.ReadHead())
        {
            throw new Cli.CommandException("the server's results do not parse: " + reader.Error.Message);
        }

        int s = Column(reader.Variables, "s");
        int p = Column(reader.Variables, "p");
        int o = Column(reader.Variables, "o");
        int g = Column(reader.Variables, "g");

        while (reader.Read())
        {
            Varve.Sparql.Results.SolutionView row = reader.Current;

            if (!row.TryGet(s, out RdfTermView subject) || !row.TryGet(p, out RdfTermView predicate) || !row.TryGet(o, out RdfTermView @object))
            {
                continue;
            }

            output.WriteTerm(subject.Materialise());
            output.Write(" "u8);
            output.WriteTerm(predicate.Materialise());
            output.Write(" "u8);
            output.WriteTerm(@object.Materialise());

            if (row.TryGet(g, out RdfTermView graph))
            {
                output.Write(" "u8);
                output.WriteTerm(graph.Materialise());
            }

            output.Write(" .\n"u8);

            if (output.IsDue)
            {
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static int Column(IReadOnlyList<string> variables, string name)
    {
        for (int i = 0; i < variables.Count; i++)
        {
            if (variables[i] == name)
            {
                return i;
            }
        }

        return -1;
    }
}
