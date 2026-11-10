// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Varve.Delta.Model;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// What a response says about where it was read: its status, the dataset
/// position it describes (<c>Varve-Position</c>), the management position the
/// instance had applied (<c>Varve-Management-Position</c>, ADR 0163), and the
/// request id (<c>Varve-Request-Id</c>, ADR 0119).
/// </summary>
/// <remarks>
/// A response that describes no position names none, and the fact is null:
/// a caller says so rather than showing a position the server never gave.
/// </remarks>
public readonly record struct ResponseFacts
{
    /// <summary>The facts of a response with these values.</summary>
    public ResponseFacts(HttpStatusCode status, FeedPosition? position, FeedPosition? managementPosition, RequestId? requestId)
    {
        Status = status;
        Position = position;
        ManagementPosition = managementPosition;
        RequestId = requestId;
    }

    /// <summary>The HTTP status.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>The <c>Varve-Position</c> the response describes, or null.</summary>
    public FeedPosition? Position { get; }

    /// <summary>The <c>Varve-Management-Position</c> the instance answered at, or null.</summary>
    public FeedPosition? ManagementPosition { get; }

    /// <summary>The <c>Varve-Request-Id</c>, or the problem's <c>instance</c> when the headers carried none, or null.</summary>
    public RequestId? RequestId { get; }

    /// <summary>The facts the headers of <paramref name="response"/> state.</summary>
    public static ResponseFacts Of(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new ResponseFacts(
            response.StatusCode,
            PositionOf(response, "Varve-Position"),
            PositionOf(response, "Varve-Management-Position"),
            response.Headers.TryGetValues("Varve-Request-Id", out IEnumerable<string>? ids) && string.Join(",", ids) is { Length: > 0 } id ? new RequestId(id) : null);
    }

    /// <summary>
    /// These facts, with the request id taken from <paramref name="problem"/>'s
    /// <c>instance</c> when the headers carried none: a problem the host writes
    /// before an endpoint runs names its request only there.
    /// </summary>
    public ResponseFacts With(Problem? problem) =>
        RequestId is null && problem?.Instance is RequestId instance
            ? new ResponseFacts(Status, Position, ManagementPosition, instance)
            : this;

    private static FeedPosition? PositionOf(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out IEnumerable<string>? values)
        && long.TryParse(string.Join(string.Empty, values), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? new FeedPosition(value)
            : null;
}
