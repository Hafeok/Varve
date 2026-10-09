// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Turtle;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A document <see cref="RdfDocumentClient"/> fetched: its bytes, the syntax
/// its media type named, and the IRI its relative references resolve against
/// — or why there is none (ADR 0104). A host turns it into the update
/// executor's <c>LoadedDocument</c>.
/// </summary>
public sealed class RdfDocument
{
    private RdfDocument(ReadOnlyMemory<byte> content, RdfSyntax syntax, ReadOnlyMemory<byte> baseIri, string? failure)
    {
        Content = content;
        Syntax = syntax;
        BaseIri = baseIri;
        Failure = failure;
    }

    /// <summary>The document, UTF-8.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>What to parse it as.</summary>
    public RdfSyntax Syntax { get; }

    /// <summary>The IRI relative references in it resolve against: the request's, without a fragment (RFC 3986 §5.1.3).</summary>
    public ReadOnlyMemory<byte> BaseIri { get; }

    /// <summary>Why there is no document, or null when there is one.</summary>
    [DesignDecision(typeof(ModelNamespacesForLayers3To5.FailureTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Failure { get; }

    /// <summary>Whether the fetch failed.</summary>
    public bool IsFailure => Failure is not null;

    /// <summary>A document to parse.</summary>
    public static RdfDocument Of(ReadOnlyMemory<byte> content, RdfSyntax syntax, ReadOnlyMemory<byte> baseIri) =>
        new(content, syntax, baseIri, null);

    /// <summary>No document, and why.</summary>
    public static RdfDocument Failed(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new RdfDocument(default, default, default, reason);
    }
}
