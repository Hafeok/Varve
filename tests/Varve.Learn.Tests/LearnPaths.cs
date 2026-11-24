// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Learn.Tests;

/// <summary>Where the repository and the learning track's samples are.</summary>
internal static class LearnPaths
{
    /// <summary>The repository root: the first directory above the test that holds <c>Varve.slnx</c>.</summary>
    internal static string Root { get; } = FindRoot();

    /// <summary><c>samples/learn/</c>.</summary>
    internal static string Samples => Path.Combine(Root, "samples", "learn");

    private static string FindRoot()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Varve.slnx")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("The repository root is not above " + AppContext.BaseDirectory + ".");
    }
}
