// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The protocol suites (ADR 0092) against an in-process server, each case
/// over the memory store and the file store, one ratchet line per case per
/// subject: <c>&lt;test IRI&gt;@memory</c> and <c>&lt;test IRI&gt;@file</c>.
/// </summary>
public class ProtocolConformanceTests
{
    public static IEnumerable<TheoryDataRow<string, string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ProtocolEntry entry in ProtocolCatalogue.Entries)
        {
            foreach (string subject in new[] { "memory", "file" })
            {
                yield return new TheoryDataRow<string, string>(entry.TestIri, subject) { TestDisplayName = entry.TestIri + "@" + subject };
            }
        }

        foreach (string check in ServiceDescriptionChecks.Iris)
        {
            foreach (string subject in new[] { "memory", "file" })
            {
                yield return new TheoryDataRow<string, string>(check, subject) { TestDisplayName = check + "@" + subject };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri, string subject)
    {
        ProtocolSubject on = subject == "file" ? ProtocolSubject.File : ProtocolSubject.Memory;
        string? failure = ProtocolCatalogue.ByIri.TryGetValue(testIri, out ProtocolEntry? entry)
            ? await ProtocolRunner.RunAsync(entry, on, TestContext.Current.CancellationToken)
            : await ServiceDescriptionChecks.RunAsync(testIri, on, TestContext.Current.CancellationToken);
        Assert.True(failure is null, testIri + "@" + subject + ": " + failure);
    }
}
