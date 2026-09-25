// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Security.Cryptography;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Writing;

namespace Varve.Benchmarks;

/// <summary>
/// Generates the N-Triples translations of the SPARQL suites' RDF/XML files
/// (ADR 0027's dated note, <c>tests/fixtures/w3c-rdfxml/README.md</c>): one
/// offline run of dotNetRDF's RDF/XML parser, each output beside the SHA-256
/// of its original, so the conformance harness can refuse a translation made
/// from a file that has since changed. Nothing reads dotNetRDF at test time.
/// </summary>
internal static class ConvertRdfXml
{
    private const string PublishedRoot = "https://w3c.github.io/rdf-tests/";

    /// <summary>The directories whose RDF/XML the query evaluation suites read.</summary>
    private static readonly string[] Directories = ["sparql/sparql10/sort", "sparql/sparql11/subquery"];

    internal static void Run(string repositoryRoot)
    {
        string tests = Path.Combine(repositoryRoot, "tests", "w3c", "rdf-tests");
        string output = Path.Combine(repositoryRoot, "tests", "fixtures", "w3c-rdfxml");
        foreach (string directory in Directories)
        {
            foreach (string file in Directory.GetFiles(Path.Combine(tests, directory), "*.rdf"))
            {
                string relative = Path.GetRelativePath(tests, file).Replace('\\', '/');
                Graph graph = new() { BaseUri = new Uri(PublishedRoot + relative) };
                using (StreamReader reader = new(file))
                {
                    new RdfXmlParser().Load(graph, reader);
                }

                string target = Path.Combine(output, relative + ".nt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                new NTriplesWriter(NTriplesSyntax.Rdf11).Save(graph, target);
                File.WriteAllText(target + ".sha256", Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))) + "\n");
                Console.WriteLine($"{relative}: {graph.Triples.Count} triples");
            }
        }
    }
}
