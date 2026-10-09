// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Store;
using Varve.Sparql.Store.Model;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// What the server reaches out to (ADRs 0103, 0104), wired at the composition
/// root (ADR 0060): the <c>SERVICE</c> handler and the <c>LOAD</c> source over
/// one outbound client, each under its configured policy and limits. With no
/// allowed endpoint or source, the refusing defaults stay.
/// </summary>
internal sealed class Outbound
{
    private Outbound(IServiceHandler service, ILoadSource load)
    {
        Service = service;
        Load = load;
    }

    internal IServiceHandler Service { get; }

    internal ILoadSource Load { get; }

    internal static Outbound Of(ServerSettings settings, HttpClient http)
    {
        FederationSettings federation = settings.Federation;
        LoadSettings load = settings.Load;

        IServiceHandler service = federation.AllowedEndpoints.Count == 0
            ? RefusingServiceHandler.Instance
            : new HttpServiceHandler(http,
                new EndpointPolicy(federation.AllowedEndpoints, federation.AllowPrivateAddresses),
                new ClientLimits(federation.Timeout, new ByteCount(federation.MaxResponseBytes)));

        ILoadSource source = load.AllowedSources.Count == 0
            ? new UpdateOptions().LoadSource
            : new HttpLoadSource(new RdfDocumentClient(http,
                new EndpointPolicy(load.AllowedSources, load.AllowPrivateAddresses),
                new ClientLimits(load.Timeout, new ByteCount(load.MaxResponseBytes))));

        return new Outbound(service, source);
    }

    /// <summary>The one-line binding of ADR 0104: a fetched document is a loaded document.</summary>
    private sealed class HttpLoadSource(RdfDocumentClient client) : ILoadSource
    {
        public async ValueTask<LoadedDocument> LoadAsync(RdfTerm iri, CancellationToken cancellationToken)
        {
            RdfDocument document = await client.FetchAsync(iri, cancellationToken).ConfigureAwait(false);
            return document.IsFailure ? LoadedDocument.Failed(document.Failure!) : LoadedDocument.Of(document.Content, document.Syntax, document.BaseIri);
        }
    }
}
