// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using VDS.RDF.Parsing;
using Varve.Sparql;
using Varve.Sparql.Algebra;

namespace Varve.Benchmarks;

/// <summary>
/// Parsing the W3C syntax corpus, against dotNetRDF on the same files.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is every positive query case of the SPARQL 1.0 and 1.1 syntax
/// suites that both parsers accept, so that the two rows time the same work:
/// a few hundred small queries that between them reach every construct of the
/// grammar. A single large query would measure one shape; the corpus measures
/// the parser's dispatch. The 1.2 positive cases are a Varve-only row, because
/// dotNetRDF has no SPARQL 1.2. The large update of <c>syntax-update-2</c>, an
/// <c>INSERT DATA</c> of 868 quads in 12.9 KB, is the one big input.
/// </para>
/// <para>
/// <strong>Not gating</strong> (ADR 0027). The claim it supports belongs in a
/// report with the machine stated, which is what <c>README.md</c> is for.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SparqlBenchmarks
{
    private static long sink;

    private readonly ArrayBufferWriter<byte> _output = new(1 << 16);
    private SparqlCorpus _corpus = null!;
    private SparqlQueryParser _dotNetRdfQuery = null!;
    private SparqlUpdateParser _dotNetRdfUpdate = null!;

    [GlobalSetup]
    public void Load()
    {
        _corpus = SparqlCorpus.Load();
        _dotNetRdfQuery = new SparqlQueryParser();
        _dotNetRdfUpdate = new SparqlUpdateParser();

        // The denominators are checked, not asserted in prose.
        if (_corpus.Shared.Count == 0 || _corpus.Sparql12.Count == 0 || _corpus.LargeUpdate.Bytes.Length == 0)
        {
            throw new InvalidOperationException("The corpus is empty; is the submodule checked out?");
        }
    }

    /// <summary>Varve over the queries both parsers accept.</summary>
    [Benchmark(Description = "Varve — 1.0 and 1.1 corpus", Baseline = true)]
    public long Varve_Shared()
    {
        long nodes = 0;

        foreach (SparqlCase entry in _corpus.Shared)
        {
            Query query = SparqlParser.ParseQuery(entry.Bytes, entry.Options);
            nodes += query.Pattern.Span.End;
        }

        return nodes;
    }

    /// <summary>dotNetRDF over the same queries.</summary>
    [Benchmark(Description = "dotNetRDF — 1.0 and 1.1 corpus")]
    public long DotNetRdf_Shared()
    {
        long nodes = 0;

        foreach (SparqlCase entry in _corpus.Shared)
        {
            _dotNetRdfQuery.DefaultBaseUri = entry.BaseUri;
            VDS.RDF.Query.SparqlQuery query = _dotNetRdfQuery.ParseFromString(entry.Text);
            nodes += query.Variables is null ? 0 : 1;
        }

        return nodes;
    }

    /// <summary>Varve parsing and writing the algebra back, over the same queries.</summary>
    [Benchmark(Description = "Varve — 1.0 and 1.1 corpus, parse and write back")]
    public long Varve_Shared_RoundTrip()
    {
        long bytes = 0;

        foreach (SparqlCase entry in _corpus.Shared)
        {
            Query query = SparqlParser.ParseQuery(entry.Bytes, entry.Options);
            _output.Clear();
            SparqlWriter.Write(query, _output);
            bytes += _output.WrittenCount;
        }

        return bytes;
    }

    /// <summary>Varve over the SPARQL 1.2 positive cases. No baseline: dotNetRDF has no 1.2.</summary>
    [Benchmark(Description = "Varve — 1.2 corpus")]
    public long Varve_Sparql12()
    {
        long nodes = 0;

        foreach (SparqlCase entry in _corpus.Sparql12)
        {
            if (entry.IsUpdate)
            {
                nodes += SparqlParser.ParseUpdate(entry.Bytes, entry.Options).Operations.Count;
            }
            else
            {
                nodes += SparqlParser.ParseQuery(entry.Bytes, entry.Options).Pattern.Span.End;
            }
        }

        return nodes;
    }

    /// <summary>Varve on the large INSERT DATA.</summary>
    [Benchmark(Description = "Varve — large update")]
    public int Varve_LargeUpdate()
    {
        Update update = SparqlParser.ParseUpdate(_corpus.LargeUpdate.Bytes, _corpus.LargeUpdate.Options);
        int quads = 0;

        foreach (UpdateOperation operation in update.Operations)
        {
            quads += ((InsertData)operation).Quads.Count;
        }

        sink += quads;
        return quads;
    }

    /// <summary>dotNetRDF on the same update.</summary>
    [Benchmark(Description = "dotNetRDF — large update")]
    public int DotNetRdf_LargeUpdate()
    {
        _dotNetRdfUpdate.DefaultBaseUri = _corpus.LargeUpdate.BaseUri;
        VDS.RDF.Update.SparqlUpdateCommandSet commands = _dotNetRdfUpdate.ParseFromString(_corpus.LargeUpdate.Text);
        return commands.CommandCount;
    }
}

