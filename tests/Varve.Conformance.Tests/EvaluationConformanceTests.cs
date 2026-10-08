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
/// case that is not blocked, over <see cref="InMemoryDataset"/>, over the
/// store's default projection, over the store through a graph scope naming
/// every graph of the case, and through the protocol with an <c>all</c>
/// grant (ADR 0106), one ratchet line per case per subject —
/// <c>&lt;test IRI&gt;@dataset</c>, <c>@store</c>, <c>@scoped</c> and
/// <c>@protocol</c>.
/// </summary>
public class EvaluationConformanceTests
{
    internal static readonly string[] SubjectNames = ["dataset", "store", "scoped", "protocol"];

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
        string name = testIri[(at + 1)..];

        if (name == "protocol")
        {
            string? overHttp = await ProtocolEvaluationRunner.RunAsync(entry, TestContext.Current.CancellationToken);
            Assert.True(overHttp is null, entry.Suite + " " + entry.Name + " (protocol): " + overHttp);
            return;
        }

        IEvaluationSubject subject = EvaluationSubjects.ByName(name);
        string? failure = await EvaluationRunner.RunAsync(entry, subject, TestContext.Current.CancellationToken);
        Assert.True(failure is null, entry.Suite + " " + entry.Name + " (" + subject.Name + "): " + failure);
    }
}
