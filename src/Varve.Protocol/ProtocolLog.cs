// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Varve.Protocol;

/// <summary>
/// What the protocol logs through the host's <see cref="ILogger"/> (ADR
/// 0112): the request id and the position on every commit, so that a log
/// line joins the commit it caused; the request id is also in the hosting
/// scope of every line a request writes.
/// </summary>
internal static partial class ProtocolLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Operation} on '{Dataset}' committed position {Position} for request {RequestId}")]
    internal static partial void Committed(ILogger logger, string operation, string dataset, long position, string requestId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Method} {Path} answered {Status} for request {RequestId}")]
    internal static partial void Refused(ILogger logger, string method, string path, int status, string requestId);
}
