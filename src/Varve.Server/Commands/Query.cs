// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Sparql.Results;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server.Commands;

/// <summary><c>varve query &lt;dataset&gt;</c>: a query, its results to standard output in the format asked for.</summary>
internal static class Query
{
    internal static Command Command(Target target, Io io)
    {
        Option<string?> text = new("--query", "-q") { Description = "The query text." };
        Option<string?> file = new("--file") { Description = "A file holding the query." };
        Option<string?> asOf = new("--as-of") { Description = "Read the past: position:<n> or time:<RFC 3339>." };
        Option<string?> format = new("--format", "-f") { Description = "json, xml, csv or tsv for solutions and booleans (json by default); nt, ttl, nq or trig for graphs (nt by default)." };
        Command command = new("query", "Evaluate a SPARQL query over a dataset.");
        target.AddTo(command);
        command.Options.Add(text);
        command.Options.Add(file);
        command.Options.Add(format);
        command.Options.Add(asOf);
        command.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            byte[] query = await Commands.TextAsync(parsed.GetValue(text), parsed.GetValue(file), "query", cancellationToken).ConfigureAwait(false);
            string? syntax = parsed.GetValue(format);
            string? selector = parsed.GetValue(asOf);
            await using Opened opened = await target.OpenAsync(parsed, io, cancellationToken).ConfigureAwait(false);

            if (opened is Remote remote)
            {
                using HttpResponseMessage response = await remote.Client.QueryAsync(System.Text.Encoding.UTF8.GetString(query), Accept(syntax), selector, cancellationToken).ConfigureAwait(false);
                return await Commands.CopyAsync(response, io, cancellationToken).ConfigureAwait(false);
            }

            return await LocalAsync((Local)opened, query, syntax, selector, io, cancellationToken).ConfigureAwait(false);
        }, parsed.InvocationConfiguration.Error));
        return command;
    }

    /// <summary>The media type a format name asks a server for; every format a result can take when none is named.</summary>
    internal static string Accept(string? format) => format?.ToLowerInvariant() switch
    {
        null => "application/sparql-results+json, application/n-triples",
        "json" => "application/sparql-results+json",
        "xml" => "application/sparql-results+xml",
        "csv" => "text/csv",
        "tsv" => "text/tab-separated-values",
        "nt" => "application/n-triples",
        "ttl" => "text/turtle",
        "nq" => "application/n-quads",
        "trig" => "application/trig",
        _ => throw new Cli.CommandException("--format is json, xml, csv, tsv, nt, ttl, nq or trig."),
    };

    private static async Task<int> LocalAsync(Local local, byte[] text, string? format, string? selector, Io io, CancellationToken cancellationToken)
    {
        Varve.Sparql.Algebra.Query query;

        try
        {
            query = SparqlParser.ParseQuery(text);
        }
        catch (SparqlParseException error)
        {
            throw new Cli.CommandException("the query does not parse: " + error.Message);
        }

        Dataset dataset = local.Dataset;
        using DatasetView view = await ViewAsync(dataset, selector, cancellationToken).ConfigureAwait(false);
        StreamOutput output = new(io.Data);
        using QueryResults results = new SparqlEvaluator(local.Evaluation).Evaluate(query, view, cancellationToken);

        switch (results)
        {
            case SolutionResults solutions:
                {
                    using SparqlResultsWriter writer = new(output, ResultsFormat(format));
                    string[] names = new string[solutions.Variables.Count];

                    for (int i = 0; i < names.Length; i++)
                    {
                        names[i] = solutions.Variables[i].Name;
                    }

                    writer.WriteHead(names);

                    while (solutions.MoveNext())
                    {
                        writer.StartSolution();

                        for (int i = 0; i < names.Length; i++)
                        {
                            if (solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term))
                            {
                                writer.WriteBinding(i, term!);
                            }
                        }

                        writer.EndSolution();

                        if (output.IsDue)
                        {
                            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    writer.WriteEnd();
                    break;
                }

            case BooleanResult boolean:
                {
                    using SparqlResultsWriter writer = new(output, ResultsFormat(format));
                    writer.WriteBoolean(boolean.Value);
                    writer.WriteEnd();
                    break;
                }

            case TripleResults triples:
                GraphFormat(format);

                while (triples.MoveNext())
                {
                    output.WriteTerm(triples.Subject);
                    output.Write(" "u8);
                    output.WriteTerm(triples.Predicate);
                    output.Write(" "u8);
                    output.WriteTerm(triples.Object);
                    output.Write(" .\n"u8);

                    if (output.IsDue)
                    {
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                break;
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>The view a selector names: the head, or an as-of view (ADR 0096's selectors, in process).</summary>
    internal static async Task<DatasetView> ViewAsync(Dataset dataset, string? selector, CancellationToken cancellationToken)
    {
        if (selector is null)
        {
            return dataset.Pin();
        }

        if (!Varve.Protocol.Model.AsOf.TryParse(selector, out Varve.Protocol.Model.AsOf asOf))
        {
            throw new Cli.CommandException("--as-of is position:<n> or time:<RFC 3339 date-time>.");
        }

        Position position = asOf.Kind == Varve.Protocol.Model.AsOfKind.Position ? asOf.Position : dataset.PositionAt(asOf.Time);

        if (position.Value == 0 || position > dataset.Head)
        {
            throw new Cli.CommandException("no commit at " + selector + "; the head is " + dataset.Head.Value.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return await dataset.AsOfAsync(position, cancellationToken).ConfigureAwait(false);
    }

    private static SparqlResultsFormat ResultsFormat(string? format) => format?.ToLowerInvariant() switch
    {
        null or "json" => SparqlResultsFormat.Json,
        "xml" => SparqlResultsFormat.Xml,
        "csv" => SparqlResultsFormat.Csv,
        "tsv" => SparqlResultsFormat.Tsv,
        _ => throw new Cli.CommandException("a solution or boolean result is written as json, xml, csv or tsv."),
    };

    // A local CONSTRUCT or DESCRIBE is written as N-Triples whatever graph
    // format was named: the terms come materialised, and N-Triples is the
    // format every reader takes. A server negotiates the others.
    private static void GraphFormat(string? format)
    {
        if (format is not (null or "nt"))
        {
            throw new Cli.CommandException("a graph result of a local dataset is written as N-Triples (--format nt); ask a server for ttl, nq or trig.");
        }
    }

    internal static IReadOnlyList<string> Names(IReadOnlyList<Variable> variables)
    {
        string[] names = new string[variables.Count];

        for (int i = 0; i < names.Length; i++)
        {
            names[i] = variables[i].Name;
        }

        return names;
    }
}
