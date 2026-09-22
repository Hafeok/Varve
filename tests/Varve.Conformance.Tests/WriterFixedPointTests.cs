using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// Writing, reading back and writing again produces the same bytes.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline of stages reads and writes the same graph repeatedly, so a
/// transformation that renames a blank node on every pass renames every node
/// in the graph every time — the diff between two stages is then noise rather
/// than the change. The property that stops that is a fixed point: whatever
/// the first write produces, a second one reproduces exactly.
/// </para>
/// <para>
/// It is the writer's half of the naming contract in <c>turtle.md</c> §4, and
/// it holds because the writer emits every blank node as an explicit label and
/// a document's own label passes through the reader unchanged. It does not
/// claim the <em>first</em> write preserves the input's labels: a document that
/// uses generated-form labels beside anonymous nodes may have some renamed
/// once, which §4 states and this measures by starting from the first write
/// rather than from the file.
/// </para>
/// <para>
/// Unlike the chunk-boundary oracle this goes at <c>Varve.Turtle</c> directly
/// rather than through <see cref="IParserSubject"/>: it is a property of this
/// writer, not a question one could ask of any implementation.
/// </para>
/// </remarks>
public class WriterFixedPointTests
{
    /// <summary>
    /// Declared for both writes, so that byte-identity is a real comparison and
    /// prefix compaction is exercised rather than avoided.
    /// </summary>
    private static readonly (string Prefix, string Iri)[] Prefixes =
    [
        ("e", "http://example/"),
        ("a", "http://a.example/"),
        ("x", "http://www.w3.org/2001/XMLSchema#"),
    ];

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ManifestEntry entry in OracleCatalogue.Entries)
        {
            if (entry.Expected != ExpectedOutcome.IsRejected)
            {
                yield return new TheoryDataRow<string>(entry.TestIri)
                {
                    TestDisplayName = entry.TestIri,
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void writing_what_was_written_reproduces_it(string testIri)
    {
        ManifestEntry entry = OracleCatalogue.ByIri[testIri];
        RdfSyntax syntax = Syntax(entry.Format);

        byte[] source = File.ReadAllBytes(entry.ActionPath);

        if (!TryWrite(source, syntax, out byte[] first))
        {
            // A positive-syntax entry this reader rejects is a conformance
            // failure the suites report; it is not this property's business.
            return;
        }

        Assert.True(TryWrite(first, syntax, out byte[] second), Text(first));
        Assert.Equal(Text(first), Text(second));
    }

    /// <summary>
    /// The syntax to read and write each format as. N-Triples is a subset of
    /// Turtle and N-Quads of TriG, so their inputs are corpus for this property
    /// too rather than a separate path.
    /// </summary>
    private static RdfSyntax Syntax(RdfFormat format) => format switch
    {
        RdfFormat.TriG or RdfFormat.NQuads => RdfSyntax.TriG,
        _ => RdfSyntax.Turtle,
    };

    /// <summary>
    /// Parses <paramref name="source"/> and writes it out, or reports that it
    /// was rejected.
    /// </summary>
    private static bool TryWrite(byte[] source, RdfSyntax syntax, out byte[] written)
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions writeOptions = new() { Syntax = syntax };
        ParseResult result;

        using (TurtleWriter writer = new(output, in writeOptions))
        {
            foreach ((string prefix, string iri) in Prefixes)
            {
                writer.DeclarePrefix(Encoding.UTF8.GetBytes(prefix), Encoding.UTF8.GetBytes(iri));
            }

            TurtleOptions readOptions = new()
            {
                Syntax = syntax == RdfSyntax.TriG ? RdfSyntax.TriG : RdfSyntax.Turtle,
                BaseIri = Encoding.UTF8.GetBytes("http://example/base"),
            };

            result = TurtleParser.Parse(
                source, (in QuadView quad) => writer.Write(in quad), in readOptions);
        }

        written = output.Written.ToArray();
        return result.Succeeded;
    }

    private static string Text(byte[] utf8) => Encoding.UTF8.GetString(utf8);
}
