// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// An RFC 9457 problem as the server answered it (ADRs 0092, 0119): its type,
/// title, status, detail and instance, and every other member as it was sent.
/// </summary>
/// <remarks>
/// The client fixes no catalogue of its own. A member the client does not
/// know is kept, so that a problem a newer server answers is still read whole,
/// and a caller shows it rather than nothing (ADR 0203).
/// </remarks>
public sealed class Problem
{
    private Problem(ProblemType type, string title, HttpStatusCode status, string? detail, RequestId? instance, IReadOnlyDictionary<string, JsonElement> members)
    {
        Type = type;
        Title = title;
        Status = status;
        Detail = detail;
        Instance = instance;
        Members = members;
    }

    /// <summary>The problem's type, <see cref="ProblemType.AboutBlank"/> when the body names none.</summary>
    public ProblemType Type { get; }

    /// <summary>The type's title.</summary>
    [DesignDecision(typeof(TypedReadsInTheProtocolClient.ProblemTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string Title { get; }

    /// <summary>The status the body states, or the response's when it states none.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>What went wrong with this request, when the server said.</summary>
    [DesignDecision(typeof(TypedReadsInTheProtocolClient.ProblemTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Detail { get; }

    /// <summary>The request id the server answered as <c>instance</c> (ADR 0119).</summary>
    public RequestId? Instance { get; }

    /// <summary>Every member but the five above, by name.</summary>
    public IReadOnlyDictionary<string, JsonElement> Members { get; }

    /// <summary>
    /// The problem a body holds, or null when it is not a JSON object.
    /// <paramref name="status"/> is the response's, used when the body states none.
    /// </summary>
    public static Problem? Parse(ReadOnlyMemory<byte> body, HttpStatusCode status)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            return document.RootElement.ValueKind == JsonValueKind.Object ? Of(document.RootElement, status) : null;
        }
    }

    // The problem in a parsed object; every element it keeps is a clone, so
    // it outlives the document.
    private static Problem Of(JsonElement root, HttpStatusCode status)
    {
        ProblemType type = ProblemType.AboutBlank;
        string title = string.Empty;
        string? detail = null;
        RequestId? instance = null;
        Dictionary<string, JsonElement> members = new(StringComparer.Ordinal);

        foreach (JsonProperty member in root.EnumerateObject())
        {
            switch (member.Name)
            {
                case "type" when member.Value.ValueKind == JsonValueKind.String && member.Value.GetString() is { Length: > 0 } named:
                    type = new ProblemType(named);
                    break;
                case "title" when member.Value.ValueKind == JsonValueKind.String:
                    title = member.Value.GetString()!;
                    break;
                case "status" when member.Value.ValueKind == JsonValueKind.Number && member.Value.TryGetInt32(out int stated):
                    status = (HttpStatusCode)stated;
                    break;
                case "detail" when member.Value.ValueKind == JsonValueKind.String:
                    detail = member.Value.GetString();
                    break;
                case "instance" when member.Value.ValueKind == JsonValueKind.String && member.Value.GetString() is { Length: > 0 } id:
                    instance = new RequestId(id);
                    break;
                default:
                    members[member.Name] = member.Value.Clone();
                    break;
            }
        }

        return new Problem(type, title, status, detail, instance, members);
    }
}
