// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

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
        Option<string?> server = new("--server") { Description = "The instance to ask, by its address: the one the dataset is placed on. The request is never forwarded." };
        Option<bool> embedded = new("--embedded") { Description = "Where no fleet serves the copy: promote it on the store itself, and release it for the writer that starts next. The dataset is then its store address." };
        Option<string?> endpoint = new("--endpoint") { Description = "--embedded, s3://: the S3-compatible service's endpoint." };
        Option<string?> region = new("--region") { Description = "--embedded, s3://: the region the signature is for." };
        Option<string?> sts = new("--sts-endpoint") { Description = "--embedded, s3://: the STS endpoint web identity is exchanged at; Amazon's when absent." };
        Option<string> source = new("--source") { Description = "The store address of the dataset this is a copy of, as its lineage names it. A copy of another source is not promoted.", Required = true };
        Option<long> position = new("--position") { Description = "The last position of the copy you mean to keep. A copy whose tail is elsewhere is not promoted, and the answer says where it is.", Required = true };
        Command promote = new("promote", "Promote a replica: make a shipped copy of a dataset the one that is written (ADR 0185). An operator's act; nothing does it by itself.");
        promote.Arguments.Add(dataset);
        promote.Options.Add(server);
        promote.Options.Add(source);
        promote.Options.Add(position);
        promote.Options.Add(embedded);
        promote.Options.Add(endpoint);
        promote.Options.Add(region);
        promote.Options.Add(sts);
        Manage.OperatorToken credentials = new(promote);

        promote.SetAction((parsed, cancellationToken) => Cli.GuardAsync(async () =>
        {
            if (parsed.GetValue(embedded))
            {
                if (parsed.GetValue(server) is not null)
                {
                    throw new Cli.CommandException("--embedded acts on the store itself and --server asks an instance: give one. Where a fleet serves the copy, ask its instance.");
                }

                (IObjectStore store, ObjectName prefix) = ObjectLease.Open(parsed.GetValue(dataset)!, parsed.GetValue(endpoint), parsed.GetValue(region), parsed.GetValue(sts));

                using (store as IDisposable)
                {
                    string cache = Directory.CreateTempSubdirectory("varve-promote-").FullName;

                    try
                    {
                        return await EmbeddedAsync(store, prefix, cache, parsed.GetValue(source)!, parsed.GetValue(position), io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
                    }
                    finally
                    {
                        Directory.Delete(cache, recursive: true);
                    }
                }
            }

            using HttpClient http = OutboundHttp.CreateClient();
            string? bearer = await credentials.TokenAsync(parsed, http, cancellationToken).ConfigureAwait(false);
            return await AskAsync(http, bearer, (parsed.GetValue(server) ?? throw new Cli.CommandException("give --server, the instance to ask, or --embedded where no fleet serves the copy.")).TrimEnd('/'), parsed.GetValue(dataset)!, parsed.GetValue(source)!, parsed.GetValue(position), io, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }, parsed.InvocationConfiguration.Error));

        return promote;
    }

    /// <summary>
    /// Promotes a copy that no fleet serves, on the store itself (ADR 0185
    /// decision 5): it opens the copy read-only, replays and verifies its
    /// log, promotes it in place as a member of a fleet would, and then
    /// releases the epoch with the reason <c>promotion</c>, so that the
    /// writer that starts next claims one epoch more. Whether it was promoted.
    /// </summary>
    internal static async Task<bool> EmbeddedAsync(IObjectStore store, ObjectName prefix, string cache, string source, long position, Io io, CancellationToken cancellationToken)
    {
        if (position < 0)
        {
            throw new Cli.CommandException("--position is a position of the copy's log, zero or more.");
        }

        PromotionOutcome outcome;
        long epoch = 0;
        Position tail;

        await using (Backend storage = await Backend.OpenAsync(
            new ObjectStorageOptions
            {
                Store = store,
                Prefix = prefix,
                Cache = new CacheDirectory(cache),
                Access = ObjectStorageAccess.ReadOnly,
                Clock = TimeProvider.System,
                ProbeRandomness = SystemProbeRandomness.Instance,
            },
            cancellationToken).ConfigureAwait(false))
        await using (Dataset replica = await Dataset.OpenFollowerAsync(storage, new DatasetOptions { Clock = TimeProvider.System }, cancellationToken).ConfigureAwait(false))
        {
            tail = replica.Head;
            CommitHeaderHash hash = new(Convert.ToHexStringLower((await replica.HeaderHashAsync(tail, cancellationToken).ConfigureAwait(false)).Span));
            outcome = PromotionOutcome.Lost;
            bool won = await replica.TakeWriteAsync(
                async claim => (outcome = await storage.PromoteInPlaceAsync(
                    new PromotionRequest
                    {
                        Instance = new WriterInstance("varve promote on " + Environment.MachineName),
                        Source = new StoreAddress(source),
                        Expected = new Position(position),
                        Verified = tail,
                        Hash = hash,
                        Randomness = SystemProbeRandomness.Instance,
                    },
                    claim).ConfigureAwait(false)) == PromotionOutcome.Promoted,
                cancellationToken).ConfigureAwait(false);

            if (won)
            {
                epoch = storage.Writer?.Epoch.Value ?? 0;

                // Until the lineage records the fence the promotion has not finished: a failure before it
                // releases for promotion, which no standby races for (decision 6).
                storage.ReleaseOnClose = ReleaseReason.Promotion;

                // The last write: the position and hash at the fence. A shipper mid-round may have added chunks.
                tail = replica.Head;
                await storage.CompletePromotionAsync(tail, new CommitHeaderHash(Convert.ToHexStringLower((await replica.HeaderHashAsync(tail, cancellationToken).ConfigureAwait(false)).Span)), cancellationToken).ConfigureAwait(false);

                // Finished (ADR 0185 decision 5): released as any writer that closes, so the dataset's placed
                // member, or its writer set, starts as after any ordinary release.
                storage.ReleaseOnClose = ReleaseReason.Shutdown;
            }
        }

        if (outcome == PromotionOutcome.Promoted)
        {
            await io.Out.WriteLineAsync(
                "promoted at epoch " + epoch.ToString(CultureInfo.InvariantCulture) + ". The promotion finished at position " + tail.Value.ToString(CultureInfo.InvariantCulture) + Above(tail.Value, position)
                + " It is released for the writer that starts next, which claims one epoch more. It is a replica no longer.").ConfigureAwait(false);
            return true;
        }

        await io.Error.WriteLineAsync("not promoted. " + outcome switch
        {
            PromotionOutcome.NotAReplica => "not-a-replica: it has no lineage record, or one this version does not read. Nothing was changed.",
            PromotionOutcome.SourceMismatch => "source-mismatch: its lineage record names another source than " + source + ". Nothing was changed.",
            PromotionOutcome.PositionMismatch => "position-mismatch: its last position is " + tail.Value.ToString(CultureInfo.InvariantCulture) + ", not the " + position.ToString(CultureInfo.InvariantCulture)
                + " asked for. Nothing was changed; ask again with the position you mean to keep.",
            _ => "epoch-moved: another promotion or another claim came first. Read its lease, and promote again only if it is still a replica.",
        }).ConfigureAwait(false);
        return false;
    }

    // What follows the position a promotion finished at (ADR 0185 decision 4): a full stop, or that it is above
    // the position asked for, and why.
    private static string Above(long finished, long asked) => finished > asked
        ? ", " + (finished - asked).ToString(CultureInfo.InvariantCulture) + " above the " + asked.ToString(CultureInfo.InvariantCulture)
            + " you named: a shipper created those commits on the copy before the fence. The source acknowledged them, and the copy keeps them."
        : ".";

    /// <summary>Sends the promotion request and writes its answer; whether the replica was promoted.</summary>
    internal static async Task<bool> AskAsync(HttpClient http, string? bearer, string server, string dataset, string source, long position, Io io, CancellationToken cancellationToken)
    {
        if (position < 0)
        {
            throw new Cli.CommandException("--position is a position of the copy's log, zero or more.");
        }

        using MemoryStream body = new();

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
            long finished = promoted.RootElement.GetProperty("position").GetInt64();
            await io.Out.WriteLineAsync(
                dataset + ": promoted at epoch " + promoted.RootElement.GetProperty("epoch").GetInt64().ToString(CultureInfo.InvariantCulture)
                + ". The promotion finished at position " + finished.ToString(CultureInfo.InvariantCulture) + Above(finished, position)
                + " It is written from there on, by the instance at " + server + ".").ConfigureAwait(false);
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
