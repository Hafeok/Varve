// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Store.Model;

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
[Contract(typeof(SparqlUpdateOneRequestOneCommit.LoadThroughALoadSource), Role = "what LOAD fetches a document through")]
public interface ILoadSource
{
    /// <summary>The document the IRI names, or a failure.</summary>
    ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken);
}

/// <summary>The default: every IRI refused, naming it. Nothing is fetched.</summary>
internal sealed class RefusingLoadSource : ILoadSource
{
    internal static RefusingLoadSource Instance { get; } = new();

    public ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LoadedDocument.Failed(
            "No load source is configured, so <" + System.Text.Encoding.UTF8.GetString(iri.Lexical) + "> was not fetched; set UpdateOptions.LoadSource."));
}
