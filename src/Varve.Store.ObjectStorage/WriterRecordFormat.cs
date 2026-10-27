// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
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
                json.WriteString("released", Name(released));
            }

            json.WriteStartArray("history");

            foreach (EpochClaim claim in record.History)
            {
                json.WriteStartObject();
                json.WriteNumber("epoch", claim.Epoch.Value);
                json.WriteString("holder", claim.Holder.Value);
                json.WriteString("trigger", Name(claim.Trigger));
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
                    json.WriteString("released", Name(reason));
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <exception cref="InvalidDataException">The bytes are not a writer record this version reads.</exception>
    public static WriterRecord Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement root = document.RootElement;

            if (root.GetProperty("format").GetInt32() != Version)
            {
                throw new InvalidDataException("The writer record is of format " + root.GetProperty("format").GetInt32() + "; this version of Varve reads format " + Version + ".");
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
                        "abdication" => ClaimTrigger.Abdication,
                        "probe" => ClaimTrigger.Probe,
                        string other => throw new InvalidDataException("The writer record names a trigger '" + other + "' this version does not know."),
                        null => throw new InvalidDataException("A claim in the writer record has no trigger."),
                    },
                    ClaimedAt = new ClaimTime(DateTimeOffset.Parse(claim.GetProperty("at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
                    Fence = claim.TryGetProperty("fence", out JsonElement fence) ? new LogSlot(fence.GetString()!) : null,
                    Address = claim.TryGetProperty("address", out JsonElement address) ? new WriterAddress(address.GetString()!) : null,
                    Released = claim.TryGetProperty("released", out JsonElement released) ? Reason(released.GetString()) : null,
                });
            }

            return new WriterRecord
            {
                Epoch = new WriterEpoch(root.GetProperty("epoch").GetInt64()),
                Holder = new WriterInstance(root.GetProperty("holder").GetString()!),
                Released = root.TryGetProperty("released", out JsonElement reason) ? Reason(reason.GetString()) : null,
                History = history,
            };
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidDataException("The writer record does not read: " + error.Message, error);
        }
    }

    /// <summary>A release reason's name in the record (ADRs 0136, 0179).</summary>
    internal static string Name(ReleaseReason reason) => reason switch
    {
        ReleaseReason.Shutdown => "shutdown",
        ReleaseReason.OperatorTakeover => "operator-takeover",
        ReleaseReason.Placement => "placement",
        ReleaseReason.StorageError => "storage-error",
        ReleaseReason.LocalDiskError => "local-disk-error",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No such reason."),
    };

    /// <summary>A claim trigger's name in the record (ADRs 0136, 0179).</summary>
    internal static string Name(ClaimTrigger trigger) => trigger switch
    {
        ClaimTrigger.Restart => "restart",
        ClaimTrigger.Operator => "operator",
        ClaimTrigger.Abdication => "abdication",
        ClaimTrigger.Probe => "probe",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "No such trigger."),
    };
    private static ReleaseReason Reason(string? name) => name switch
    {
        "shutdown" => ReleaseReason.Shutdown,
        "operator-takeover" => ReleaseReason.OperatorTakeover,
        "placement" => ReleaseReason.Placement,
        "storage-error" => ReleaseReason.StorageError,
        "local-disk-error" => ReleaseReason.LocalDiskError,
        _ => throw new InvalidDataException("The writer record names a release reason '" + name + "' this version does not know."),
    };
}
