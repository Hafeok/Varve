// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;
using Varve.Turtle.Model;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Management;

/// <summary>Declarations as TriG, read and written (ADR 0163 point 4).</summary>
[Contract(typeof(TheManagementPlane.DeclarationsAreTrig), Role = "declarations read from and written as TriG")]
public static class ManagementTrig
{
    /// <summary>
    /// Parses TriG into a dataset, or says why it does not parse: the first
    /// error, by line and column.
    /// </summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static InMemoryDataset? TryParse(ReadOnlySpan<byte> utf8, string? baseIri, out string? error)
    {
        InMemoryDatasetBuilder builder = new();
        string? failure = null;

        void Add(in QuadView quad) =>
            builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), quad.HasGraph ? quad.Graph.Materialise() : null);

        ErrorAction OnError(in ParseError parse)
        {
            failure ??= parse.Kind.ToString() + " at line " + parse.Position.Line.ToString(CultureInfo.InvariantCulture)
                + ", column " + parse.Position.Column.ToString(CultureInfo.InvariantCulture);
            return ErrorAction.Stop;
        }

        TurtleOptions options = new() { Syntax = RdfSyntax.TriG, BaseIri = Encoding.UTF8.GetBytes(baseIri ?? string.Empty), OnError = OnError };

        if (!TurtleParser.Parse(utf8, Add, in options).Succeeded || failure is not null)
        {
            error = failure ?? "The TriG does not parse.";
            return null;
        }

        error = null;
        return builder.ToDataset();
    }

    /// <summary>
    /// Parses canonical N-Quads lines, a saved plan's content, into a dataset,
    /// or says why they do not parse.
    /// </summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static InMemoryDataset? TryParseLines(IEnumerable<string> lines, out string? error)
    {
        ArgumentNullException.ThrowIfNull(lines);
        InMemoryDatasetBuilder builder = new();
        string? failure = null;

        void Add(in QuadView quad) =>
            builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), quad.HasGraph ? quad.Graph.Materialise() : null);

        ErrorAction OnError(in ParseError parse)
        {
            failure ??= parse.Kind.ToString() + " at line " + parse.Position.Line.ToString(CultureInfo.InvariantCulture);
            return ErrorAction.Stop;
        }

        byte[] text = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
        ParseOptions options = new() { Syntax = RdfSyntax.NQuads, OnError = OnError };

        if (!NQuadsParser.Parse(text, Add, in options).Succeeded || failure is not null)
        {
            error = failure ?? "The lines do not parse as N-Quads.";
            return null;
        }

        error = null;
        return builder.ToDataset();
    }

    /// <summary>The quads of <paramref name="graphs"/> in <paramref name="source"/>, as TriG.</summary>
    [DesignDecision(typeof(VarveManagementAndTheVocabulary.ManagementSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static byte[] Write(IQuadSource source, IEnumerable<RdfTerm> graphs)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(graphs);
        ArrayBufferWriter<byte> output = new();
        TurtleWriteOptions options = new() { Syntax = RdfSyntax.TriG };

        using (TurtleWriter writer = new(output, in options))
        {
            writer.DeclarePrefix("varve"u8, Encoding.UTF8.GetBytes(ManagementVocabulary.Namespace));

            foreach (RdfTerm graph in graphs)
            {
                if (!source.TryInternalise(graph, out TermHandle handle))
                {
                    continue;
                }

                using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(handle));

                while (cursor.MoveNext())
                {
                    Quad quad = cursor.Current;
                    writer.Write(in quad, source);
                }
            }

            writer.Flush();
        }

        return output.WrittenSpan.ToArray();
    }
}
