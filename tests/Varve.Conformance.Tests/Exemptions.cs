// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;

namespace Varve.Conformance.Tests;

/// <summary>
/// The cases <c>baseline/exemptions.txt</c> names: decided not to pass yet,
/// each with its justification. The ratchet (<c>eng/ratchet.cs</c>) reads
/// the same file; this is the harness's own reading of it, for the guards
/// that run cases outside the ratchet and must not require what the
/// baseline has excused.
/// </summary>
internal static class Exemptions
{
    private static readonly Lazy<HashSet<string>> Cases = new(Read);

    internal static string Path { get; } =
        System.IO.Path.Combine(TestData.RdfTestsRoot, "..", "..", "Varve.Conformance.Tests", "baseline", "exemptions.txt");

    /// <summary>Whether the baseline exempts a case, named as its ratchet line names it.</summary>
    internal static bool Covers(string caseId) => Cases.Value.Contains(caseId);

    private static HashSet<string> Read()
    {
        HashSet<string> cases = new(StringComparer.Ordinal);

        if (!File.Exists(Path))
        {
            return cases;
        }

        foreach (string line in File.ReadAllLines(Path))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int space = trimmed.IndexOf(' ', StringComparison.Ordinal);
            cases.Add(space < 0 ? trimmed : trimmed[..space]);
        }

        return cases;
    }
}
