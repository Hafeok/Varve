// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>What a write's preconditions decided, before anything was read.</summary>
internal readonly record struct WritePlan(bool Proceed, Position? Expected);

/// <summary>
/// What every write shares (ADR 0094): the preconditions, the commit's
/// metadata, and the mapping of the store's outcome onto HTTP.
/// </summary>
internal static class Writes
{
    /// <summary>
    /// Checks a write's preconditions: no <c>Varve-As-Of</c>, no
    /// <c>If-None-Match</c>, and an <c>If-Match</c> that names the head. A
    /// stale <c>If-Match</c> is <c>412</c> with the head, before anything is
    /// read or parsed; a matching one is the commit's expected position.
    /// </summary>
    internal static async Task<WritePlan> CheckAsync(Exchange exchange)
    {
        HttpContext context = exchange.Context;

        if (!StringValues.IsNullOrEmpty(context.Request.Headers[Preconditions.AsOfHeader]))
        {
            await HttpProblems.BadRequest(context, "A write is made at the head; Varve-As-Of is for reads.").ConfigureAwait(false);
            return default;
        }

        if (!StringValues.IsNullOrEmpty(context.Request.Headers.IfNoneMatch))
        {
            await HttpProblems.BadRequest(context, "A write takes If-Match, the position it expects; If-None-Match is for reads.").ConfigureAwait(false);
            return default;
        }

        switch (Preconditions.Read(context.Request.Headers.IfMatch, out Preconditions.TagList tags))
        {
            case TagCondition.Absent:
            case TagCondition.Any:
                return new WritePlan(true, null);
            case TagCondition.Positions when tags.TrySingle(out Position expected):
                Position head = exchange.Dataset.Head;

                if (expected != head)
                {
                    await PreconditionFailedAsync(exchange, head, expected).ConfigureAwait(false);
                    return default;
                }

                return new WritePlan(true, expected);
            default:
                await HttpProblems.BadRequest(context, "If-Match names one position, as the ETag of every response does.").ConfigureAwait(false);
                return default;
        }
    }

    /// <summary>The commit's agent — the caller, as the host names it — and its cause, the request id.</summary>
    internal static CommitMetadata Metadata(Exchange exchange)
    {
        string id = exchange.Context.TraceIdentifier;
        exchange.Response.Headers[Preconditions.RequestIdHeader] = id;
        return new CommitMetadata
        {
            Agent = exchange.Options.Identity.AgentOf(exchange.Context.User),
            Cause = RdfTerm.Literal(Encoding.UTF8.GetBytes(id)),
        };
    }

    /// <summary>
    /// Answers a write's result: <c>204</c> (or <paramref name="created"/>'s
    /// <c>201</c>) for a commit or no change, <c>409</c>, <c>422</c>, <c>503</c>.
    /// Every one carries the position it leaves the dataset at.
    /// </summary>
    internal static Task AnswerAsync(Exchange exchange, CommitResult result, int created = StatusCodes.Status204NoContent, Position? expected = null)
    {
        HttpContext context = exchange.Context;
        Preconditions.Describe(context.Response, result.Position);

        switch (result.Outcome)
        {
            case CommitOutcome.Committed:
                context.Response.StatusCode = created;
                ProtocolLog.Committed(exchange.Options.Logger, context.Request.Method + " " + context.Request.Path.Value, exchange.Name.Value, result.Position.Value, context.TraceIdentifier);
                return Task.CompletedTask;

            case CommitOutcome.NoChange:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;

            case CommitOutcome.Conflict:
                return HttpProblems.WriteAsync(context, ProblemType.Conflict, "The head is " + result.Position.ToString() + ".", members =>
                {
                    members.Number("position", result.Position.Value);
                    members.Number("headPosition", result.Position.Value);

                    if (expected is Position wanted)
                    {
                        members.Number("expectedPosition", wanted.Value);
                    }
                });

            case CommitOutcome.Rejected:
                return HttpProblems.WriteAsync(context, ProblemType.Rejected, result.Reason, members => members.String("report", Report(result)));

            default:
                return QueryRun.WriteUnavailableAsync(exchange, result.Reason ?? "The dataset cannot commit now.");
        }
    }

    /// <summary><c>403</c> <c>graph-not-writable</c> (ADR 0107): the request changes a graph outside the caller's writable scope; nothing was committed.</summary>
    internal static Task GraphNotWritableAsync(Exchange exchange, RdfTerm? graph)
    {
        string name = graph is null ? "the default graph" : "<" + Encoding.UTF8.GetString(graph.Lexical) + ">";
        return HttpProblems.WriteAsync(exchange.Context, ProblemType.GraphNotWritable, "Nothing was committed; the graph is " + name + ".",
            members => members.String("graph", graph is null ? "default" : Encoding.UTF8.GetString(graph.Lexical)));
    }

    /// <summary><c>412</c>: the expected position is not the head.</summary>
    internal static Task PreconditionFailedAsync(Exchange exchange, Position head, Position expected)
    {
        Preconditions.Describe(exchange.Response, head);
        return HttpProblems.WriteAsync(exchange.Context, ProblemType.PreconditionFailed, "The head is " + head.ToString() + ".", members =>
        {
            members.Number("position", head.Value);
            members.Number("headPosition", head.Value);
            members.Number("expectedPosition", expected.Value);
        });
    }

    // The validator's report, as N-Triples-style term lines.
    private static string Report(CommitResult result)
    {
        System.Buffers.ArrayBufferWriter<byte> text = new();

        foreach (RdfTerm term in result.Report)
        {
            TermLines.WriteTerm(text, term);
            TermLines.Write(text, "\n"u8);
        }

        return Encoding.UTF8.GetString(text.WrittenSpan);
    }
}
