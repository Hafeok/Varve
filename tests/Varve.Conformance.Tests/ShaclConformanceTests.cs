// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The W3C SHACL suites (ADR 0149): every approved <c>sht:Validate</c> case
/// of the 2017 suite's and SHACL 1.2's <c>core</c> and <c>sparql</c>, over
/// <c>InMemoryDataset</c> and over the store, one ratchet line per case per
/// subject — <c>&lt;test IRI&gt;@dataset</c> and <c>@store</c>.
/// </summary>
public class ShaclConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        foreach (ShaclEntry entry in ShaclSuite.Entries)
        {
            foreach (string subject in ShaclRunner.SubjectNames)
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
        ShaclEntry entry = ShaclSuite.ByIri.Value[testIri[..at]];
        string? failure = await ShaclRunner.RunAsync(entry, testIri[(at + 1)..], TestContext.Current.CancellationToken);
        Assert.True(failure is null, entry.Suite + " " + entry.Name + " (" + testIri[(at + 1)..] + "): " + failure);
    }
}

/// <summary>Guards that the SHACL suites are present and enumerate what their manifests list (ADR 0149).</summary>
public class ShaclGuardTests
{
    [Fact]
    public void The_data_shapes_submodule_is_checked_out() =>
        Assert.True(ShaclSuite.IsCheckedOut, "tests/w3c/data-shapes is not checked out: git submodule update --init --recursive");

    [Theory]
    [InlineData("shacl10/core", 98)]
    [InlineData("shacl10/sparql", 22)]
    [InlineData("shacl12/core", 162)]
    [InlineData("shacl12/sparql", 43)]
    public void Every_shacl_suite_enumerates_its_approved_validate_cases(string suite, int expected)
    {
        int count = 0;
        foreach (ShaclEntry entry in ShaclSuite.Entries)
        {
            if (entry.Suite == suite)
            {
                count++;
            }
        }

        Assert.Equal(expected, count);
    }

    [Fact]
    public void Shacl_test_iris_are_unique() =>
        Assert.Equal(ShaclSuite.Entries.Count, ShaclSuite.ByIri.Value.Count);
}
