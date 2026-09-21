using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>Varve.Turtle</c> as the harness needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// The adapter wraps the real API rather than the real API being shaped to
/// suit the harness, which is what <see cref="IParserSubject"/> says it is for.
/// It parses from a byte array — the whole file in memory — because that is
/// what a syntax test is: one small document, accepted or rejected. The
/// streaming entry points are exercised by <c>Varve.Turtle.Tests</c>, where a
/// test can control how the input is fragmented.
/// </para>
/// <para>
/// Quads are collected even though no wired suite reads them. They cost one
/// materialisation per quad on files of a few lines, and they mean a failure
/// report can say what was parsed rather than only that something was.
/// </para>
/// </remarks>
internal sealed class VarveParserSubject : IParserSubject
{
    [ModuleInitializer]
    internal static void Register() => ParserSubjects.Current = new VarveParserSubject();

    public ParseOutcome Parse(RdfFormat format, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        List<string> quads = [];
        WriteOptions writeOptions = new() { Syntax = RdfSyntax.NQuads };
        ArrayBufferWriter output = new();

        ParseOptions options = new()
        {
            Syntax = format == RdfFormat.NQuads ? RdfSyntax.NQuads : RdfSyntax.NTriples,
        };

        ParseResult result = NQuadsParser.Parse(
            bytes,
            (in QuadView quad) =>
            {
                output.Reset();
                NQuadsWriter.Write(output, in quad, writeOptions);
                quads.Add(Encoding.UTF8.GetString(output.Written).TrimEnd('\n'));
            },
            options);

        return result.Succeeded
            ? ParseOutcome.Parsed(quads)
            : ParseOutcome.Rejected(result.FirstError.ToString());
    }
}
