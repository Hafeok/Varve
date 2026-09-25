// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Varve.Rdf;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The W3C query evaluation suites (<c>sparql-evaluation.md</c> §12.1): every
/// case that is not blocked, over <see cref="InMemoryDataset"/> and over the
/// store's default projection, one ratchet line per case per subject —
/// <c>&lt;test IRI&gt;@dataset</c> and <c>&lt;test IRI&gt;@store</c>.
/// </summary>
public class EvaluationConformanceTests
{
    internal static readonly string[] SubjectNames = ["dataset", "store"];

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EvaluationEntry entry in EvaluationCatalogue.Entries)
        {
            if (EvaluationCatalogue.IsBlocked(entry))
            {
                continue;
            }

            foreach (string subject in SubjectNames)
            {
                string id = entry.TestIri + "@" + subject;
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        int at = testIri.LastIndexOf('@');
        EvaluationEntry entry = EvaluationCatalogue.ByIri[testIri[..at]];
        IEvaluationSubject subject = EvaluationSubjects.ByName(testIri[(at + 1)..]);
        string? failure = await EvaluationRunner.RunAsync(entry, subject, TestContext.Current.CancellationToken);
        Assert.True(failure is null, entry.Suite + " " + entry.Name + " (" + subject.Name + "): " + failure);
    }
}
