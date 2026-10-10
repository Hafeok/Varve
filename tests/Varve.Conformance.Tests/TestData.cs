// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Conformance.Tests;

/// <summary>
/// Where the W3C test data is, and whether it is there at all.
/// </summary>
/// <remarks>
/// The data is a git submodule pinned to a commit (ADR 0007). A clone without
/// <c>--recurse-submodules</c> has the directory and nothing in it, which would
/// enumerate zero cases and pass — the one failure mode that would make the
/// gate worthless. <see cref="SubmoduleGuardTests"/> turns that into a failure.
/// </remarks>
internal static class TestData
{
    /// <summary>The submodule root, whether or not it has been checked out.</summary>
    internal static string RdfTestsRoot { get; } =
        Path.Combine(RepositoryRoot(), "tests", "w3c", "rdf-tests");

    /// <summary>Whether the submodule has actually been checked out.</summary>
    internal static bool IsCheckedOut =>
        Directory.Exists(RdfTestsRoot) && File.Exists(Path.Combine(RdfTestsRoot, "README.md"));

    /// <summary>
    /// The second submodule, <c>w3c/rdf-canon</c> (ADR 0059, ADR 0007's
    /// pattern), whether or not it has been checked out.
    /// </summary>
    internal static string RdfCanonRoot { get; } =
        Path.Combine(RepositoryRoot(), "tests", "w3c", "rdf-canon");

    /// <summary>Whether the canonicalisation submodule has actually been checked out.</summary>
    internal static bool IsCanonCheckedOut =>
        File.Exists(Path.Combine(RdfCanonRoot, "tests", "manifest.ttl"));

    /// <summary>Resolves a manifest path from a suite to an absolute path.</summary>
    /// <summary>Where the json-ld-api suite publishes its tests, and the base of every entry's IRI.</summary>
    internal const string JsonLdPublishedRoot = "https://w3c.github.io/json-ld-api/tests/";

    /// <summary>The third submodule, <c>w3c/json-ld-api</c> (ADR 0123): the JSON-LD 1.1 API test suite.</summary>
    internal static string JsonLdRoot { get; } =
        Path.Combine(RepositoryRoot(), "tests", "w3c", "json-ld-api");

    internal static bool IsJsonLdCheckedOut =>
        File.Exists(Path.Combine(JsonLdRoot, "tests", "toRdf-manifest.jsonld"));

    /// <summary>
    /// The document loader the suite runs with: a published test IRI is read
    /// from the submodule, and anything else is not available. Remote
    /// contexts only ever come from the caller (ADR 0123).
    /// </summary>
    internal static bool LoadJsonLdDocument(ReadOnlySpan<byte> iri, out ReadOnlyMemory<byte> document)
    {
        string text = System.Text.Encoding.UTF8.GetString(iri);
        document = default;

        if (!text.StartsWith(JsonLdPublishedRoot, StringComparison.Ordinal))
        {
            return false;
        }

        string relative = text[JsonLdPublishedRoot.Length..];
        int fragment = relative.IndexOf('#', StringComparison.Ordinal);

        if (fragment >= 0)
        {
            relative = relative[..fragment];
        }

        string path = Path.Combine(JsonLdRoot, "tests", relative.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            return false;
        }

        document = File.ReadAllBytes(path);
        return true;
    }

    internal static string ResolveFromRoot(string relativePath) =>
        Path.Combine(RdfTestsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

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

        throw new InvalidOperationException(
            "Could not find the repository root (no Varve.slnx above " + AppContext.BaseDirectory + ").");
    }
}