/// <summary>One corpus file, ready for both parsers.</summary>
internal sealed record SparqlCase(string Name, byte[] Bytes, string Text, SparqlParseOptions Options, Uri BaseUri, bool IsUpdate);

/// <summary>The syntax corpus, read from the submodule's manifests.</summary>
internal sealed class SparqlCorpus
{
    private static readonly string[] SharedSuites =
    [
        "sparql10/syntax-sparql1", "sparql10/syntax-sparql2", "sparql10/syntax-sparql3", "sparql10/syntax-sparql4", "sparql10/syntax-sparql5",
        "sparql11/syntax-query", "sparql11/syntax-fed",
    ];

    private static readonly string[] Sparql12Suites =
    [
        "sparql12/syntax-triple-terms-positive", "sparql12/syntax", "sparql12/version", "sparql12/codepoint-escapes", "sparql12/lang-basedir",
    ];

    private SparqlCorpus(List<SparqlCase> shared, int rejectedByDotNetRdf, List<SparqlCase> sparql12, SparqlCase largeUpdate)
    {
        Shared = shared;
        RejectedByDotNetRdf = rejectedByDotNetRdf;
        Sparql12 = sparql12;
        LargeUpdate = largeUpdate;
    }

    /// <summary>The 1.0 and 1.1 positive query cases both parsers accept.</summary>
    internal IReadOnlyList<SparqlCase> Shared { get; }

    /// <summary>How many of those dotNetRDF rejected and were therefore left out.</summary>
    internal int RejectedByDotNetRdf { get; }

    /// <summary>The 1.2 positive cases, queries and updates.</summary>
    internal IReadOnlyList<SparqlCase> Sparql12 { get; }

    /// <summary><c>syntax-update-2/large-request-01.ru</c>.</summary>
    internal SparqlCase LargeUpdate { get; }

    internal static SparqlCorpus Load()
    {
        string root = Path.Combine(RepositoryRoot(), "tests", "w3c", "rdf-tests", "sparql");
        SparqlQueryParser probe = new();
        List<SparqlCase> shared = [];
        int rejected = 0;

        foreach (SparqlCase entry in Positive(root, SharedSuites, SparqlVersion.Sparql11))
        {
            if (entry.IsUpdate)
            {
                continue;
            }

            try
            {
                probe.DefaultBaseUri = entry.BaseUri;
                probe.ParseFromString(entry.Text);
                shared.Add(entry);
            }
            catch (RdfParseException)
            {
                rejected++;
            }
        }

        List<SparqlCase> sparql12 = [.. Positive(root, Sparql12Suites, SparqlVersion.Sparql12)];
        string largePath = Path.Combine(root, "sparql11", "syntax-update-2", "large-request-01.ru");
        SparqlCase large = Case(largePath, SparqlVersion.Sparql11, isUpdate: true);

        return new SparqlCorpus(shared, rejected, sparql12, large);
    }

    private static IEnumerable<SparqlCase> Positive(string root, string[] suites, SparqlVersion version)
    {
        foreach (string suite in suites)
        {
            string directory = Path.Combine(root, suite.Replace('/', Path.DirectorySeparatorChar));
            string manifest = File.ReadAllText(Path.Combine(directory, "manifest.ttl"));

            foreach (Match match in Regex.Matches(manifest, @"rdf:type\s+mf:(Positive\w*SyntaxTest\w*)\s*;.*?mf:action\s+<([^>]+)>", RegexOptions.Singleline))
            {
                string file = match.Groups[2].Value;
                bool isUpdate = match.Groups[1].Value.Contains("Update", StringComparison.Ordinal) || file.EndsWith(".ru", StringComparison.Ordinal);
                yield return Case(Path.Combine(directory, file), version, isUpdate);
            }
        }
    }

    private static SparqlCase Case(string path, SparqlVersion version, bool isUpdate)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Uri baseUri = new(Path.GetFullPath(path));
        SparqlParseOptions options = new(Encoding.UTF8.GetBytes(baseUri.AbsoluteUri), version);
        return new SparqlCase(Path.GetFileName(path), bytes, Encoding.UTF8.GetString(bytes), options, baseUri, isUpdate);
    }

    internal void Print()
    {
        long sharedBytes = 0;

        foreach (SparqlCase entry in Shared)
        {
            sharedBytes += entry.Bytes.Length;
        }

        long bytes12 = 0;

        foreach (SparqlCase entry in Sparql12)
        {
            bytes12 += entry.Bytes.Length;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"1.0 and 1.1 corpus: {Shared.Count} queries both parsers accept ({RejectedByDotNetRdf} that dotNetRDF rejects left out), {sharedBytes:N0} bytes"));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"1.2 corpus: {Sparql12.Count} cases, {bytes12:N0} bytes"));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"large update: {LargeUpdate.Bytes.Length:N0} bytes"));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory + ".");
    }
}
