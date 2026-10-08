// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>The endpoint policy (ADR 0103): the allow-list, the schemes, the authority, the private ranges.</summary>
public class EndpointPolicyTests
{
    private static readonly EndpointPolicy Public = new(["https://example.org/sparql", "http://data.example/"], allowPrivateAddresses: false);

    [Fact]
    public void The_default_policy_allows_nothing()
    {
        Assert.False(EndpointPolicy.None.Allows("https://example.org/sparql"u8, out string? reason));
        Assert.Contains("allow-list is empty", reason, StringComparison.Ordinal);
        Assert.Empty(EndpointPolicy.None.AllowedPrefixes);
    }

    [Theory]
    [InlineData("https://example.org/sparql")]
    [InlineData("https://example.org/sparql?default-graph-uri=x")]
    [InlineData("http://data.example/people.ttl")]
    public void An_iri_under_an_allowed_prefix_is_allowed(string iri)
    {
        Assert.True(Public.Allows(Encoding.UTF8.GetBytes(iri), out string? reason), reason);
    }

    [Theory]
    [InlineData("https://example.org/other", "not under an allowed prefix")]
    [InlineData("https://example.org.evil/sparql", "not under an allowed prefix")]
    [InlineData("ftp://example.org/sparql", "scheme")]
    [InlineData("file:///etc/passwd", "scheme")]
    [InlineData("https:///sparql", "not under")]
    [InlineData("https://user:secret@example.org/sparql", "credentials")]
    [InlineData("relative/path", "absolute")]
    [InlineData("https://example.org/spa rql", "absolute")]
    public void An_iri_outside_the_rules_is_refused_with_the_reason(string iri, string reason)
    {
        Assert.False(Public.Allows(Encoding.UTF8.GetBytes(iri), out string? why));
        Assert.Contains(reason, why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/sparql")]
    [InlineData("http://127.1.2.3/sparql")]
    [InlineData("http://localhost/sparql")]
    [InlineData("http://LOCALHOST:1234/sparql")]
    [InlineData("http://api.localhost/sparql")]
    [InlineData("http://10.0.0.5/sparql")]
    [InlineData("http://172.16.0.1/sparql")]
    [InlineData("http://172.31.255.255/sparql")]
    [InlineData("http://192.168.1.1/sparql")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://0.0.0.0/sparql")]
    [InlineData("http://[::1]/sparql")]
    [InlineData("http://[::]/sparql")]
    [InlineData("http://[fe80::1]/sparql")]
    [InlineData("http://[fc00::1]/sparql")]
    [InlineData("http://[fd12::1]:9000/sparql")]
    [InlineData("http://[::ffff:10.0.0.1]/sparql")]
    public void A_private_address_is_refused_unless_allowed(string iri)
    {
        EndpointPolicy refusing = new(["http://"], allowPrivateAddresses: false);
        EndpointPolicy allowing = new(["http://"], allowPrivateAddresses: true);
        Assert.False(refusing.Allows(Encoding.UTF8.GetBytes(iri), out string? reason));
        Assert.Contains("private", reason, StringComparison.Ordinal);
        Assert.True(allowing.Allows(Encoding.UTF8.GetBytes(iri), out reason), reason);
    }

    [Theory]
    [InlineData("http://172.32.0.1/sparql")]
    [InlineData("http://8.8.8.8/sparql")]
    [InlineData("http://[2001:db8::1]/sparql")]
    [InlineData("http://example.org/sparql")]
    public void A_public_address_is_not_private(string iri)
    {
        EndpointPolicy refusing = new(["http://"], allowPrivateAddresses: false);
        Assert.True(refusing.Allows(Encoding.UTF8.GetBytes(iri), out string? reason), reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.org/sparql")]
    [InlineData("ftp://example.org/")]
    public void A_prefix_that_is_not_an_http_iri_is_refused_at_construction(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new EndpointPolicy([prefix], allowPrivateAddresses: false));
    }
}
