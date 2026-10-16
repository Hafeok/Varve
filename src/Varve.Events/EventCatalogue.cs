// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Events;

/// <summary>
/// The catalogue graphs' names (ADR 0144, docs/spec/events.md §3.1): the
/// catalogue of a source graph G is
/// <c>https://w3id.org/varve/ns#events-catalogue/{enc(G)}</c>, where enc(G) is
/// <c>default</c> for the default graph and otherwise G's IRI percent-encoded as
/// one segment, as <c>varve:setup/migration/{id}</c> is. The functions here are
/// the rule a host composes into a caller's scope: a catalogue graph is
/// governed by its source graph.
/// </summary>
[Contract(typeof(TheEventCatalogue.CatalogueGraphPerSourceGraph), Role = "the catalogue graphs' names and the governing rule")]
public static class EventCatalogue
{
    /// <summary>The prefix every catalogue graph's IRI starts with.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string Namespace = "https://w3id.org/varve/ns#events-catalogue/";

    private const string DefaultSegment = "default";

    /// <summary>The catalogue graph of <paramref name="sourceGraph"/>, <see langword="null"/> naming the default graph.</summary>
    public static RdfTerm GraphOf(RdfTerm? sourceGraph)
    {
        if (sourceGraph is null)
        {
            return Vocabulary.Iri(Namespace + DefaultSegment);
        }

        if (sourceGraph.Kind != RdfTermKind.Iri)
        {
            throw new ArgumentException("A source graph is named by an IRI.", nameof(sourceGraph));
        }

        return Vocabulary.Iri(Namespace + Encode(sourceGraph.Lexical));
    }

    /// <summary>Whether <paramref name="graph"/>'s name is under the catalogue namespace, a catalogue or not.</summary>
    public static bool IsUnderNamespace(RdfTerm graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.Kind == RdfTermKind.Iri && graph.Lexical.StartsWith("https://w3id.org/varve/ns#events-catalogue/"u8);
    }

    /// <summary>
    /// When <paramref name="graph"/> is a catalogue graph, the source graph it
    /// catalogues — <see langword="null"/> for the default graph — and
    /// <see langword="true"/>. A name under the namespace that does not decode
    /// to <c>default</c> or to an absolute IRI is no catalogue.
    /// </summary>
    public static bool TryGetSourceGraph(RdfTerm graph, out RdfTerm? sourceGraph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        sourceGraph = null;

        if (!IsUnderNamespace(graph))
        {
            return false;
        }

        ReadOnlySpan<byte> segment = graph.Lexical["https://w3id.org/varve/ns#events-catalogue/"u8.Length..];

        if (segment.SequenceEqual("default"u8))
        {
            return true;
        }

        if (!TryDecode(segment, out byte[]? iri) || !IsAbsolute(iri))
        {
            return false;
        }

        sourceGraph = RdfTerm.Iri(iri);

        // Only the canonical encoding names a catalogue: %3a and %3A would
        // otherwise be two graphs governed by one source.
        return GraphOf(sourceGraph).Lexical.SequenceEqual(graph.Lexical);
    }

    /// <summary>Percent-encodes every byte outside RFC 3986's unreserved set, with upper-case hex.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    internal static string Encode(ReadOnlySpan<byte> utf8)
    {
        StringBuilder text = new(utf8.Length * 3);

        foreach (byte b in utf8)
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                text.Append((char)b);
            }
            else
            {
                text.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 0xF]);
            }
        }

        return text.ToString();
    }

    private static bool TryDecode(ReadOnlySpan<byte> segment, [NotNullWhen(true)] out byte[]? decoded)
    {
        decoded = null;
        byte[] buffer = new byte[segment.Length];
        int length = 0;

        for (int i = 0; i < segment.Length; i++)
        {
            byte b = segment[i];

            if (b == (byte)'%')
            {
                if (i + 2 >= segment.Length || !IsHex(segment[i + 1]) || !IsHex(segment[i + 2]))
                {
                    return false;
                }

                buffer[length++] = (byte)((HexValue(segment[i + 1]) << 4) | HexValue(segment[i + 2]));
                i += 2;
            }
            else
            {
                buffer[length++] = b;
            }
        }

        decoded = buffer[..length];

        try
        {
            _ = new UTF8Encoding(false, true).GetString(decoded);
        }
        catch (DecoderFallbackException)
        {
            decoded = null;
            return false;
        }

        return true;
    }

    private static bool IsAbsolute(ReadOnlySpan<byte> iri)
    {
        int colon = iri.IndexOf((byte)':');

        if (colon < 1 || !IsAlpha(iri[0]))
        {
            return false;
        }

        foreach (byte b in iri[..colon])
        {
            if (!(IsAlpha(b) || b is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.'))
            {
                return false;
            }
        }

        foreach (byte b in iri)
        {
            if (b <= 0x20 || b is (byte)'<' or (byte)'>' or (byte)'"' or (byte)'{' or (byte)'}' or (byte)'|' or (byte)'\\' or (byte)'^' or (byte)'`')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAlpha(byte b) => b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    private static bool IsHex(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'A' and <= (byte)'F' or >= (byte)'a' and <= (byte)'f';

    private static int HexValue(byte b) => b <= (byte)'9' ? b - '0' : (b | 0x20) - 'a' + 10;
}
