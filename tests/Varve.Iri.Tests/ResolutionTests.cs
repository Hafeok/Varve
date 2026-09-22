// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Xunit;

namespace Varve.Iri.Tests;

/// <summary>
/// RFC 3986 §5.4, every vector, against the base the RFC uses.
/// </summary>
/// <remarks>
/// The abnormal examples are not optional. Four of them carry most of the
/// weight: <c>../../../g</c> and <c>../../../../g</c> must not climb above the
/// root, and <c>g?y/../x</c> and <c>g#s/../x</c> must not apply
/// <c>remove_dot_segments</c> to a query or a fragment.
/// </remarks>
public class ResolutionTests
{
    private const string Base = "http://a/b/c/d;p?q";

    [Theory]
    // §5.4.1 normal examples
    [InlineData("g:h", "g:h")]
    [InlineData("g", "http://a/b/c/g")]
    [InlineData("./g", "http://a/b/c/g")]
    [InlineData("g/", "http://a/b/c/g/")]
    [InlineData("/g", "http://a/g")]
    [InlineData("//g", "http://g")]
    [InlineData("?y", "http://a/b/c/d;p?y")]
    [InlineData("g?y", "http://a/b/c/g?y")]
    [InlineData("#s", "http://a/b/c/d;p?q#s")]
    [InlineData("g#s", "http://a/b/c/g#s")]
    [InlineData("g?y#s", "http://a/b/c/g?y#s")]
    [InlineData(";x", "http://a/b/c/;x")]
    [InlineData("g;x", "http://a/b/c/g;x")]
    [InlineData("g;x?y#s", "http://a/b/c/g;x?y#s")]
    [InlineData("", "http://a/b/c/d;p?q")]
    [InlineData(".", "http://a/b/c/")]
    [InlineData("./", "http://a/b/c/")]
    [InlineData("..", "http://a/b/")]
    [InlineData("../", "http://a/b/")]
    [InlineData("../g", "http://a/b/g")]
    [InlineData("../..", "http://a/")]
    [InlineData("../../", "http://a/")]
    [InlineData("../../g", "http://a/g")]
    // §5.4.2 abnormal examples
    [InlineData("../../../g", "http://a/g")]
    [InlineData("../../../../g", "http://a/g")]
    [InlineData("/./g", "http://a/g")]
    [InlineData("/../g", "http://a/g")]
    [InlineData("g.", "http://a/b/c/g.")]
    [InlineData(".g", "http://a/b/c/.g")]
    [InlineData("g..", "http://a/b/c/g..")]
    [InlineData("..g", "http://a/b/c/..g")]
    [InlineData("./../g", "http://a/b/g")]
    [InlineData("./g/.", "http://a/b/c/g/")]
    [InlineData("g/./h", "http://a/b/c/g/h")]
    [InlineData("g/../h", "http://a/b/c/h")]
    [InlineData("g;x=1/./y", "http://a/b/c/g;x=1/y")]
    [InlineData("g;x=1/../y", "http://a/b/c/y")]
    [InlineData("g?y/./x", "http://a/b/c/g?y/./x")]
    [InlineData("g?y/../x", "http://a/b/c/g?y/../x")]
    [InlineData("g#s/./x", "http://a/b/c/g#s/./x")]
    [InlineData("g#s/../x", "http://a/b/c/g#s/../x")]
    // Strict resolution. RFC 3986 §5.2.2's non-strict mode would give
    // "http://a/b/c/g"; it exists for pre-RFC-2396 parsers.
    [InlineData("http:g", "http:g")]
    public void Resolves_as_RFC_3986_section_5_4_says(string reference, string expected)
    {
        Assert.Equal(expected, Resolve(Base, reference));
    }

    [Fact]
    public void Reports_the_required_length_when_the_destination_is_too_small()
    {
        byte[] baseIri = Encoding.UTF8.GetBytes(Base);
        byte[] reference = Encoding.UTF8.GetBytes("../../g");
        Span<byte> tooSmall = stackalloc byte[4];

        Assert.False(IriRef.TryResolve(baseIri, reference, tooSmall, out int written));
        Assert.Equal("http://a/g".Length, written);
        Assert.Equal("http://a/g".Length, IriRef.ResolveLength(baseIri, reference));
    }

    [Fact]
    public void Refuses_a_relative_base()
    {
        byte[] baseIri = "b/c"u8.ToArray();
        byte[] reference = "g"u8.ToArray();

        Assert.False(IriRef.TryResolve(baseIri, reference, stackalloc byte[64], out int written));
        Assert.Equal(0, written);
        Assert.Equal(-1, IriRef.ResolveLength(baseIri, reference));
    }

    [Fact]
    public void Merges_onto_an_empty_base_path()
    {
        // RFC 3986 §5.2.3: with an authority and an empty path, merge yields "/".
        Assert.Equal("http://a/g", Resolve("http://a", "g"));
    }

    private static string Resolve(string baseIri, string reference)
    {
        byte[] b = Encoding.UTF8.GetBytes(baseIri);
        byte[] r = Encoding.UTF8.GetBytes(reference);

        int length = IriRef.ResolveLength(b, r);
        Assert.True(length >= 0, $"'{reference}' against '{baseIri}' was rejected outright.");

        byte[] destination = new byte[length];
        Assert.True(IriRef.TryResolve(b, r, destination, out int written));
        Assert.Equal(length, written);

        return Encoding.UTF8.GetString(destination);
    }
}
