// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Varve.Protocol.Http;

/// <summary>One format an endpoint can write: its media type and what selects the writer.</summary>
internal readonly record struct Offer<T>(string MediaType, T Format);

/// <summary>
/// Proactive negotiation over <c>Accept</c> (RFC 9110 §12.5.1): the offer with
/// the highest quality wins, the most specific matching range decides an
/// offer's quality, and the server's order breaks ties. No <c>Accept</c>, or
/// one that parses to nothing, takes the first offer (ADR 0092).
/// </summary>
internal static class Negotiation
{
    internal static bool TryChoose<T>(StringValues accept, ImmutableArray<Offer<T>> offers, out Offer<T> chosen)
    {
        chosen = offers[0];

        if (StringValues.IsNullOrEmpty(accept)
            || !MediaTypeHeaderValue.TryParseList(accept, out IList<MediaTypeHeaderValue>? ranges)
            || ranges.Count == 0)
        {
            return true;
        }

        double best = 0;
        bool found = false;

        foreach (Offer<T> offer in offers)
        {
            double quality = QualityOf(offer.MediaType, ranges);

            if (quality > best)
            {
                best = quality;
                chosen = offer;
                found = true;
            }
        }

        return found;
    }

    // The quality of the most specific range that matches: type/subtype over
    // type/* over */*, and among equals the one with the most parameters.
    private static double QualityOf(string mediaType, IList<MediaTypeHeaderValue> ranges)
    {
        int slash = mediaType.IndexOf('/', StringComparison.Ordinal);
        ReadOnlySpan<char> type = mediaType.AsSpan(0, slash);
        ReadOnlySpan<char> subtype = mediaType.AsSpan(slash + 1);
        int specificity = -1;
        double quality = 0;

        foreach (MediaTypeHeaderValue range in ranges)
        {
            int score;
            ReadOnlySpan<char> rangeType = range.Type.AsSpan();
            ReadOnlySpan<char> rangeSubtype = range.SubType.AsSpan();

            if (rangeType.Equals("*", StringComparison.Ordinal) && rangeSubtype.Equals("*", StringComparison.Ordinal))
            {
                score = 0;
            }
            else if (!rangeType.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            else if (rangeSubtype.Equals("*", StringComparison.Ordinal))
            {
                score = 1;
            }
            else if (rangeSubtype.Equals(subtype, StringComparison.OrdinalIgnoreCase))
            {
                score = 2;
            }
            else
            {
                continue;
            }

            if (score > specificity)
            {
                specificity = score;
                quality = range.Quality ?? 1.0;
            }
        }

        return quality;
    }
}
