// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client;

namespace Varve.Server.Commands;

/// <summary>
/// <c>varve promote</c> (ADR 0185 decision 4): the client of the promotion
/// request. It asks one instance of the fleet that serves a replica to
/// promote it, stating the source the operator says it is a copy of and the
/// position they mean to keep. The instance answers for itself and the
/// request is never forwarded, so the command names the instance.
/// </summary>
/// <remarks>
/// Nothing in Varve promotes a replica by itself (decision 1). The command
/// sends one request and reports its answer: it does not retry, wait, or
/// choose another instance.
/// </remarks>
[Contract(typeof(PromotionAcrossCopies.PromotionIsAnOperatorsAct), Role = "the command an operator promotes a replica with")]
internal static class Promote
{
    internal static Command Command(Io io)
    {
        Argument<string> dataset = new("dataset") { Description = "The replica to promote, by its name in the fleet that serves it." };
        Option<string> server = new("--server") { Description = "The instance to ask, by its address: the one the dataset is placed on. The request is never forwarded.", Required = true };
        Option<string> source = new("--source") { Description = "The store address of the dataset this is a copy of, as its lineage names it. A copy of another source is not promoted.", Required = true };
        Option<long> position = new("--position") { Description = "The last position of the copy you mean to keep. A copy whose tail is elsewhere is not promoted, and the answer says where it is.", Required = true };
        Command promote = new("promote", "Promote a replica: make a shipped copy of a dataset the one that is written (ADR 0185). An operator's act; nothing does it by itself.");
        promote.Arguments.Add(dataset);
        promote.Options.Add(server);
        promote.Options.Add(source);
        promote.Options.Add(position);
        Manage.OperatorToken credentials = new(promote);

        promote.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            using HttpClient http = OutboundHttp.CreateClient();
            string? bearer = await credentials.TokenAsync(parsed, http, cancellationToken).ConfigureAwait(false);
            return await AskAsync(http, bearer, parsed.GetValue(server)!.TrimEnd('/'), parsed.GetValue(dataset)!, parsed.GetValue(source)!, parsed.GetValue(position), io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));

        return promote;
    }

    /// <summary>Sends the promotion request and writes its answer; whether the replica was promoted.</summary>
    internal static async Task<bool> AskAsync(HttpClient http, string? bearer, string server, string dataset, string source, long position, Io io, CancellationToken cancellationToken)
    {
        if (position < 0)
        {
            throw new Cli.CommandException("--position is a position of the copy's log, zero or more.");
        }

        using System.IO.MemoryStream body = new();

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteStartObject("promote");
            json.WriteString("source", source);
            json.WriteNumber("position", position);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(server + "/manage/datasets/" + Uri.EscapeDataString(dataset) + "/state", UriKind.Absolute))
        {
            Content = new ByteArrayContent(body.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string answer = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            using JsonDocument promoted = JsonDocument.Parse(answer);
            await io.Out.WriteLineAsync(
                dataset + ": promoted at epoch " + promoted.RootElement.GetProperty("epoch").GetInt64().ToString(CultureInfo.InvariantCulture)
                + ". It is written from position " + promoted.RootElement.GetProperty("position").GetInt64().ToString(CultureInfo.InvariantCulture) + " on, by the instance at " + server + ".").ConfigureAwait(false);
            return true;
        }

        await io.Error.WriteLineAsync(dataset + ": not promoted. " + Said(answer, (int)response.StatusCode)).ConfigureAwait(false);
        return false;
    }

    // The problem's reason and detail, or the status where the answer is not a problem.
    private static string Said(string answer, int status)
    {
        try
        {
            using JsonDocument problem = JsonDocument.Parse(answer);
            JsonElement root = problem.RootElement;
            StringBuilder said = new();

            if (root.TryGetProperty("reason", out JsonElement reason))
            {
                said.Append(reason.GetString()).Append(": ");
            }

            said.Append(root.TryGetProperty("detail", out JsonElement detail) ? detail.GetString() : root.TryGetProperty("title", out JsonElement title) ? title.GetString() : "the server answered " + status.ToString(CultureInfo.InvariantCulture));
            return said.ToString();
        }
        catch (JsonException)
        {
            return "The server answered " + status.ToString(CultureInfo.InvariantCulture) + ".";
        }
    }
}
