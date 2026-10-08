// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The W3C SPARQL 1.1 update evaluation suites (<c>sparql-update-store.md</c>
/// §7), each case over a fresh store through <c>Varve.Sparql.Store</c>, one
/// ratchet line per case, named by its test IRI; and each case again through
/// the protocol with an <c>all</c> grant (ADR 0107), <c>&lt;test IRI&gt;@protocol</c>.
/// </summary>
public class UpdateConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (UpdateEntry entry in UpdateCatalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
            yield return new TheoryDataRow<string>(entry.TestIri + "@protocol") { TestDisplayName = entry.TestIri + "@protocol" };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        const string Protocol = "@protocol";
        bool overHttp = testIri.EndsWith(Protocol, System.StringComparison.Ordinal);
        UpdateEntry entry = UpdateCatalogue.ByIri[overHttp ? testIri[..^Protocol.Length] : testIri];
        UpdateOutcome outcome = await UpdateRunner.RunAsync(entry, overHttp ? UpdateSubject.Protocol : UpdateSubject.Store, TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine(outcome.Result + ", " + outcome.Commits + " commit(s)");
        Assert.True(outcome.Failure is null, entry.Suite + " " + entry.Name + ": " + outcome.Failure);
    }
}
