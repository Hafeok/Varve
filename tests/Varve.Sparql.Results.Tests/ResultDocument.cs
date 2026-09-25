// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Varve.Rdf;

namespace Varve.Sparql.Results.Tests;

/// <summary>
/// A result document read to the end and rendered as text, so that two reads
/// can be compared with string equality: the head, each solution's terms in
/// N-Triples-like form, and the error with its position.
/// </summary>
internal static class ResultDocument
{
    internal static string Render(ReadOnlySequence<byte> utf8, SparqlResultsFormat format)
    {
        SparqlResultsReader reader = new(utf8, format);
        StringBuilder text = new();
        if (reader.ReadHead())
        {
            if (reader.IsBoolean)
            {
                text.Append("boolean ").Append(reader.Boolean ? "true" : "false").Append('\n');
            }
            else
            {
                text.Append("vars ").AppendJoin(' ', reader.Variables).Append('\n');
            }

            while (reader.Read())
            {
                SolutionView solution = reader.Current;
                for (int i = 0; i < solution.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(" | ");
                    }

                    text.Append(solution.TryGet(i, out RdfTermView term) ? Term(term.Materialise()) : "-");
                }

                text.Append('\n');
            }
        }

        if (reader.Error.IsError)
        {
            text.Append("error ").Append(reader.Error.Kind).Append(" at ").Append(reader.Error.Position.ToString());
        }

        return text.ToString();
    }

    internal static string Render(byte[] utf8, SparqlResultsFormat format) =>
        Render(new ReadOnlySequence<byte>(utf8), format);

    internal static string Render(string text, SparqlResultsFormat format) =>
        Render(Encoding.UTF8.GetBytes(text), format);

    /// <summary>Two segments, split at <paramref name="at"/>.</summary>
    internal static ReadOnlySequence<byte> Split(byte[] utf8, int at)
    {
        Segment first = new(utf8.AsMemory(0, at), 0);
        Segment second = first.Append(utf8.AsMemory(at));
        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    internal static string Term(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        RdfTermKind.TripleTerm => "<<( " + Term(term.Subject!) + " " + Term(term.Predicate!) + " " + Term(term.Object!) + " )>>",
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\""
            + (term.Language.IsEmpty ? "" : "@" + Encoding.UTF8.GetString(term.Language)
                + (term.Direction == TextDirection.None ? "" : term.Direction == TextDirection.LeftToRight ? "--ltr" : "--rtl"))
            + (term.Datatype is null ? "" : "^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"),
    };

    /// <summary>The repository's root, found by walking up to the solution file.</summary>
    internal static string RepositoryRoot
    {
        get
        {
            string? directory = AppContext.BaseDirectory;
            while (directory is not null && !File.Exists(Path.Combine(directory, "Varve.slnx")))
            {
                directory = Path.GetDirectoryName(directory);
            }

            return directory ?? throw new InvalidOperationException("Varve.slnx not found above the test binaries.");
        }
    }

    internal static string SparqlSuites => Path.Combine(RepositoryRoot, "tests", "w3c", "rdf-tests", "sparql");

    internal static SparqlResultsFormat? FormatOf(string path) => Path.GetExtension(path).ToLower(CultureInfo.InvariantCulture) switch
    {
        ".srx" => SparqlResultsFormat.Xml,
        ".srj" => SparqlResultsFormat.Json,
        ".tsv" => SparqlResultsFormat.Tsv,
        ".csv" => SparqlResultsFormat.Csv,
        _ => null,
    };

    /// <summary>Every result document in the pinned SPARQL suites, relative to the suites' root.</summary>
    internal static IEnumerable<string> Corpus()
    {
        if (!Directory.Exists(SparqlSuites))
        {
            yield break;
        }

        List<string> files = [];
        foreach (string file in Directory.EnumerateFiles(SparqlSuites, "*", SearchOption.AllDirectories))
        {
            if (FormatOf(file) is not null)
            {
                files.Add(Path.GetRelativePath(SparqlSuites, file).Replace('\\', '/'));
            }
        }

        files.Sort(StringComparer.Ordinal);
        foreach (string file in files)
        {
            yield return file;
        }
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            Segment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
