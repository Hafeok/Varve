// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Conformance.Tests;

/// <summary>
/// Whether an actual dataset is the expected one up to its blank nodes: the
/// comparison for <c>CONSTRUCT</c> and <c>DESCRIBE</c> results and for update
/// results.
/// </summary>
internal static class DatasetComparison
{
    /// <summary>Null when the datasets are isomorphic, otherwise why not.</summary>
    internal static string? Compare(IReadOnlyList<ParsedQuad> actual, IReadOnlyList<ParsedQuad> expected)
    {
        IsomorphismResult verdict = Isomorphism.Compare(actual, expected);
        return verdict.Verdict == IsomorphismVerdict.Same
            ? null
            : verdict.Reason + "\n  actual:\n    " + string.Join("\n    ", actual) + "\n  expected:\n    " + string.Join("\n    ", expected);
    }
}
