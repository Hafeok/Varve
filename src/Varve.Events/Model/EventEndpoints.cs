// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Events.Model;

/// <summary>
/// The URLs a dataset's events and schemas carry (docs/spec/events.md §4.4,
/// §6.2): <c>{base}datasets/{name}</c> as the CloudEvents <c>source</c>, and the
/// schema URLs under it. One base, one set of bytes: byte identity holds per
/// base (ADR 0143, <c>SourceIsConfiguredOrTheRequestBase</c>).
/// </summary>
[DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
public sealed class EventEndpoints
{
    /// <summary>The URLs of dataset <paramref name="dataset"/> under <paramref name="baseUrl"/>, an absolute URL ending in <c>/</c>.</summary>
    public EventEndpoints(string baseUrl, string dataset)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);
        ArgumentException.ThrowIfNullOrEmpty(dataset);

        // An http or https URL ending in '/'; the scheme, the authority and the
        // path are taken apart by hand, since System.Uri is not used for an
        // IRI here (ADR 0004).
        int schemeEnd = baseUrl.IndexOf("://", StringComparison.Ordinal);
        string scheme = schemeEnd > 0 ? baseUrl[..schemeEnd] : string.Empty;
        int pathStart = schemeEnd > 0 ? baseUrl.IndexOf('/', schemeEnd + 3) : -1;

        if (scheme is not ("http" or "https") || pathStart <= schemeEnd + 3 || !baseUrl.EndsWith('/') || baseUrl.AsSpan().ContainsAny(" \"<>"))
        {
            throw new ArgumentException("The base is an absolute http or https URL ending in '/'.", nameof(baseUrl));
        }

        Scheme = scheme;
        Host = baseUrl[(schemeEnd + 3)..pathStart];
        BasePath = baseUrl[pathStart..];
        Base = baseUrl;
        Dataset = dataset;
        Source = baseUrl + "datasets/" + dataset;
    }

    /// <summary>The base's scheme, <c>http</c> or <c>https</c>.</summary>
    public string Scheme { get; }

    /// <summary>The base's authority: its host and port.</summary>
    public string Host { get; }

    /// <summary>The base's path, starting and ending with <c>/</c>.</summary>
    public string BasePath { get; }

    /// <summary>The base, ending in <c>/</c>.</summary>
    public string Base { get; }

    /// <summary>The dataset's name.</summary>
    public string Dataset { get; }

    /// <summary>The dataset's URL: the CloudEvents <c>source</c>.</summary>
    public string Source { get; }

    /// <summary>The events resource.</summary>
    public string Events => Source + "/events";

    /// <summary>The dataset's list of types.</summary>
    public string Types => Source + "/events/types";

    /// <summary>The AsyncAPI document.</summary>
    public string AsyncApi => Source + "/events/asyncapi";

    /// <summary>The latest revision of the current version of <paramref name="type"/>.</summary>
    public string Schema(string type) => Types + "/" + type;

    /// <summary>The latest revision of <paramref name="version"/> of <paramref name="type"/>.</summary>
    public string Schema(string type, int version) =>
        Schema(type) + "/versions/" + version.ToString(CultureInfo.InvariantCulture);

    /// <summary>Revision <paramref name="revision"/> of <paramref name="version"/> of <paramref name="type"/>: an immutable schema's <c>$id</c>.</summary>
    public string Schema(string type, int version, SchemaRevisionId revision) =>
        Schema(type, version) + "/revisions/" + revision.ToString();
}
