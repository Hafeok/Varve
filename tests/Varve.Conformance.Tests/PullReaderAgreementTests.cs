using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The pull readers answer every manifest input exactly as the push parsers do.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TurtleReader"/> and <see cref="NQuadsReader"/> are a second way
/// into the same grammar, and a second way in is a second thing that can be
/// wrong. Rather than write a separate suite for them, this runs the one that
/// already exists: every wired case, push and pull, and the two must agree on
/// the quads and on the rejection.
/// </para>
/// <para>
/// It is the same argument as the chunk-boundary oracle's — the expected value
/// is computed from the other path rather than authored — and it costs one
/// parse per case rather than a suite of hand-written examples that would
/// cover less.
/// </para>
/// </remarks>
public class PullReaderAgreementTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ManifestEntry entry in Catalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_pull_reader_answers_as_the_push_parser_does(string testIri)
    {
        ManifestEntry entry = Catalogue.ByIri[testIri];
        byte[] bytes = File.ReadAllBytes(entry.ActionPath);

        (bool pushOk, string pushError, List<string> pushQuads) = Push(entry, bytes);
        (bool pullOk, string pullError, List<string> pullQuads) = Pull(entry, bytes);

        Assert.Equal(pushOk, pullOk);
        Assert.Equal(pushQuads, pullQuads);

        if (!pushOk)
        {
            Assert.Equal(pushError, pullError);
        }
    }

    private static (bool Ok, string Error, List<string> Quads) Push(ManifestEntry entry, byte[] bytes)
    {
        List<string> quads = [];
        QuadHandler handler = (in QuadView quad) => quads.Add(Render(quad));

        ParseResult result = IsLineBased(entry.Format)
            ? NQuadsParser.Parse(bytes, handler, LineOptions(entry.Format))
            : TurtleParser.Parse(bytes, handler, TurtleOptionsFor(entry));

        return (result.Succeeded, result.FirstError.ToString(), quads);
    }

    private static (bool Ok, string Error, List<string> Quads) Pull(ManifestEntry entry, byte[] bytes)
    {
        List<string> quads = [];

        if (IsLineBased(entry.Format))
        {
            NQuadsReader reader = new(bytes, LineOptions(entry.Format));

            while (reader.Read())
            {
                quads.Add(Render(reader.Current));
            }

            return (reader.Result.Succeeded, reader.Result.FirstError.ToString(), quads);
        }

        TurtleReader turtle = new(bytes, TurtleOptionsFor(entry));

        while (turtle.Read())
        {
            quads.Add(Render(turtle.Current));
        }

        return (turtle.Result.Succeeded, turtle.Result.FirstError.ToString(), quads);
    }

    private static bool IsLineBased(RdfFormat format) => format is RdfFormat.NTriples or RdfFormat.NQuads;

    private static ParseOptions LineOptions(RdfFormat format) =>
        new() { Syntax = format == RdfFormat.NQuads ? RdfSyntax.NQuads : RdfSyntax.NTriples };

    private static TurtleOptions TurtleOptionsFor(ManifestEntry entry) => new()
    {
        Syntax = entry.Format == RdfFormat.TriG ? RdfSyntax.TriG : RdfSyntax.Turtle,
        BaseIri = Encoding.UTF8.GetBytes(entry.ActionIri),
    };

    /// <summary>One quad as canonical N-Quads, which is comparable as a string.</summary>
    private static string Render(QuadView quad)
    {
        StringBuilder text = new();
        text.Append(Term(quad.Subject)).Append(' ');
        text.Append(Term(quad.Predicate)).Append(' ');
        text.Append(Term(quad.Object));

        if (quad.HasGraph)
        {
            text.Append(' ').Append(Term(quad.Graph));
        }

        return text.ToString();
    }

    private static string Term(RdfTermView view)
    {
        WriteOptions options = new() { Syntax = RdfSyntax.NQuads };
        RdfTerm term = view.Materialise();
        byte[] buffer = new byte[256];

        while (!NQuadsWriter.TryWriteTerm(term, buffer, out _, in options))
        {
            buffer = new byte[buffer.Length * 2];
        }

        NQuadsWriter.TryWriteTerm(term, buffer, out int written, in options);
        return Encoding.UTF8.GetString(buffer, 0, written);
    }
}
