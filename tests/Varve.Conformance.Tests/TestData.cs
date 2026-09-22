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

    /// <summary>Resolves a manifest path from a suite to an absolute path.</summary>
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
