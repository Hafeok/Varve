// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Turtle;

namespace Varve.Sparql.Store.Model;

/// <summary>A document for <c>LOAD</c>: its bytes and syntax, or why there is none.</summary>
public sealed class LoadedDocument
{
    private LoadedDocument(ReadOnlyMemory<byte> content, RdfSyntax syntax, ReadOnlyMemory<byte> baseIri, string? failure)
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

    /// <summary>The IRI relative references in it resolve against.</summary>
    public ReadOnlyMemory<byte> BaseIri { get; }

    /// <summary>Why there is no document, or null when there is one.</summary>
    [DesignDecision(typeof(ModelNamespacesForLayers3To5.FailureTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Failure { get; }

    /// <summary>A document to parse.</summary>
    public static LoadedDocument Of(ReadOnlyMemory<byte> content, RdfSyntax syntax, ReadOnlyMemory<byte> baseIri) =>
        new(content, syntax, baseIri, null);

    /// <summary>No document, and why.</summary>
    public static LoadedDocument Failed(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new LoadedDocument(default, default, default, reason);
    }
}
