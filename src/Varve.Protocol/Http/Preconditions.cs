// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Varve.Store.Log;

namespace Varve.Protocol.Http;

/// <summary>What an <c>If-Match</c> or <c>If-None-Match</c> header asks.</summary>
internal enum TagCondition : byte
{
    /// <summary>The header is absent.</summary>
    Absent,

    /// <summary><c>*</c>: any position.</summary>
    Any,

    /// <summary>One or more positions, in <see cref="Preconditions.TagList"/>.</summary>
    Positions,

    /// <summary>The header does not parse, or names a tag that is not a position.</summary>
    Malformed,
}

/// <summary>
/// The position as an entity tag (ADRs 0094, 0096): every response that touched
/// a dataset carries <c>ETag: "&lt;position&gt;"</c> and <c>Varve-Position</c>;
/// <c>If-Match</c> is a write's expected position and <c>If-None-Match</c> a
/// read's cheap poll.
/// </summary>
internal static class Preconditions
{
    internal const string PositionHeader = "Varve-Position";
    internal const string AsOfHeader = "Varve-As-Of";
    internal const string RequestIdHeader = "Varve-Request-Id";
    internal const string ErrorTrailer = "Varve-Error";

    /// <summary>Sets <c>Varve-Position</c> and the strong <c>ETag</c> for a position.</summary>
    internal static void Describe(HttpResponse response, Position position)
    {
        string value = position.Value.ToString(CultureInfo.InvariantCulture);
        response.Headers[PositionHeader] = value;
        response.Headers.ETag = "\"" + value + "\"";
    }

    /// <summary>Reads an <c>If-Match</c> or <c>If-None-Match</c> header.</summary>
    internal static TagCondition Read(StringValues header, out TagList tags)
    {
        tags = default;

        if (StringValues.IsNullOrEmpty(header))
        {
            return TagCondition.Absent;
        }

        if (!EntityTagHeaderValue.TryParseList(header, out System.Collections.Generic.IList<EntityTagHeaderValue>? parsed) || parsed.Count == 0)
        {
            return TagCondition.Malformed;
        }

        long[] positions = new long[parsed.Count];

        for (int i = 0; i < parsed.Count; i++)
        {
            EntityTagHeaderValue tag = parsed[i];

            if (tag.Equals(EntityTagHeaderValue.Any))
            {
                return TagCondition.Any;
            }

            ReadOnlySpan<char> text = tag.Tag.AsSpan().Trim('"');

            if (tag.IsWeak || !Model.Instants.IsDecimal(text) || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out positions[i]))
            {
                return TagCondition.Malformed;
            }
        }

        tags = new TagList(positions);
        return TagCondition.Positions;
    }

    /// <summary>The positions a tag header named.</summary>
    internal readonly record struct TagList(long[] Values)
    {
        internal bool Contains(Position position) => Array.IndexOf(Values, position.Value) >= 0;

        /// <summary>The single position an <c>If-Match</c> expects; several are ambiguous for a write.</summary>
        internal bool TrySingle(out Position position)
        {
            position = Values.Length == 1 ? new Position(Values[0]) : default;
            return Values.Length == 1;
        }
    }
}
