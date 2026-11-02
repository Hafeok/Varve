// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The writer record's bytes: JSON an operator can read, written and read by
/// hand so that nothing reflects (ADR 0136).
/// </summary>
internal static class WriterRecordFormat
{
    private const int Version = 1;

    public static byte[] Write(WriterRecord record)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Version);
            json.WriteNumber("epoch", record.Epoch.Value);
            json.WriteString("holder", record.Holder.Value);

            if (record.Released is { } released)
            {
                json.WriteString("released", Name(released, record.ReleasedText));
            }

            json.WriteStartArray("history");

            foreach (EpochClaim claim in record.History)
            {
                json.WriteStartObject();
                json.WriteNumber("epoch", claim.Epoch.Value);
                json.WriteString("holder", claim.Holder.Value);
                json.WriteString("trigger", claim.Trigger switch
                {
                    ClaimTrigger.Operator => "operator",
                    ClaimTrigger.Restart => "restart",
                    _ => claim.TriggerText ?? throw new ArgumentOutOfRangeException(nameof(record), claim.Trigger, "An unknown trigger is written only as it was read."),
                });
                json.WriteString("at", claim.ClaimedAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

                if (claim.Fence is { } fence)
                {
                    json.WriteString("fence", fence.Value);
                }

                if (claim.Address is { } address)
                {
                    json.WriteString("address", address.Value);
                }

                if (claim.Released is { } reason)
                {
                    json.WriteString("released", Name(reason, claim.ReleasedText));
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Reads a writer record conservatively (ADR 0217): a format this version
    /// does not read is <see cref="WriterRecordFormatUnknownException"/>, on
    /// which nothing is written; a trigger or a release reason it does not know
    /// is held as <see cref="ClaimTrigger.Unknown"/> or <see cref="ReleaseReason.Unknown"/>,
    /// with its text kept, and never acted on.
    /// </summary>
    /// <exception cref="WriterRecordFormatUnknownException">The record is of a format this version does not read.</exception>
    /// <exception cref="InvalidDataException">The bytes are not a writer record.</exception>
    [DesignDecision(typeof(CoordinationRecordsReadConservatively.AnUnknownWriterRecordFormatIsNeverWritten), Scope = ExceptionScope.Boundary)]
    [DesignDecision(typeof(CoordinationRecordsReadConservatively.AnUnknownValueIsHeldNeverActedOn), Scope = ExceptionScope.Boundary)]
    public static WriterRecord Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement root = document.RootElement;

            if (root.GetProperty("format").GetInt32() is int format && format != Version)
            {
                throw new WriterRecordFormatUnknownException(format, Version);
            }

            List<EpochClaim> history = [];

            foreach (JsonElement claim in root.GetProperty("history").EnumerateArray())
            {
                history.Add(new EpochClaim
                {
                    Epoch = new WriterEpoch(claim.GetProperty("epoch").GetInt64()),
                    Holder = new WriterInstance(claim.GetProperty("holder").GetString()!),
                    Trigger = claim.GetProperty("trigger").GetString() switch
                    {
                        "restart" => ClaimTrigger.Restart,
                        "operator" => ClaimTrigger.Operator,
                        string => ClaimTrigger.Unknown,
                        null => throw new InvalidDataException("A claim in the writer record has no trigger."),
                    },
                    TriggerText = claim.GetProperty("trigger").GetString(),
                    ClaimedAt = new ClaimTime(DateTimeOffset.Parse(claim.GetProperty("at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
                    Fence = claim.TryGetProperty("fence", out JsonElement fence) ? new LogSlot(fence.GetString()!) : null,
                    Address = claim.TryGetProperty("address", out JsonElement address) ? new WriterAddress(address.GetString()!) : null,
                    Released = claim.TryGetProperty("released", out JsonElement released) ? Reason(released.GetString()) : null,
                    ReleasedText = claim.TryGetProperty("released", out JsonElement releasedText) ? releasedText.GetString() : null,
                });
            }

            return new WriterRecord
            {
                Epoch = new WriterEpoch(root.GetProperty("epoch").GetInt64()),
                Holder = new WriterInstance(root.GetProperty("holder").GetString()!),
                Released = root.TryGetProperty("released", out JsonElement reason) ? Reason(reason.GetString()) : null,
                ReleasedText = root.TryGetProperty("released", out JsonElement reasonText) ? reasonText.GetString() : null,
                History = history,
            };
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidDataException("The writer record does not read: " + error.Message, error);
        }
    }

    private static string Name(ReleaseReason reason, string? text) => reason switch
    {
        ReleaseReason.Shutdown => "shutdown",
        ReleaseReason.OperatorTakeover => "operator-takeover",
        ReleaseReason.Placement => "placement",
        _ => text ?? throw new ArgumentOutOfRangeException(nameof(reason), reason, "An unknown reason is written only as it was read."),
    };

    private static ReleaseReason Reason(string? name) => name switch
    {
        "shutdown" => ReleaseReason.Shutdown,
        "operator-takeover" => ReleaseReason.OperatorTakeover,
        "placement" => ReleaseReason.Placement,
        null => throw new InvalidDataException("The writer record names a release with no reason."),
        _ => ReleaseReason.Unknown,
    };
}
