// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The guards on the protocol suites: each enumerates the cases its manifest
/// lists, pinned, so that a manifest read short is a failure rather than a
/// smaller suite that passes (ADR 0092).
/// </summary>
public class ProtocolGuardTests
{
    [Theory]
    [InlineData("sparql11/protocol", 34)]
    [InlineData("sparql11/graph-store-protocol", 13)]
    [InlineData("sparql11/http-rdf-update", 18)]
    public void Each_protocol_suite_enumerates_the_cases_its_manifest_lists(string suite, int count)
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Assert.Equal(count, ProtocolCatalogue.Entries.Count(e => e.Suite == suite));
    }

    [Fact]
    public void The_service_description_suite_lists_three_names_and_each_is_a_check_of_ours()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Assert.Equal(3, ServiceDescriptionChecks.Iris.Count);
    }

    [Fact]
    public void Every_protocol_case_has_requests_and_a_unique_iri()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Assert.All(ProtocolCatalogue.Entries, e => Assert.NotEmpty(e.Requests));
        Assert.Equal(ProtocolCatalogue.Entries.Count, ProtocolCatalogue.Entries.Select(e => e.TestIri).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(65, ProtocolCatalogue.Entries.Count);
    }
}
