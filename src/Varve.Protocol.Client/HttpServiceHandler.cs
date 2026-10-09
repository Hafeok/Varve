// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Sparql.Results;

namespace Varve.Protocol.Client;

/// <summary>
/// <c>SERVICE</c> over HTTP (ADR 0104): the pattern sent to the endpoint as
/// <c>SELECT … WHERE { … }</c> by the SPARQL 1.1 Protocol, the answer read
/// back as SPARQL results JSON or XML. The endpoint is checked against the
/// policy first. A failure is returned, never thrown: the evaluator errors
/// without <c>SILENT</c> and answers one empty solution with it (ADR 0055,
/// Federated Query §2.3).
/// </summary>
public sealed class HttpServiceHandler : IServiceHandler
{
    private const string ResultsJson = "application/sparql-results+json";
    private const string ResultsXml = "application/sparql-results+xml";

    private readonly HttpClient _http;
    private readonly EndpointPolicy _policy;
    private readonly ClientLimits _limits;

    /// <summary>A handler over <paramref name="http"/>, reaching what <paramref name="policy"/> allows, within <paramref name="limits"/>.</summary>
    public HttpServiceHandler(HttpClient http, EndpointPolicy policy, ClientLimits limits)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(policy);
        _http = http;
        _policy = policy;
        _limits = limits;
    }

    /// <inheritdoc />
    public ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string endpoint = Encoding.UTF8.GetString(request.Endpoint.Lexical);

        if (!_policy.Allows(request.Endpoint.Lexical, out string? refused))
        {
            return ServiceResult.Failed("the endpoint policy refuses <" + endpoint + ">: " + refused);
        }

        SelectQuery query = new(Prologue.Empty, null, new Project(request.Pattern.Inner, AlgebraList.From(request.Variables)));
        ArrayBufferWriter<byte> text = new(256);
        SparqlWriter.Write(query, text);

        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(endpoint, UriKind.Absolute))
        {
            Content = new ReadOnlyMemoryContent(text.WrittenMemory),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/sparql-query");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ResultsJson, 1.0));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ResultsXml, 0.9));

        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_limits.Timeout);
        HttpResponseMessage response;

        try
        {
            response = _http.Send(message, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
        }
        catch (HttpRequestException error)
        {
            return ServiceResult.Failed("<" + endpoint + "> could not be reached: " + error.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult.Failed("<" + endpoint + "> did not answer within " + _limits.Timeout.ToString() + ".");
        }

        using (response)
        {
            if (Answered(response, endpoint) is string refusal)
            {
                return ServiceResult.Failed(refusal);
            }

            string? failure;

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            SparqlResultsFormat format;

            if (string.Equals(mediaType, ResultsJson, StringComparison.OrdinalIgnoreCase))
            {
                format = SparqlResultsFormat.Json;
            }
            else if (string.Equals(mediaType, ResultsXml, StringComparison.OrdinalIgnoreCase))
            {
                format = SparqlResultsFormat.Xml;
            }
            else
            {
                return ServiceResult.Failed("<" + endpoint + "> answered " + (mediaType ?? "no media type") + ", not SPARQL results JSON or XML.");
            }

            byte[]? body;

            try
            {
                body = ResponseBodies.Read(response.Content, _limits.MaxResponseBytes, bounded.Token, out failure);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ServiceResult.Failed("<" + endpoint + "> did not answer within " + _limits.Timeout.ToString() + ".");
            }
            catch (HttpRequestException error)
            {
                return ServiceResult.Failed("<" + endpoint + ">'s answer was cut: " + error.Message);
            }

            return body is null ? ServiceResult.Failed("<" + endpoint + ">: " + failure) : Parse(endpoint, body, format);
        }
    }

    /// <summary>Why the response is not an answer, or null when it is one.</summary>
    private static string? Answered(HttpResponseMessage response, string endpoint)
    {
        int status = (int)response.StatusCode;

        if (status is >= 300 and < 400)
        {
            return "<" + endpoint + "> answered a redirect (" + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + "), which is not followed.";
        }

        return response.IsSuccessStatusCode
            ? null
            : "<" + endpoint + "> answered " + status.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + response.ReasonPhrase + ".";
    }

    private static ServiceResult Parse(string endpoint, byte[] body, SparqlResultsFormat format)
    {
        SparqlResultsReader reader = new(body, format);

        if (!reader.ReadHead())
        {
            return ServiceResult.Failed("<" + endpoint + ">'s results do not parse: " + reader.Error.Message);
        }

        if (reader.IsBoolean)
        {
            return ServiceResult.Failed("<" + endpoint + "> answered a boolean result to a SELECT.");
        }

        IReadOnlyList<string> names = reader.Variables;
        Variable[] variables = new Variable[names.Count];

        for (int i = 0; i < variables.Length; i++)
        {
            variables[i] = new Variable(names[i]);
        }

        List<IReadOnlyList<RdfTerm?>> rows = [];

        while (reader.Read())
        {
            SolutionView solution = reader.Current;
            RdfTerm?[] row = new RdfTerm?[variables.Length];

            for (int i = 0; i < row.Length; i++)
            {
                row[i] = solution.TryGet(i, out RdfTermView term) ? term.Materialise() : null;
            }

            rows.Add(row);
        }

        return reader.Error.IsError
            ? ServiceResult.Failed("<" + endpoint + ">'s results do not parse: " + reader.Error.Message)
            : ServiceResult.FromSolutions(variables, rows);
    }
}
