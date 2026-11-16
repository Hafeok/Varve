// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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

    [Fact]
    public void A_rewritten_body_is_sent_rewritten_whatever_its_length_in_chars()
    {
        // Twelve bytes, eleven chars: "é" is two bytes. A rewrite that adds
        // one char makes the text's length in chars equal the body's length
        // in bytes, which the runner once took for "unchanged" and sent the
        // transcript's bytes as they were (#262).
        byte[] body = Encoding.UTF8.GetBytes("<urn:a> \"é\"");
        Assert.Equal(12, body.Length);

        byte[] sent = ProtocolRunner.Body(body, text => text.Replace("urn:a", "urn:ab", StringComparison.Ordinal));

        Assert.Equal("<urn:ab> \"é\"", Encoding.UTF8.GetString(sent));
        Assert.Same(body, ProtocolRunner.Body(body, text => text));
    }

    [Fact]
    public async Task A_failure_states_the_content_length_sent_the_bytes_written_and_the_body()
    {
        SentContent content = new(Encoding.UTF8.GetBytes("<urn:a> <urn:p> \"ø\""));
        Assert.Equal(" (sent Content-Length 20, wrote 0 bytes: <urn:a> <urn:p> \"ø\")", ProtocolRunner.Sent(content));

        await content.CopyToAsync(System.IO.Stream.Null, TestContext.Current.CancellationToken);

        Assert.Equal(" (sent Content-Length 20, wrote 20 bytes: <urn:a> <urn:p> \"ø\")", ProtocolRunner.Sent(content));
        Assert.Equal("", ProtocolRunner.Sent(new ByteArrayContent([])));
        Assert.Equal("", ProtocolRunner.Sent(null));
    }

    [Fact]
    public void An_exempt_failure_says_so_and_the_image_reads_the_memory_stores_exemptions()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        const string Post = "http://www.w3.org/2009/sparql/docs/tests/data-sparql11/http-rdf-update/manifest#post__existing_graph";

        Assert.StartsWith("exempt", ProtocolConformanceTests.Exempt(Post, "memory"), StringComparison.Ordinal);
        Assert.StartsWith("exempt", ProtocolConformanceTests.Exempt(Post, "server"), StringComparison.Ordinal);
        Assert.Equal("", ProtocolConformanceTests.Exempt(Post + "-not-a-case", "server"));
    }
}
