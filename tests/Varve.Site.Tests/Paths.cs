// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Site.Tests;

/// <summary>The repository the tests run in, and its site fixtures.</summary>
internal static class Paths
{
    public static string Root { get; } = FindRoot();

    public static string Fixture(string name) => Path.Combine(Root, "tests", "fixtures", "site", name);

    public static string Reference(string page) => Path.Combine(Root, "docs", "ui", "site", "reference", page.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No Varve.slnx above " + AppContext.BaseDirectory + ".");
    }
}
