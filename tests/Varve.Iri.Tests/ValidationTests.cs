using System;
using System.Text;
using Xunit;

namespace Varve.Iri.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("http://example/s")]
    [InlineData("http://example.com:8080/a/b?q=1#f")]
    [InlineData("urn:isbn:0451450523")]
    [InlineData("mailto:a@example.com")]
    [InlineData("file:///etc/hosts")]
    [InlineData("http://user:pw@example.com/")]
    [InlineData("http://[2001:db8::1]:80/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:192.0.2.1]/")]
    [InlineData("http://[v7.host]/")]
    [InlineData("http://example/%20")]
    [InlineData("scheme:")]
    // The N-Triples suite's nt-syntax-uri-04: every character class at once.
    [InlineData("scheme:!$%25&'()*+,-./0123456789:/@ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz~?#")]
    public void Accepts_an_absolute_iri(string iri)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(iri);

        Assert.True(IriRef.TryValidate(utf8, out IriComponents components, out IriError error), error.Kind.ToString());
        Assert.True(components.HasScheme);
        Assert.True(IriRef.IsAbsolute(utf8));
    }

    [Theory]
    // The four relative references the N-Triples suite rejects
    // (nt-syntax-bad-uri-06 through -09).
    [InlineData("s")]
    [InlineData("p")]
    [InlineData("o")]
    [InlineData("dt")]
    [InlineData("//example/a")]
    [InlineData("/absolute/path")]
    [InlineData("?query")]
    [InlineData("#fragment")]
    [InlineData("")]
    public void Well_formed_but_not_absolute(string reference)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(reference);

        Assert.True(IriRef.TryValidate(utf8, out IriComponents components, out _));
        Assert.False(components.HasScheme);
        Assert.False(IriRef.IsAbsolute(utf8));
    }

    [Theory]
    // nt-syntax-bad-uri-01: a space. The IRIREF production excludes it too, so
    // the parser catches this twice over — deliberately.
    [InlineData("http://example/ space", IriErrorKind.InvalidCharacter)]
    [InlineData("http://example/a\u0000b", IriErrorKind.InvalidCharacter)]
    [InlineData("http://example/<", IriErrorKind.InvalidCharacter)]
    [InlineData("http://example/\"", IriErrorKind.InvalidCharacter)]
    [InlineData("http://example/{}", IriErrorKind.InvalidCharacter)]
    [InlineData("http://example/%2", IriErrorKind.UnexpectedEnd)]
    [InlineData("http://example/%ZZ", IriErrorKind.InvalidPercentEncoding)]
    [InlineData("http://example:80a/", IriErrorKind.InvalidPort)]
    [InlineData("http://[2001:db8::1/", IriErrorKind.UnterminatedIpLiteral)]
    // A colon in the first segment of a relative reference is path-noscheme.
    [InlineData("1a:b", IriErrorKind.InvalidCharacter)]
    public void Rejects(string iri, IriErrorKind expected)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(iri);

        Assert.False(IriRef.TryValidate(utf8, out _, out IriError error));
        Assert.Equal(expected, error.Kind);
        Assert.True(error.IsError);
    }

    [Fact]
    public void Rejects_ill_formed_utf8_as_its_own_kind()
    {
        // A lone continuation byte. Not an invalid character — invalid UTF-8,
        // which has a different cause and a different fix.
        byte[] utf8 = [.. "http://example/"u8, 0x80];

        Assert.False(IriRef.TryValidate(utf8, out _, out IriError error));
        Assert.Equal(IriErrorKind.InvalidUtf8, error.Kind);
    }

    [Fact]
    public void Accepts_ucschar_and_confines_iprivate_to_the_query()
    {
        // U+00E9 is ucschar and allowed anywhere a character is.
        Assert.True(IriRef.TryValidate(Encoding.UTF8.GetBytes("http://example/é"), out _, out _));

        // U+E000 is iprivate: permitted in a query, and nowhere else.
        Assert.True(IriRef.TryValidate(Encoding.UTF8.GetBytes("http://example/?"), out _, out _));
        Assert.False(IriRef.TryValidate(Encoding.UTF8.GetBytes("http://example/"), out _, out _));
        Assert.False(IriRef.TryValidate(Encoding.UTF8.GetBytes("http://example/#"), out _, out _));
    }

    [Theory]
    [InlineData("http://[1:2:3:4:5:6:7:8]/")]
    [InlineData("http://[1:2:3:4:5:6:1.2.3.4]/")]
    [InlineData("http://[::]/")]
    [InlineData("http://[1::8]/")]
    public void Accepts_well_formed_ip_literals(string iri) =>
        Assert.True(IriRef.TryValidate(Encoding.UTF8.GetBytes(iri), out _, out _));

    [Theory]
    // Nine groups, all of them valid hexadecimal: a character-class check would
    // let this through, which is why the literal is validated structurally.
    [InlineData("http://[1:2:3:4:5:6:7:8:9]/")]
    [InlineData("http://[1::2::3]/")]
    [InlineData("http://[12345::1]/")]
    [InlineData("http://[1:2:3:4:5:6:7]/")]
    [InlineData("http://[1.2.3.4:5:6:7:8:9:10]/")]
    [InlineData("http://[v.host]/")]
    public void Rejects_malformed_ip_literals(string iri) =>
        Assert.False(IriRef.TryValidate(Encoding.UTF8.GetBytes(iri), out _, out _));

    [Fact]
    public void Reports_components_as_ranges_into_the_input()
    {
        const string Iri = "http://user:pw@example.com:8080/a/b?q=1#f";
        byte[] utf8 = Encoding.UTF8.GetBytes(Iri);

        Assert.True(IriRef.TryValidate(utf8, out IriComponents c, out _));

        Assert.Equal("http", Slice(utf8, c.Scheme));
        Assert.Equal("user:pw@example.com:8080", Slice(utf8, c.Authority));
        Assert.Equal("/a/b", Slice(utf8, c.Path));
        Assert.Equal("q=1", Slice(utf8, c.Query));
        Assert.Equal("f", Slice(utf8, c.Fragment));
        Assert.True(c.HasScheme && c.HasAuthority && c.HasQuery && c.HasFragment);
    }

    [Fact]
    public void An_absent_component_is_not_an_empty_one()
    {
        // http://a and http://a? are different IRIs, and a zero-length range
        // cannot tell them apart.
        Assert.True(IriRef.TryValidate("http://a"u8, out IriComponents without, out _));
        Assert.False(without.HasQuery);

        Assert.True(IriRef.TryValidate("http://a?"u8, out IriComponents with, out _));
        Assert.True(with.HasQuery);
        Assert.Equal(0, with.Query.End.Value - with.Query.Start.Value);
    }

    private static string Slice(byte[] utf8, Range range) =>
        Encoding.UTF8.GetString(utf8.AsSpan()[range]);
}
