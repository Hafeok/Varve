// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Sparql.Store;

/// <summary>
/// Resolves the IRI of a <c>LOAD</c> to a document (SPARQL 1.1 Update §3.1.4,
/// <c>sparql-update-store.md</c> §6.3).
/// </summary>
/// <remarks>
/// The package ships only the refusing default, <see cref="UpdateOptions.LoadSource"/>'s:
/// fetching over HTTP is the server's (milestone 7), and reading files is the
/// host's to allow. A failure is a <see cref="LoadedDocument.Failed"/> result
/// or an exception; either fails the operation, and <c>LOAD SILENT</c> makes
/// it an operation with no effect.
/// </remarks>
public interface ILoadSource
{
    /// <summary>The document the IRI names, or a failure.</summary>
    ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken);
}

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

/// <summary>The default: every IRI refused, naming it. Nothing is fetched.</summary>
internal sealed class RefusingLoadSource : ILoadSource
{
    internal static RefusingLoadSource Instance { get; } = new();

    public ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LoadedDocument.Failed(
            "No load source is configured, so <" + System.Text.Encoding.UTF8.GetString(iri.Lexical) + "> was not fetched; set UpdateOptions.LoadSource."));
}
