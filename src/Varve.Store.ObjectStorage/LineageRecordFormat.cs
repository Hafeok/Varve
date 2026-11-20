// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The lineage record's bytes (ADR 0185): JSON an operator can read, written
/// and read by hand so that nothing reflects. Read conservatively (ADR 0217):
/// a format this build does not read, or bytes that do not read, are a
/// record that is not understood, which is a replica and is never written
/// on; a state it does not know is held as written.
/// </summary>
internal static class LineageRecordFormat
{
    private const int Version = 1;

    public static byte[] Write(LineageRecord record)
    {
        if (!record.Understood || record.Source is not { } source)
        {
            throw new ArgumentException("A lineage record this build does not read is never written.", nameof(record));
        }

        ArrayBufferWriter<byte> buffer = new();

        // One line ending on every platform: the record's bytes are its format's (ADR 0216).
        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Version);
            json.WriteString("state", record.State switch
            {
                LineageState.Replica => "replica",
                LineageState.Promoted => "promoted",
                _ => record.StateText ?? throw new ArgumentException("An unknown state is written only as it was read.", nameof(record)),
            });
            json.WriteStartObject("source");
            json.WriteString("store", source.Store.Value);

            if (source.Prefix is { } prefix)
            {
                json.WriteString("prefix", prefix.Value);
            }

            json.WriteString("dataset", source.Dataset.ToString());

            if (source.Fleet is { } fleet)
            {
                json.WriteString("fleet", fleet.ToString());
            }

            json.WriteEndObject();

            if (record.Promoted is { } promoted)
            {
                json.WriteStartObject("promoted");
                json.WriteNumber("epoch", promoted.Epoch.Value);
                json.WriteNumber("position", promoted.Position.Value);
                json.WriteString("hash", promoted.Hash.Value);
                json.WriteString("at", promoted.At.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                json.WriteString("by", promoted.By.Value);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static LineageRecord Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            Utf8JsonReader reader = new(bytes);
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.GetInt32() != Version)
            {
                return new LineageRecord { Understood = false };
            }

            string state = root.GetProperty("state").GetString() ?? throw new FormatException("The lineage record names no state.");
            JsonElement source = root.GetProperty("source");
            LineagePromotion? promotion = null;

            if (root.TryGetProperty("promoted", out JsonElement promoted))
            {
                promotion = new LineagePromotion
                {
                    Epoch = new WriterEpoch(promoted.GetProperty("epoch").GetInt64()),
                    Position = new Position(promoted.GetProperty("position").GetInt64()),
                    Hash = new CommitHeaderHash(promoted.GetProperty("hash").GetString()!),
                    At = new ClaimTime(DateTimeOffset.Parse(promoted.GetProperty("at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
                    By = new WriterInstance(promoted.GetProperty("by").GetString()!),
                };
            }

            LineageState read = state switch { "replica" => LineageState.Replica, "promoted" => LineageState.Promoted, _ => LineageState.Unknown };

            // A promoted record with no promotion says less than it claims: it is not one this build reads.
            if (read == LineageState.Promoted && promotion is null)
            {
                return new LineageRecord { Understood = false };
            }

            return new LineageRecord
            {
                Understood = true,
                State = read,
                StateText = state,
                Source = new LineageSource(
                    new StoreAddress(source.GetProperty("store").GetString()!),
                    source.TryGetProperty("prefix", out JsonElement prefix) ? new ObjectName(prefix.GetString()!) : null,
                    new DatasetId(Guid.ParseExact(source.GetProperty("dataset").GetString()!, "D")),
                    source.TryGetProperty("fleet", out JsonElement fleet) ? new DatasetId(Guid.ParseExact(fleet.GetString()!, "D")) : null),
                Promoted = promotion,
            };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or ArgumentException or KeyNotFoundException)
        {
            return new LineageRecord { Understood = false };
        }
    }
}
