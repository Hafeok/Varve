// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.JsonLd.Json;
using Varve.JsonLd.Model;
using Varve.JsonLd.Processing;

namespace Varve.JsonLd;

/// <summary>
/// Reads a JSON-LD 1.1 document as RDF: expansion (JSON-LD 1.1 API §5.1) and
/// deserialization to RDF (§8.3), the <c>toRdf</c> of the specification's
/// API, each quad handed to the caller as a view (ADR 0112).
/// </summary>
/// <remarks>
/// <para>
/// The document is processed as a whole: expansion needs every context before
/// the first triple is known, so nothing is emitted before the last byte is
/// read, whichever entry point is used. A split <see cref="ReadOnlySequence{T}"/>
/// is read by <c>Utf8JsonReader</c> across its boundaries.
/// </para>
/// <para>
/// The first error ends the processing with the specification's error code
/// (<see cref="JsonLdResult.Error"/>); the quads handed out before it stand.
/// </para>
/// </remarks>
public static class JsonLdParser
{
    /// <summary>Reads a document held in memory.</summary>
    public static JsonLdResult Parse(ReadOnlyMemory<byte> utf8, JsonLdQuadHandler handler, in JsonLdOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Processor.Run(new ReadOnlySequence<byte>(utf8), in options, handler, null);
    }

    /// <summary>Reads a document held in a sequence of segments.</summary>
    public static JsonLdResult Parse(in ReadOnlySequence<byte> utf8, JsonLdQuadHandler handler, in JsonLdOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Processor.Run(in utf8, in options, handler, null);
    }

    /// <summary>Reads a document from a stream, whole, before processing it (ADR 0112, <c>TheWholeStreamIsRead</c>).</summary>
    public static JsonLdResult Parse(Stream stream, JsonLdQuadHandler handler, in JsonLdOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(handler);
        return Processor.RunStream(stream, in options, handler, null);
    }
}

/// <summary>Expands a JSON-LD 1.1 document (JSON-LD 1.1 API §5.1), writing the expanded document as JSON.</summary>
public static class JsonLdExpander
{
    /// <summary>Expands a document held in memory into <paramref name="output"/>.</summary>
    public static JsonLdResult Expand(ReadOnlyMemory<byte> utf8, IBufferWriter<byte> output, in JsonLdOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Processor.Run(new ReadOnlySequence<byte>(utf8), in options, null, output);
    }

    /// <summary>Expands a document held in a sequence of segments into <paramref name="output"/>.</summary>
    public static JsonLdResult Expand(in ReadOnlySequence<byte> utf8, IBufferWriter<byte> output, in JsonLdOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Processor.Run(in utf8, in options, null, output);
    }

    /// <summary>Expands a document read whole from a stream into <paramref name="output"/>.</summary>
    public static JsonLdResult Expand(Stream stream, IBufferWriter<byte> output, in JsonLdOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(output);
        return Processor.RunStream(stream, in options, null, output);
    }
}

/// <summary>
/// The processor: one name table, one tree, the context processor, the
/// expander and the emitter, kept per thread and reset per document so that a
/// steady state of documents allocates only what grows.
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.Utf8JsonReaderBuildsTheTree), Scope = ExceptionScope.Boundary)]
internal sealed class Processor
{
    [ThreadStatic]
    [DesignDecision(typeof(JsonLdOverUtf8Json.TheProcessorIsKeptPerThread), Scope = ExceptionScope.Pool)]
    private static Processor? cached;

    private readonly NameTable _names = new();
    private readonly JsonTree _tree;

    private Processor()
    {
        _tree = new JsonTree(_names);
    }

    internal static JsonLdResult Run(in ReadOnlySequence<byte> utf8, in JsonLdOptions options, JsonLdQuadHandler? handler, IBufferWriter<byte>? output)
    {
        Processor processor = cached ?? new Processor();
        cached = null;

        try
        {
            return processor.Process(in utf8, in options, handler, output);
        }
        finally
        {
            processor._tree.Reset();
            processor._names.Reset();
            cached = processor;
        }
    }

    [DesignDecision(typeof(JsonLdOverUtf8Json.TheWholeStreamIsRead), Scope = ExceptionScope.Boundary)]
    internal static JsonLdResult RunStream(Stream stream, in JsonLdOptions options, JsonLdQuadHandler? handler, IBufferWriter<byte>? output)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16384);
        int length = 0;

        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    byte[] bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, length).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }

                int read = stream.Read(buffer, length, buffer.Length - length);

                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            return Run(new ReadOnlySequence<byte>(buffer, 0, length), in options, handler, output);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private JsonLdResult Process(in ReadOnlySequence<byte> utf8, in JsonLdOptions options, JsonLdQuadHandler? handler, IBufferWriter<byte>? output)
    {
        ContextProcessor contexts = new(_tree, options.DocumentLoader);
        Expander expander = new(_tree, contexts);
        RdfEmitter? emitter = handler is null ? null : new RdfEmitter(_tree, contexts);

        try
        {
            TextRange baseIri = options.BaseIri.IsEmpty ? TextRange.None : _tree.AddText(options.BaseIri.Span);
            ActiveContext active = new() { Base = baseIri, OriginalBase = baseIri };

            // The expandContext option: a document holding @context, or a
            // context value itself.
            if (!options.ExpandContext.IsEmpty)
            {
                int expandContext = JsonTreeReader.Read(options.ExpandContext.Span, _tree, JsonLdErrorCode.InvalidLocalContext);

                if (_tree.IsObject(expandContext) && _tree.HasMember(expandContext, Keyword.Context))
                {
                    expandContext = _tree.Member(expandContext, Keyword.Context);
                }

                TextRange contextBase = options.ExpandContextIri.IsEmpty ? baseIri : _tree.AddText(options.ExpandContextIri.Span);
                active = contexts.Process(active, expandContext, contextBase);
            }

            int root = JsonTreeReader.Read(in utf8, _tree, JsonLdErrorCode.LoadingDocumentFailed);
            int expanded = expander.Expand(active, -1, root, baseIri);

            // §5.1.1 steps 8–9: a lone @graph is its value; null is []; a
            // single value is wrapped.
            if (expanded >= 0 && _tree.IsObject(expanded) && _tree.HasOnly(expanded, Keyword.Graph))
            {
                expanded = _tree.Member(expanded, Keyword.Graph);
            }

            if (expanded < 0)
            {
                expanded = _tree.AddArray();
            }
            else if (!_tree.IsArray(expanded))
            {
                expanded = _tree.AddArrayOf(expanded);
            }

            if (output is not null)
            {
                JsonTreeWriter.Write(output, _tree, expanded, options.Indent);
            }

            if (emitter is not null)
            {
                emitter.Emit(expanded, handler!, options.RdfDirection);
                return new JsonLdResult(emitter.QuadCount, null);
            }

            return new JsonLdResult(0, null);
        }
        catch (JsonLdException exception)
        {
            return new JsonLdResult(emitter?.QuadCount ?? 0, exception.ToError());
        }
    }
}
