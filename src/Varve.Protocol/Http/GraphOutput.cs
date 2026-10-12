// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Sparql.Evaluation;
using Varve.Turtle;

namespace Varve.Protocol.Http;

/// <summary>
/// A graph the negotiation chose to write (ADR 0092, amended 2026-10-12)
/// cannot be written in that format: RDF/XML names a predicate with no
/// NCName suffix, or JSON-LD meets a triple term. Answered as
/// <c>not-acceptable</c>: before the first byte a <c>406</c> problem, after
/// it the cut every bounded read takes.
/// </summary>
internal sealed class GraphNotExpressibleException : Exception
{
    public GraphNotExpressibleException()
        : base("The graph cannot be written in the format Accept chose.")
    {
    }

    public GraphNotExpressibleException(string message)
        : base(message)
    {
    }

    public GraphNotExpressibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Writes a graph response in its negotiated format. The four syntaxes of
/// <c>Varve.Turtle</c> take canonical N-Triples lines, which are each of
/// them, RDF 1.2 terms included; RDF/XML streams through
/// <see cref="RdfXmlWriter"/>, which declares <c>rdf:version="1.2"</c> where
/// it writes a triple term or a direction (ADR 0122); JSON-LD buffers the
/// graph in <see cref="JsonLdWriter"/> until the last triple (ADR 0123,
/// FromRdfBuffersTheDataset), charged against <c>Limits:MaxQueryMemory</c>
/// as it grows (ADR 0114), and writes directions natively.
/// </summary>
/// <remarks>
/// Only <see cref="Complete"/> finishes the document. A response cut part
/// way is abandoned rather than closed, so a cut read is never a
/// whole-looking document (ADR 0095): <see cref="Dispose"/> after a cut
/// writes nothing, and the JSON-LD writer's pooled buffers go to the
/// collector rather than serialise a graph nobody will read.
/// </remarks>
internal sealed class GraphOutput : IDisposable
{
    // What a buffered triple costs JSON-LD beyond its terms' bytes: four
    // indices into the writer's term table.
    private const long JsonLdTripleCost = 4 * sizeof(int);

    private static readonly WriteOptions Lines = new() { Syntax = RdfSyntax.NTriples };

    private readonly ResponseOutput _output;
    private readonly GraphFormat _format;
    private readonly RdfXmlWriter? _xml;
    private readonly JsonLdWriter? _json;
    private readonly TermArena? _arena;
    private readonly MemoryBytes? _budget;
    private long _charged;
    private bool _completed;
    private bool _abandoned;

    internal GraphOutput(ResponseOutput output, GraphFormat format, ProtocolLimits limits)
    {
        _output = output;
        _format = format;

        if (format == GraphFormat.RdfXml)
        {
            _xml = new RdfXmlWriter(output, default);
            _arena = new TermArena();
        }
        else if (format == GraphFormat.JsonLd)
        {
            _json = new JsonLdWriter(output, default);
            _arena = new TermArena();
            _budget = limits.MaxQueryMemory;
        }
    }

    /// <summary>One triple of terms from outside a store: a CONSTRUCT row, a line of the service description.</summary>
    internal void Write(RdfTerm subject, RdfTerm predicate, RdfTerm @object)
    {
        if (_arena is null)
        {
            TermLines.WriteTriple(_output, subject, predicate, @object);
            return;
        }

        _arena.Reset();
        int s = Add(_arena, subject);
        int p = Add(_arena, predicate);
        int o = Add(_arena, @object);
        QuadView quad = _arena.Quad(default, s, p, o, -1);
        Write(in quad, subject.Lexical.Length + predicate.Lexical.Length + @object.Lexical.Length);
    }

    /// <summary>One triple of a graph, its terms by handle in <paramref name="source"/>.</summary>
    internal void Write(in Quad quad, IQuadSource source)
    {
        Quad triple = new(quad.Subject, quad.Predicate, quad.Object);

        switch (_format)
        {
            case GraphFormat.RdfXml:
                try
                {
                    _xml!.Write(in triple, source);
                }
                catch (InvalidOperationException refused)
                {
                    throw Refused(refused);
                }

                break;
            case GraphFormat.JsonLd:
                Write(Externalise(source, quad.Subject), Externalise(source, quad.Predicate), Externalise(source, quad.Object));
                break;
            default:
                NQuadsWriter.Write(_output, in triple, source, in Lines);
                break;
        }
    }

    /// <summary>Finishes the document: closes RDF/XML's root, or writes the JSON-LD the triples made.</summary>
    internal void Complete()
    {
        ObjectDisposedException.ThrowIf(_abandoned, this);
        _completed = true;

        if (_xml is not null)
        {
            _xml.Dispose();
        }
        else if (_json is not null)
        {
            try
            {
                _json.Dispose();
            }
            catch (InvalidOperationException refused)
            {
                throw Refused(refused);
            }
        }
    }

    /// <summary>
    /// Writes nothing. After <see cref="Complete"/> the writers are done;
    /// after a cut the document is abandoned unwritten, deliberately: closing
    /// it would make a cut read look whole.
    /// </summary>
    public void Dispose() => _abandoned = !_completed;

    private void Write(in QuadView quad, int bytes)
    {
        try
        {
            if (_json is not null)
            {
                Charge(bytes);
                _json.Write(in quad);
            }
            else
            {
                _xml!.Write(in quad);
            }
        }
        catch (InvalidOperationException refused)
        {
            throw Refused(refused);
        }
    }

    private void Charge(int bytes)
    {
        _charged += bytes + JsonLdTripleCost;

        if (_budget is { } budget && _charged > budget.Bytes)
        {
            throw new MemoryBudgetExceededException(budget, new MemoryBytes(_charged));
        }
    }

    // The writers refuse what their syntax cannot spell with
    // InvalidOperationException, naming it; that is the client's format
    // choice meeting this graph, not a server fault.
    private static GraphNotExpressibleException Refused(InvalidOperationException refused) =>
        new(refused.Message, refused);

    private static RdfTerm Externalise(IQuadSource source, TermHandle handle) =>
        source.TryExternalise(handle, out RdfTerm? term)
            ? term
            : throw new InvalidOperationException("The quad source cannot externalise a term of this graph.");

    private static int Add(TermArena arena, RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                return arena.AddIri(arena.AppendScratch(term.Lexical));
            case RdfTermKind.BlankNode:
                return arena.AddBlankNode(arena.AppendScratch(term.Lexical));
            case RdfTermKind.Literal:
                TermSpan lexical = arena.AppendScratch(term.Lexical);

                if (!term.Language.IsEmpty)
                {
                    return arena.AddLiteral(lexical, TermSpan.None, arena.AppendScratch(term.Language), term.Direction);
                }

                return term.DatatypeIri.IsEmpty || term.DatatypeIri.SequenceEqual(RdfVocabulary.XsdString)
                    ? arena.AddLiteral(lexical, TermSpan.None, TermSpan.None, TextDirection.None)
                    : arena.AddLiteral(lexical, arena.AppendScratch(term.DatatypeIri), TermSpan.None, TextDirection.None);
            default:
                int s = Add(arena, term.Subject!);
                int p = Add(arena, term.Predicate!);
                int o = Add(arena, term.Object!);
                return arena.AddTripleTerm(s, p, o);
        }
    }
}
