// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Client.Model;

/// <summary>A Graph Store write's body: bytes in a media type Varve reads.</summary>
[DesignDecision(typeof(ClientSideSetupAndSystemGraphs.OneGraphStoreWriteApi), Scope = ExceptionScope.Boundary)]
public sealed class GraphContent
{
    private GraphContent(ReadOnlyMemory<byte> bytes, string mediaType)
    {
        Bytes = bytes;
        MediaType = mediaType;
    }

    /// <summary>The body.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>Its media type, such as <c>text/turtle</c>.</summary>
    public string MediaType { get; }

    /// <summary>A body of bytes.</summary>
    public static GraphContent FromBytes(ReadOnlyMemory<byte> bytes, string mediaType)
    {
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        return new GraphContent(bytes, mediaType);
    }

    /// <summary>A body of text, sent as UTF-8.</summary>
    public static GraphContent FromText(string text, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        return new GraphContent(Encoding.UTF8.GetBytes(text), mediaType);
    }
}

/// <summary>What a Graph Store write requires of the graph it writes (ADR 0126).</summary>
public readonly record struct GraphCondition
{
    private GraphCondition(long? version, bool absent)
    {
        Version = version;
        Absent = absent;
    }

    /// <summary>No condition: the server's own scope decides (ADR 0126 point 3).</summary>
    public static GraphCondition None => default;

    /// <summary>The graph is at <paramref name="version"/>, or the write is <c>412</c>: <c>If-Match</c>.</summary>
    [DesignDecision(typeof(ClientSideSetupAndSystemGraphs.OneGraphStoreWriteApi), Scope = ExceptionScope.Boundary)]
    public static GraphCondition IfMatch(long version) => new(version, absent: false);

    /// <summary>The graph holds no quad, or the write is <c>412</c>: <c>If-None-Match: *</c>, a create.</summary>
    public static GraphCondition IfAbsent => new(null, absent: true);

    /// <summary>The version an <see cref="IfMatch"/> names.</summary>
    [DesignDecision(typeof(ClientSideSetupAndSystemGraphs.OneGraphStoreWriteApi), Scope = ExceptionScope.Boundary)]
    public long? Version { get; }

    /// <summary>Whether this is <see cref="IfAbsent"/>.</summary>
    [DesignDecision(typeof(ClientSideSetupAndSystemGraphs.OneGraphStoreWriteApi), Scope = ExceptionScope.Boundary)]
    public bool Absent { get; }
}
