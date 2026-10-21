// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// A reader record's bytes (ADR 0160): JSON an operator can read, written and
/// read by hand so that nothing reflects. Format 1 is read for ever; a record
/// of another format, or one that does not read, holds every catalog version.
/// </summary>
internal static class ReaderRecordFormat
{
    private const int Version = 1;

    public static byte[] Write(ReaderRecord record)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", Version);
            json.WriteString("reader", record.Reader.Value);

            if (record.Holder is { } holder)
            {
                json.WriteString("holder", holder.Value);
            }

            json.WriteNumber("catalogOldest", record.Oldest.Value);
            json.WriteNumber("catalogNewest", record.Newest.Value);

            if (record.AnnouncedAt is { } at)
            {
                json.WriteString("at", at.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            }

            json.WriteString("state", record.State == ReaderRecordState.Forgotten ? "forgotten" : "announced");
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>A record's bytes as the deletion rule reads them: one that does not read holds everything.</summary>
    public static ReaderRecord Read(ReaderId name, ReadOnlySpan<byte> bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement root = document.RootElement;

            if (root.GetProperty("format").GetInt32() != Version)
            {
                return Unknown(name);
            }

            return new ReaderRecord
            {
                Reader = new ReaderId(root.GetProperty("reader").GetString()!),
                Holder = root.TryGetProperty("holder", out JsonElement holder) ? new WriterInstance(holder.GetString()!) : null,
                Oldest = new CatalogVersion(root.GetProperty("catalogOldest").GetInt64()),
                Newest = new CatalogVersion(root.GetProperty("catalogNewest").GetInt64()),
                AnnouncedAt = root.TryGetProperty("at", out JsonElement at)
                    ? new ClaimTime(DateTimeOffset.Parse(at.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                    : null,
                State = root.GetProperty("state").GetString() switch
                {
                    "announced" => ReaderRecordState.Announced,
                    "forgotten" => ReaderRecordState.Forgotten,
                    _ => throw new InvalidDataException("A reader record's state is 'announced' or 'forgotten'."),
                },
            };
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or InvalidDataException)
        {
            // Never ignored: a record this build cannot read holds every version.
            return Unknown(name);
        }
    }

    private static ReaderRecord Unknown(ReaderId name) => new()
    {
        Reader = name,
        Oldest = new CatalogVersion(0),
        Newest = CatalogVersion.Newest,
        Understood = false,
    };
}
